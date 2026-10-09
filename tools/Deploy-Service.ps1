[CmdletBinding()]
param(
    [string]$ConfigurationPath = '',
    [string]$SshKeyPath = '',
    [string]$ServerRecordPath = '',
    [switch]$Upload,
    [switch]$FocusedValidation
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ($PSVersionTable.PSVersion.Major -lt 7) {
    $pwsh = Get-Command pwsh -ErrorAction Stop
    $forwarded = @('-NoProfile', '-File', $PSCommandPath)
    foreach ($entry in $PSBoundParameters.GetEnumerator()) {
        if ($entry.Value -is [System.Management.Automation.SwitchParameter]) {
            if ($entry.Value.IsPresent) { $forwarded += "-$($entry.Key)" }
        } else {
            $forwarded += @("-$($entry.Key)", [string]$entry.Value)
        }
    }
    & $pwsh.Source @forwarded
    exit $LASTEXITCODE
}

. (Join-Path $PSScriptRoot 'ReleaseAutomation.Common.ps1')
. (Join-Path $PSScriptRoot 'ServiceDeployment.Common.ps1')
if ([string]::IsNullOrWhiteSpace($ConfigurationPath)) {
    $ConfigurationPath = Join-Path $projectRoot 'service.private.json'
}

Push-Location $projectRoot
try {
    $version = Get-ReleaseVersion -ProjectRoot $projectRoot
    $commit = Get-ReleaseCommit -ProjectRoot $projectRoot
    if ($Upload) {
        if ((& git branch --show-current | Out-String).Trim() -ne 'main') {
            throw 'Service deployment must run from main.'
        }
        Assert-TrackedWorkingTreeClean -ProjectRoot $projectRoot
        if (-not $FocusedValidation) {
            & (Join-Path $PSScriptRoot 'Verify.ps1') -LockedRestore
            if ($LASTEXITCODE -ne 0) { throw 'Repository verification failed before service deployment.' }
        }
    }

    & (Join-Path $PSScriptRoot 'Test-ServiceDeployment.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Service deployment transaction tests failed.' }
    $stamp = [DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss')
    $output = Join-Path $projectRoot "artifacts\service-releases\$version-$($commit.Substring(0, 8))-$stamp"
    if (Test-Path -LiteralPath $output) { throw 'Service release output already exists.' }
    $publish = Join-Path $output 'publish'
    [IO.Directory]::CreateDirectory($publish) | Out-Null
    $localDotnet = Join-Path $projectRoot '.dotnet\dotnet.exe'
    $dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else {
        (Get-Command dotnet -ErrorAction Stop).Source
    }
    $serviceProject = Join-Path $projectRoot 'src\VRPhoneScreenOverlay.Service\VRPhoneScreenOverlay.Service.csproj'
    # Restore the declared RID set; --runtime would replace it globally and
    # invalidate Protocols/Service locks that contain both Linux and Windows.
    & $dotnet restore $serviceProject --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked Linux service restore failed.' }
    & $dotnet publish $serviceProject --configuration Release --runtime linux-x64 `
        --self-contained true --no-restore --output $publish `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false `
        -p:InvariantGlobalization=true -p:DebugType=None -p:DebugSymbols=false `
        -p:PublishDocumentationFiles=false -p:IsTransformWebConfigDisabled=true `
        -p:StaticWebAssetsEnabled=false `
        "-p:SourceRevisionId=$commit"
    if ($LASTEXITCODE -ne 0) { throw 'Linux service publish failed.' }
    $binary = Join-Path $publish 'VRPhoneScreenOverlay.Service'
    Assert-ServiceBinary -Path $binary
    $publishedFiles = @(Get-ChildItem -LiteralPath $publish -File -Recurse)
    if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].FullName -ne $binary) {
        throw 'Service publication must contain exactly one self-contained executable.'
    }
    $hash = (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash.ToLowerInvariant()
    $reportPath = Join-Path $output 'service-release-result.json'
    $report = [ordered]@{
        schemaVersion = 1
        version = $version
        commit = $commit
        runtime = 'linux-x64'
        binaryBytes = (Get-Item -LiteralPath $binary).Length
        binarySha256 = $hash
        uploaded = $false
        status = 'built-local'
        deploymentDirectory = $null
        previousBinaryRetained = $false
        previousBinarySha256 = $null
        managedDropIn = '20-vrphone-bundle-extraction.conf'
        existingUnitAndOtherDropInsPreserved = $true
        publicHealthVerified = $false
        installedAndRunningHashVerified = $false
    }
    [IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json), [Text.UTF8Encoding]::new($false))

    if ($Upload) {
        # Read only the SSH settings; service deployment does not need a signing key.
        $configuration = Read-DeploymentConfiguration -Path $ConfigurationPath
        $key = Select-ReleaseSetting -Override $SshKeyPath -Configuration $configuration -Name 'SshKeyPath'
        $record = Select-ReleaseSetting -Override $ServerRecordPath -Configuration $configuration -Name 'ServerRecordPath'
        foreach ($path in @($key, $record)) {
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw 'A configured SSH key or server address record is missing.'
            }
        }
        $serviceName = if ($configuration.ContainsKey('ServiceName')) {
            [string]$configuration.ServiceName
        } else { 'vrphonescreen.service' }
        $publicBase = if ($configuration.ContainsKey('PublicBaseUri')) {
            ([string]$configuration.PublicBaseUri).TrimEnd('/')
        } else { '' }
        $publicHealth = "$publicBase/vrphonescreen/api/v1/health"
        if (-not $configuration.ContainsKey('PrivateHealthUri')) { throw 'Configure PrivateHealthUri before deploying.' }
        $privateHealth = [string]$configuration.PrivateHealthUri
        $token = [Guid]::NewGuid().ToString('N')
        $remoteRoot = '/opt/vrphonescreen-service'
        $remoteStage = "$remoteRoot/.service-deploy-$token"
        $remoteScript = New-ServiceDeploymentScript -Token $token -ExpectedHash $hash `
            -PublicHealthUri $publicHealth -PrivateHealthUri $privateHealth -ServiceName $serviceName -InstallDirectory $remoteRoot
        $scriptPath = Join-Path $output 'deploy-service.sh'
        [IO.File]::WriteAllText($scriptPath, $remoteScript, [Text.UTF8Encoding]::new($false))
        $uploadBinary = Join-Path $output 'VRPhoneScreenOverlay.Service.upload'
        Copy-Item -LiteralPath $binary -Destination $uploadBinary
        $ssh = (Get-Command ssh -ErrorAction Stop).Source
        $scp = (Get-Command scp -ErrorAction Stop).Source
        $serverAddress = Get-ServerAddressFromRecord -ServerRecordPath $record
        $target = "root@$serverAddress"
        $sshOptions = @('-i', $key, '-o', 'BatchMode=yes', '-o', 'ConnectTimeout=15',
            '-o', 'ServerAliveInterval=10', '-o', 'ServerAliveCountMax=3', '-o', 'StrictHostKeyChecking=accept-new')
        $remoteApplied = $false
        $report.deploymentDirectory = $remoteStage
        try {
            $preflight = "set -eu; test -d '$remoteRoot'; test ! -L '$remoteRoot'; test -f '$remoteRoot/VRPhoneScreenOverlay.Service'; command -v bash >/dev/null; command -v flock >/dev/null; command -v curl >/dev/null; command -v timeout >/dev/null; systemctl is-active --quiet '$serviceName'; test ! -e '$remoteStage'; install -d -m 0700 -- '$remoteStage'"
            & $ssh @sshOptions $target $preflight
            if ($LASTEXITCODE -ne 0) { throw 'Service deployment preflight failed.' }
            & $scp @sshOptions -- $uploadBinary $scriptPath "${target}:$remoteStage/"
            if ($LASTEXITCODE -ne 0) { throw 'Service transfer failed before activation.' }
            $report.uploaded = $true
            $report.previousBinaryRetained = $null
            $applyOutput = @(& $ssh @sshOptions $target "bash '$remoteStage/deploy-service.sh' apply")
            $applyExitCode = $LASTEXITCODE
            foreach ($line in $applyOutput) { Write-Host $line }
            if ($applyExitCode -ne 0) {
                throw 'Remote activation failed or was not confirmed. The remote transaction retains its backup and rollback state.'
            }
            $remoteApplied = $true
            $report.previousBinaryRetained = $true
            $applyEvidence = ($applyOutput -join "`n")
            if ($applyEvidence -notmatch "(?m)^SERVICE_DEPLOY_APPLIED $hash PREVIOUS=([0-9a-f]{64})\s*$") {
                throw 'Remote activation did not return its verified rollback hash.'
            }
            $report.previousBinarySha256 = $Matches[1]
            $health = Invoke-RestMethod -Uri $publicHealth -TimeoutSec 15 -NoProxy
            if ($health.ok -ne $true) { throw 'Independent public service health verification failed.' }
            $report.publicHealthVerified = $true
            & $ssh @sshOptions $target "bash '$remoteStage/deploy-service.sh' verify"
            if ($LASTEXITCODE -ne 0) { throw 'Installed/running service hash verification failed.' }
            $report.installedAndRunningHashVerified = $true
            $report.status = 'deployed'
        } catch {
            $report.status = 'failed-or-unconfirmed'
            if ($remoteApplied) {
                & $ssh @sshOptions $target "bash '$remoteStage/deploy-service.sh' rollback"
                if ($LASTEXITCODE -eq 0) { $report.status = 'rolled-back' } else {
                    $report.status = 'rollback-unconfirmed'
                }
            }
            throw
        } finally {
            [IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
        }
    }

    [PSCustomObject]@{ Binary = $binary; Report = $reportPath; Sha256 = $hash; Status = $report.status }
} finally {
    Pop-Location
}
