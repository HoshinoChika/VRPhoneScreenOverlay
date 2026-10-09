Set-StrictMode -Version Latest

function Undo-CloudDownloadTest {
    param([Parameter(Mandatory = $true)]$Configuration,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$Stage,
        [Parameter(Mandatory = $true)][string]$OutputDirectory)
    $prefix = $Configuration.RemoteRoot + '/.release-cloud-'
    Assert-SafeRemoteRoot -RemoteRoot $Stage
    if (-not $Stage.StartsWith($prefix, [StringComparison]::Ordinal) -or
        $Stage.Substring($prefix.Length) -notmatch '^[0-9a-f]{32}$' -or $Version -notmatch '[.-]download-test\.[1-9][0-9]*$') {
        throw 'Only the recorded download test transaction can be withdrawn.'
    }
    $ssh = (Get-Command ssh -ErrorAction Stop).Source
    $address = Get-ServerAddressFromRecord -ServerRecordPath $Configuration.ServerRecordPath
    $sshOptions = @('-i', $Configuration.SshKeyPath, '-o', 'BatchMode=yes', '-o', 'ConnectTimeout=15',
        '-o', 'ProxyCommand=none', '-o', 'ServerAliveInterval=10', '-o', 'ServerAliveCountMax=2',
        '-o', 'StrictHostKeyChecking=accept-new')
    # The server refuses rollback if another release has replaced this test.
    & $ssh @sshOptions "root@$address" "python3 '$Stage/Cloud-Publish.py' rollback '$Stage' '$Version'"
    if ($LASTEXITCODE -ne 0) { throw 'Download test withdrawal was not confirmed; no other release was changed.' }
    $online = Join-Path $OutputDirectory 'online-withdrawn.json'
    Invoke-WebRequest -Uri "$($Configuration.PublicBaseUri)/vrphonescreen/api/v1/updates/manifest?channel=beta" `
        -NoProxy -UserAgent 'VRPhoneScreenOverlay/update-client' -OutFile $online -TimeoutSec 30 | Out-Null
    $payload = Read-VerifiedManifestPayload -ManifestPath $online `
        -PublicKeyPath (Join-Path $PSScriptRoot '..\src\VRPhoneScreenOverlay.Update\Resources\update-public-key.pem')
    Assert-ReleaseVersion -Version $payload.version
    if ($payload.schemaVersion -ne 1 -or $payload.channel -ne 'beta' -or $payload.version -eq $Version -or
        $payload.version -match '[.-]download-test\.') { throw 'Public update manifest still indicates a download test.' }
    return [PSCustomObject]@{ WithdrawnVersion = $Version; RestoredVersion = $payload.version; OnlineVerified = $true }
}

