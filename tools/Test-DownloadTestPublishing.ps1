[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$AppDirectory)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'ReleaseAutomation.Common.ps1')
function Assert-Rejected([scriptblock]$Action) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw 'Invalid download test input was accepted.' }
}
$root = Join-Path $projectRoot "artifacts\download-test-validation-$([Guid]::NewGuid().ToString('N'))"
$source = Join-Path $root 'source'
$installed = Join-Path $root 'VRPhoneScreenOverlay'
New-Item -ItemType Directory -Path (Join-Path $source 'layout'), (Join-Path $source 'resources\scrcpy'), (Join-Path $installed 'app\layout'), (Join-Path $installed 'app\resources\scrcpy') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $AppDirectory 'VRPhoneScreenOverlay.exe') -Destination $source
Copy-Item -LiteralPath (Join-Path $AppDirectory 'layout\VRPhoneScreenOverlay.exe') -Destination (Join-Path $source 'layout')
Copy-Item -LiteralPath (Join-Path $source 'layout\VRPhoneScreenOverlay.exe') -Destination $installed
Copy-Item -LiteralPath (Join-Path $source 'layout\VRPhoneScreenOverlay.exe') -Destination (Join-Path $installed 'app\layout')
foreach ($relative in @('VRPhoneScreenOverlay.Maintenance.exe', 'resources\scrcpy\scrcpy-server-v4.1')) {
    [IO.File]::WriteAllText((Join-Path $source $relative), 'test fixture')
    Copy-Item -LiteralPath (Join-Path $source $relative) -Destination (Join-Path $installed "app\$relative")
}
Assert-ExistingDownloadTestBuild -AppDirectory $source -InstallDirectory $installed
[IO.File]::WriteAllText((Join-Path $installed 'app\VRPhoneScreenOverlay.Maintenance.exe'), 'different build')
Assert-Rejected { Assert-ExistingDownloadTestBuild -AppDirectory $source -InstallDirectory $installed }
$key = [Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve+NamedCurves]::nistP256)
try {
    $privateKey = Join-Path $root 'test-private.pem'
    $publicKey = Join-Path $root 'test-public.pem'
    [IO.File]::WriteAllText($privateKey, $key.ExportECPrivateKeyPem())
    [IO.File]::WriteAllText($publicKey, $key.ExportSubjectPublicKeyInfoPem())
} finally { $key.Dispose() }
$version = (Get-Item -LiteralPath (Join-Path $source 'VRPhoneScreenOverlay.exe')).VersionInfo.ProductVersion.Split('+', 2)[0]
$alias = "$version.download-test.1"
if ((Compare-ReleaseVersion -Left $alias -Right $version) -le 0 -or
    (Compare-ReleaseVersion -Left $version.Split('-', 2)[0] -Right $alias) -le 0) {
    throw 'The test alias must sort above its prerelease build and below the stable version.'
}
foreach ($relative in @('VRPhoneScreenOverlay.dll', 'VRPhoneScreenOverlay.SteamVR.BindingTool.exe', 'manifest.vrmanifest', 'action_manifest.json', 'resources\android-platform-tools\adb.exe')) {
    $file = Join-Path (Join-Path $installed 'app') $relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($file)) | Out-Null
    [IO.File]::WriteAllText($file, 'synthetic')
}
$publishArguments = @{ InstallDirectory = $installed; PrivateKeyPath = $privateKey; OutputDirectory = (Join-Path $root 'package') }
Assert-Rejected { & (Join-Path $PSScriptRoot 'Publish-TestUpdate.ps1') @publishArguments -Version $alias }
foreach ($invalid in @('9.9.9', "$version.download-test.0", "$version.download-test.01", "$version.download-test.1/../x")) {
    Assert-Rejected { & (Join-Path $PSScriptRoot 'Publish-TestUpdate.ps1') @publishArguments -Version $invalid -DownloadTest }
}
$package = & (Join-Path $PSScriptRoot 'Publish-TestUpdate.ps1') @publishArguments -Version $alias -DownloadTest
if (@(Get-ChildItem -LiteralPath $publishArguments.OutputDirectory -Filter '*.zip' -Recurse).Count -ne 1) { throw 'Publication created more than one package.' }
if ((Split-Path -Leaf $package.Package) -ne 'package.zip' -or (Split-Path -Leaf $package.UploadDirectory) -ne $alias) { throw 'The upload directory cannot be copied directly into 115 releases.' }
$upload = Get-Content -LiteralPath $package.UploadMetadata -Raw | ConvertFrom-Json
if ($upload.sha256 -ne $package.Sha256 -or $upload.sha1 -ne (Get-FileHash -LiteralPath $package.Package -Algorithm SHA1).Hash.ToLowerInvariant()) { throw 'Upload metadata differs from the signed package.' }
Assert-PortablePackageLayout -PackagePath $package.Package
$null = Read-VerifiedSignedManifest -ManifestPath $package.Manifest -PackagePath $package.Package -PublicKeyPath $publicKey -ExpectedVersion $alias
foreach ($name in @('Publish-BetaRelease.ps1', 'Publish-TestUpdate.ps1', 'CloudPublishing.Common.ps1', 'ReleaseAutomation.Common.ps1', 'Test-DownloadTestPublishing.ps1')) {
    $tokens = $null; $errors = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $name), [ref]$tokens, [ref]$errors)
    if ($errors.Count) { throw "$name contains parse errors: $($errors.Message -join '; ')" }
}
Write-Host 'Download test publishing validation passed.' -ForegroundColor Green
