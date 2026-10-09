[CmdletBinding()]
param(
    [string]$ConfigurationPath = '',
    [string]$UpdateSigningKeyPath = '',
    [string]$SshKeyPath = '',
    [string]$ServerRecordPath = '',
    [string]$ReleaseNotes = '测试通道更新',
    [switch]$RemoteProbe,
    [switch]$Upload,
    [switch]$WithdrawDownloadTest,
    [switch]$FocusedValidation,
    [switch]$DownloadTest,
    [switch]$ReuseExistingBuild,
    [string]$ExistingAppDirectory = '',
    [string]$ExistingInstallDirectory = '',
    [string]$PreparedReleaseDirectory = '',
    [string]$ResumeRemoteStage = '',
    [ValidateRange(1, 2147483647)][int]$DownloadTestNumber = 1
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ($PSVersionTable.PSVersion.Major -lt 7) {
    $pwsh = Get-Command pwsh -ErrorAction Stop
    $forwarded = @('-NoProfile', '-File', $PSCommandPath)
    foreach ($entry in $PSBoundParameters.GetEnumerator()) {
        if ($entry.Value -is [System.Management.Automation.SwitchParameter]) {
            if ($entry.Value.IsPresent) { $forwarded += "-$($entry.Key)" }
        } else { $forwarded += @("-$($entry.Key)", [string]$entry.Value) }
    }
    & $pwsh.Source @forwarded
    exit $LASTEXITCODE
}
if (([int]$RemoteProbe.IsPresent + [int]$Upload.IsPresent + [int]$WithdrawDownloadTest.IsPresent) -gt 1) { throw 'Choose one remote operation.' }
$remote = $Upload -or $RemoteProbe -or $WithdrawDownloadTest
if ($remote -ne (-not [string]::IsNullOrWhiteSpace($PreparedReleaseDirectory))) {
    throw 'Prepare the package locally first. After the user uploads it to 115, use -Upload or -RemoteProbe with -PreparedReleaseDirectory.'
}
if ($ResumeRemoteStage -and -not $remote) { throw 'A remote transaction requires a prepared release.' }
if (($DownloadTest -or $ReuseExistingBuild) -and ($remote -or -not $ExistingAppDirectory -or -not $ExistingInstallDirectory)) {
    throw 'Reusing a build requires both existing paths and local preparation.'
}
if (-not ($DownloadTest -or $ReuseExistingBuild) -and ($ExistingAppDirectory -or $ExistingInstallDirectory)) {
    throw 'Existing build paths require -DownloadTest or -ReuseExistingBuild.'
}
if ([string]::IsNullOrWhiteSpace($ConfigurationPath)) { $ConfigurationPath = Join-Path $projectRoot 'service.private.json' }
. (Join-Path $PSScriptRoot 'ReleaseAutomation.Common.ps1')
. (Join-Path $PSScriptRoot 'CloudPublishing.Common.ps1')
. (Join-Path $PSScriptRoot 'ReleaseFolder.Common.ps1')
$configuration = Get-ReleaseConfiguration -ConfigurationPath ([IO.Path]::GetFullPath($ConfigurationPath)) `
    -UpdateSigningKeyPath $UpdateSigningKeyPath -SshKeyPath $SshKeyPath -ServerRecordPath $ServerRecordPath `
    -RequireRemote:$remote -RequireSigningKey:(-not $remote)
$mode = if ($Upload) { 'upload' } elseif ($RemoteProbe) { 'remote-probe' } else { 'local' }
Push-Location $projectRoot
try {
    if ((& git branch --show-current | Out-String).Trim() -ne 'main') { throw 'Beta publishing must run from main.' }
    Assert-TrackedWorkingTreeClean -ProjectRoot $projectRoot
    $commit = Get-ReleaseCommit -ProjectRoot $projectRoot
    if ($remote) {
        $outputDirectory = [IO.Path]::GetFullPath($PreparedReleaseDirectory).TrimEnd('\', '/')
        $releaseRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts\releases')) + '\'
        if (-not $outputDirectory.StartsWith($releaseRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Prepared release must be inside repository artifacts/releases/.'
        }
        Assert-ReleasePath -Path $outputDirectory -RepositoryRoot $projectRoot
        $prepared = Get-Content -LiteralPath (Join-Path $outputDirectory 'release-result.json') -Raw | ConvertFrom-Json
        $version = [string]$prepared.version
        Assert-ReleaseVersion -Version $version
        $productVersion = [string]$prepared.productVersion
        if ($productVersion -notmatch '^(.+)\+([0-9a-f]{40})$') { throw 'Prepared release does not identify its source commit.' }
        $sourceVersion = $Matches[1]
        $commit = $Matches[2]
        Assert-ReleaseSourceUnchanged -ProjectRoot $projectRoot -SourceCommit $commit
        $DownloadTest = [bool]$prepared.downloadTest
        if ($DownloadTest) { Assert-DownloadTestVersion -SourceVersion $sourceVersion -Version $version }
        elseif ($version -ne $sourceVersion) { throw 'Prepared package and product versions differ.' }
        $manifestPath = Join-Path $outputDirectory 'latest.json'
        $packagePath = Join-Path $outputDirectory "$version\package.zip"
        $uploadMetadataPath = Join-Path $outputDirectory 'upload-metadata.json'
        if ($WithdrawDownloadTest) {
            if (-not $DownloadTest -or -not $prepared.onlineVerified -or -not $prepared.remoteStage) {
                throw 'Only a previously activated download test may be withdrawn.'
            }
            $null = Read-VerifiedSignedManifest -ManifestPath $manifestPath -PackagePath $packagePath `
                -PublicKeyPath (Join-Path $projectRoot 'src\VRPhoneScreenOverlay.Update\Resources\update-public-key.pem') -ExpectedVersion $version
            $withdrawal = Undo-CloudDownloadTest -Configuration $configuration -Version $version `
                -Stage $prepared.remoteStage -OutputDirectory $outputDirectory
            $withdrawal | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputDirectory 'withdrawal-result.json') -Encoding utf8
            $withdrawal
            return
        }
    } else {
        if ($DownloadTest -or $ReuseExistingBuild) {
            $appDirectory = [IO.Path]::GetFullPath($ExistingAppDirectory)
            $installDirectory = [IO.Path]::GetFullPath($ExistingInstallDirectory)
            Assert-ExistingDownloadTestBuild -AppDirectory $appDirectory -InstallDirectory $installDirectory
            $productVersion = (Get-Item -LiteralPath (Join-Path $appDirectory 'VRPhoneScreenOverlay.exe')).VersionInfo.ProductVersion
            if ($productVersion -notmatch '^(.+)\+([0-9a-f]{40})$') { throw 'Existing build does not identify its source commit.' }
            $sourceVersion = $Matches[1]
            $commit = $Matches[2]
            Assert-ReleaseSourceUnchanged -ProjectRoot $projectRoot -SourceCommit $commit
            if ($DownloadTest) {
                $version = Get-DownloadTestVersion -SourceVersion $sourceVersion -Number $DownloadTestNumber
                $ReleaseNotes = "下载更新测试：安装内容仍为 $sourceVersion。`n$ReleaseNotes"
            } else {
                $version = Get-ReleaseVersion -ProjectRoot $projectRoot
                if ($version -ne $sourceVersion) { throw 'Current product version differs from the existing build.' }
            }
        } else {
            $version = Get-ReleaseVersion -ProjectRoot $projectRoot
            & (Join-Path $PSScriptRoot 'Build-Local.ps1') -FocusedValidation:$FocusedValidation `
                -PackageOnly -ConfigurationPath $ConfigurationPath
            if ($LASTEXITCODE -ne 0) { throw 'Package-only build failed; the installed application was preserved.' }
            $appDirectory = Join-Path $projectRoot 'artifacts\app'
            $installDirectory = Join-Path $projectRoot 'artifacts\package-install\VRPhoneScreenOverlay'
            $productVersion = (Get-Item -LiteralPath (Join-Path $appDirectory 'VRPhoneScreenOverlay.exe')).VersionInfo.ProductVersion
            if ($productVersion -ne "$version+$commit") { throw 'Built product version does not match the verified source.' }
        }
        Assert-ReleaseVersion -Version $version
        $stamp = [DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss')
        $outputDirectory = Join-Path $projectRoot "artifacts\releases\$version-$($commit.Substring(0, 8))-$stamp"
        if (Test-Path -LiteralPath $outputDirectory) { throw 'Release artifact directory already exists.' }
        $packageResult = & (Join-Path $PSScriptRoot 'Publish-TestUpdate.ps1') -InstallDirectory $installDirectory `
            -Version $version -PrivateKeyPath $configuration.UpdateSigningKeyPath -OutputDirectory $outputDirectory `
            -ReleaseNotes $ReleaseNotes -DownloadTest:$DownloadTest
        if ($LASTEXITCODE -ne 0 -or $null -eq $packageResult) { throw 'Signed package preparation failed.' }
        $manifestPath = [string]$packageResult.Manifest
        $packagePath = [string]$packageResult.Package
        $uploadMetadataPath = [string]$packageResult.UploadMetadata
    }
    Assert-PortablePackageLayout -PackagePath $packagePath
    $verified = Read-VerifiedSignedManifest -ManifestPath $manifestPath -PackagePath $packagePath `
        -PublicKeyPath (Join-Path $projectRoot 'src\VRPhoneScreenOverlay.Update\Resources\update-public-key.pem') -ExpectedVersion $version
    $metadata = Get-Content -LiteralPath $uploadMetadataPath -Raw | ConvertFrom-Json
    if ($metadata.schemaVersion -ne 1 -or $metadata.version -ne $version -or
        $metadata.path -ne "releases/$version/package.zip" -or $metadata.size -ne $verified.PackageBytes -or
        $metadata.sha256 -ne $verified.PackageSha256 -or
        $metadata.sha1 -ne (Get-FileHash -LiteralPath $packagePath -Algorithm SHA1).Hash.ToLowerInvariant()) {
        throw 'Upload metadata differs from the signed local package.'
    }
    $remoteVerified = $false
    $onlineVerified = $false
    $remoteStage = $null
    if ($remote) {
        $cloudOutput = @(Publish-CloudRelease -Configuration $configuration -Version $version -PackagePath $packagePath `
            -ManifestPath $manifestPath -UploadMetadataPath $uploadMetadataPath -OutputDirectory $outputDirectory `
            -PackageHash $verified.PackageSha256 -ProbeOnly:$RemoteProbe -ResumeRemoteStage $ResumeRemoteStage)
        $transaction = $cloudOutput | Where-Object { $_ -is [PSCustomObject] -and $_.PSObject.Properties.Name -contains 'RemoteStage' } | Select-Object -Last 1
        $remoteStage = $transaction.RemoteStage
        $remoteVerified = $true
        $onlineVerified = [bool]$transaction.Activated
    }
    $uploadDirectory = Join-Path $outputDirectory $version
    $report = [ordered]@{
        schemaVersion = 1
        completedAt = [DateTimeOffset]::UtcNow
        mode = $mode
        version = $version
        commit = $commit
        productVersion = $productVersion
        package = "$version/package.zip"
        packageBytes = $verified.PackageBytes
        packageSha256 = $verified.PackageSha256
        packageFormat = 'full-install-v1'
        signatureVerified = $true
        remoteVerified = $remoteVerified
        onlineVerified = $onlineVerified
        downloadTest = [bool]$DownloadTest
        remoteStage = $remoteStage
        cloudUploadBy = 'user'
        cloudDestination = "115/VRPSO/releases/$version/package.zip"
        installedApplicationChanged = $false
        manualUpdateRetestPending = $true
    }
    $reportPath = Join-Path $outputDirectory 'release-result.json'
    [IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json) + "`n", [Text.UTF8Encoding]::new($false))
    Write-Host "Release automation completed: $mode" -ForegroundColor Green
    if (-not $remote) { Write-Host "Upload the version folder $uploadDirectory into 115/VRPSO/releases/, then publish its prepared metadata." }
    [PSCustomObject]@{
        Mode = $mode; Version = $version; Commit = $commit; Package = $packagePath
        Manifest = $manifestPath; Report = $reportPath; UploadDirectory = $uploadDirectory
        CloudDestination = $report.cloudDestination; Sha256 = $verified.PackageSha256
        RemoteVerified = $remoteVerified; OnlineVerified = $onlineVerified
    }
} finally { Pop-Location }