function Publish-CloudRelease {
    param(
        [Parameter(Mandatory = $true)]$Configuration,
        [Parameter(Mandatory = $true)][string]$Version,
        [Parameter(Mandatory = $true)][string]$PackagePath,
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [Parameter(Mandatory = $true)][string]$UploadMetadataPath,
        [Parameter(Mandatory = $true)][string]$OutputDirectory,
        [Parameter(Mandatory = $true)][string]$PackageHash,
        [switch]$ProbeOnly,
        [string]$ResumeRemoteStage = ''
    )
    $ssh = (Get-Command ssh -ErrorAction Stop).Source
    $scp = (Get-Command scp -ErrorAction Stop).Source
    $address = Get-ServerAddressFromRecord -ServerRecordPath $Configuration.ServerRecordPath
    $target = "root@$address"
    $sshOptions = @('-i', $Configuration.SshKeyPath, '-o', 'BatchMode=yes', '-o', 'ConnectTimeout=15',
        '-o', 'ProxyCommand=none', '-o', 'ServerAliveInterval=10', '-o', 'ServerAliveCountMax=2',
        '-o', 'StrictHostKeyChecking=accept-new')
    $prefix = $Configuration.RemoteRoot + '/.release-cloud-'
    $stage = if ($ResumeRemoteStage) { $ResumeRemoteStage } else { $prefix + [Guid]::NewGuid().ToString('N') }
    Assert-SafeRemoteRoot -RemoteRoot $stage
    if (-not $stage.StartsWith($prefix, [StringComparison]::Ordinal) -or
        $stage.Substring($prefix.Length) -notmatch '^[0-9a-f]{32}$') { throw 'Unsafe cloud release transaction scope.' }
    Assert-ReleaseVersion -Version $Version
    $activated = $false
    try {
        if ($ResumeRemoteStage) {
            $manifestHash = (Get-FileHash -LiteralPath $ManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
            & $ssh @sshOptions $target "set -eu; test -d '$stage'; test ! -L '$stage'; printf '%s  %s\n' '$manifestHash' '$stage/latest.json' | sha256sum -c -"
            if ($LASTEXITCODE -ne 0) { throw 'The remote transaction belongs to a different signed manifest.' }
        }
        & $ssh @sshOptions $target "set -eu; test ! -L '$stage'; install -d -m 0700 '$stage'"
        if ($LASTEXITCODE -ne 0) { throw 'Cloud metadata publishing preflight failed.' }
        # The full ZIP stays on the publishing PC. Transfer only small metadata.
        & $scp @sshOptions -- $ManifestPath $UploadMetadataPath (Join-Path $PSScriptRoot 'Cloud-Publish.py') "${target}:$stage/"
        if ($LASTEXITCODE -ne 0) { throw 'Cloud metadata transfer failed before activation.' }
        & $ssh @sshOptions $target "python3 '$stage/Cloud-Publish.py' prepare '$stage' '$Version'"
        if ($LASTEXITCODE -ne 0) { throw 'The user-uploaded 115 package is missing or does not match; active version was not changed.' }
        $download = Join-Path $OutputDirectory 'verified-package.zip'
        $uri = "$($Configuration.PublicBaseUri)/vrphonescreen/api/v1/updates/download/$Version"
        if (-not ((Test-Path -LiteralPath $download) -and
            (Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash.ToLowerInvariant() -eq $PackageHash)) {
            Invoke-WebRequest -Uri $uri -NoProxy -UserAgent 'VRPhoneScreenOverlay/update-client' `
                -OutFile $download -TimeoutSec 90 | Out-Null
        }
        if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash.ToLowerInvariant() -ne $PackageHash) {
            throw 'User-uploaded package download hash verification failed; active version was not changed.'
        }
        Assert-PortablePackageLayout -PackagePath $download
        if ($ProbeOnly) { return [PSCustomObject]@{ RemoteStage = $stage; Activated = $false } }
        & $ssh @sshOptions $target "python3 '$stage/Cloud-Publish.py' activate '$stage' '$Version'"
        if ($LASTEXITCODE -ne 0) { throw 'Cloud release activation was not confirmed.' }
        $activated = $true
        $online = Join-Path $OutputDirectory 'online-latest.json'
        Invoke-WebRequest -Uri "$($Configuration.PublicBaseUri)/vrphonescreen/api/v1/updates/manifest?channel=beta" `
            -NoProxy -UserAgent 'VRPhoneScreenOverlay/update-client' -OutFile $online -TimeoutSec 30 | Out-Null
        $null = Read-VerifiedSignedManifest -ManifestPath $online -PackagePath $download `
            -PublicKeyPath (Join-Path $PSScriptRoot '..\src\VRPhoneScreenOverlay.Update\Resources\update-public-key.pem') -ExpectedVersion $Version
        $manifest = Get-Content -LiteralPath $online -Raw | ConvertFrom-Json
        if ($null -eq $manifest.download -or $manifest.download.version -ne $Version -or
            [DateTimeOffset]$manifest.download.expiresAt -le [DateTimeOffset]::UtcNow) {
            throw 'Public manifest has no usable download lease.'
        }
        & $ssh @sshOptions $target "python3 '$stage/Cloud-Publish.py' cleanup '$stage' '$Version'"
        if ($LASTEXITCODE -ne 0) { Write-Warning 'Release is active; staging metadata cleanup will need attention.' }
        Write-Host "User-uploaded 115 package verified and activated: $Version" -ForegroundColor Green
        [PSCustomObject]@{ RemoteStage = $stage; Activated = $true }
    } catch {
        if ($activated) {
            & $ssh @sshOptions $target "python3 '$stage/Cloud-Publish.py' rollback '$stage' '$Version'"
            if ($LASTEXITCODE -ne 0) { Write-Warning 'Cloud release rollback was not confirmed.' }
        }
        throw
    }
}
