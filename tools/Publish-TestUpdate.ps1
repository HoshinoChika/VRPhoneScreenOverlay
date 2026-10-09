[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InstallDirectory,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$PrivateKeyPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string]$ReleaseNotes = '测试通道更新',

    [switch]$DownloadTest
)

$ErrorActionPreference = 'Stop'
$resolvedInstall = [IO.Path]::GetFullPath($InstallDirectory)
$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
$resolvedKey = [IO.Path]::GetFullPath($PrivateKeyPath)
if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw 'Signed update package creation requires PowerShell 7 or later.'
}

if (-not (Test-Path -LiteralPath $resolvedKey -PathType Leaf)) {
    throw "Update signing key not found: $resolvedKey"
}

if ([IO.Path]::GetFileName($resolvedInstall.TrimEnd('\', '/')) -cne 'VRPhoneScreenOverlay') {
    throw 'The full package source folder must be named VRPhoneScreenOverlay.'
}
$rootItems = @(Get-ChildItem -LiteralPath $resolvedInstall -Force)
if ($rootItems.Count -ne 2 -or -not (Test-Path -LiteralPath (Join-Path $resolvedInstall 'app') -PathType Container)) {
    throw 'The full package source root must contain only the main executable and app/.'
}
foreach ($required in @('VRPhoneScreenOverlay.exe', 'app\VRPhoneScreenOverlay.dll',
    'app\VRPhoneScreenOverlay.Maintenance.exe', 'app\VRPhoneScreenOverlay.SteamVR.BindingTool.exe',
    'app\manifest.vrmanifest', 'app\action_manifest.json',
    'app\resources\android-platform-tools\adb.exe', 'app\resources\scrcpy\scrcpy-server-v4.1')) {
    if (-not (Test-Path -LiteralPath (Join-Path $resolvedInstall $required) -PathType Leaf)) {
        throw "Full package source is missing $required."
    }
}
. (Join-Path $PSScriptRoot 'ReleaseAutomation.Common.ps1')

$publishedVersion = (Get-Item -LiteralPath (Join-Path $resolvedInstall 'VRPhoneScreenOverlay.exe')).VersionInfo.ProductVersion.Split('+', 2)[0]
if ($DownloadTest) {
    Assert-DownloadTestVersion -SourceVersion $publishedVersion -Version $Version
} elseif (-not [string]::Equals($publishedVersion, $Version, [StringComparison]::Ordinal)) {
    throw "Published version ($publishedVersion) does not match package version ($Version)."
}

[IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null
$safeVersion = $Version -replace '[^0-9A-Za-z.-]', '-'
$uploadDirectory = Join-Path $resolvedOutput $safeVersion
[IO.Directory]::CreateDirectory($uploadDirectory) | Out-Null
$packageName = 'package.zip'
$packagePath = Join-Path $uploadDirectory $packageName
if (Test-Path -LiteralPath $packagePath) {
    throw "Refusing to overwrite existing update package: $packagePath"
}

Compress-Archive -Path $resolvedInstall -DestinationPath $packagePath `
    -CompressionLevel Optimal
Assert-PortablePackageLayout -PackagePath $packagePath
$package = Get-Item -LiteralPath $packagePath
$packageHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $packagePath).Hash.ToLowerInvariant()
$payloadObject = [ordered]@{
    schemaVersion = 1
    packageFormat = 'full-install-v1'
    channel = 'beta'
    version = $Version
    publishedAt = [DateTimeOffset]::UtcNow
    packageUrl = "/vrphonescreen/api/v1/updates/download/$Version"
    packageSize = $package.Length
    packageSha256 = $packageHash
    releaseNotes = $ReleaseNotes
}
$payloadJson = $payloadObject | ConvertTo-Json -Compress
$payloadBytes = [Text.Encoding]::UTF8.GetBytes($payloadJson)
$ecdsa = [Security.Cryptography.ECDsa]::Create()
try {
    $ecdsa.ImportFromPem([IO.File]::ReadAllText($resolvedKey))
    $signature = $ecdsa.SignData($payloadBytes, [Security.Cryptography.HashAlgorithmName]::SHA256)
} finally {
    $ecdsa.Dispose()
}

$manifest = [ordered]@{
    payload = [Convert]::ToBase64String($payloadBytes)
    signature = [Convert]::ToBase64String($signature)
} | ConvertTo-Json
$manifestPath = Join-Path $resolvedOutput 'latest.json'
[IO.File]::WriteAllText($manifestPath, $manifest + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
$uploadMetadataPath = Join-Path $resolvedOutput 'upload-metadata.json'
$uploadMetadata = [ordered]@{
    schemaVersion = 1
    version = $Version
    path = "releases/$Version/package.zip"
    size = $package.Length
    sha256 = $packageHash
    sha1 = (Get-FileHash -Algorithm SHA1 -LiteralPath $packagePath).Hash.ToLowerInvariant()
}
[IO.File]::WriteAllText($uploadMetadataPath, ($uploadMetadata | ConvertTo-Json) + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
[PSCustomObject]@{
    Manifest = $manifestPath
    Package = $packagePath
    Bytes = $package.Length
    Sha256 = $packageHash
    UploadMetadata = $uploadMetadataPath
    UploadDirectory = $uploadDirectory
}
