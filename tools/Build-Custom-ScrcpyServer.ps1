[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,

    [Parameter(Mandatory = $true)]
    [string]$JavaHome,

    [Parameter(Mandatory = $true)]
    [string]$AndroidSdkRoot,

    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$expectedCommit = '2926c06c5dc3064ae6d8db706f1a98a37cfcf3f0'
$resolvedSource = [IO.Path]::GetFullPath($SourcePath)
$resolvedJavaHome = [IO.Path]::GetFullPath($JavaHome)
$resolvedAndroidSdk = [IO.Path]::GetFullPath($AndroidSdkRoot)
$resolvedOutput = if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    Join-Path $projectRoot 'artifacts\custom-scrcpy\scrcpy-server-v4.1'
} else {
    [IO.Path]::GetFullPath($OutputPath)
}

if (-not (Test-Path -LiteralPath (Join-Path $resolvedSource '.git'))) {
    throw 'SourcePath must be a clean scrcpy Git checkout.'
}

$actualCommit = (& git -C $resolvedSource rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $actualCommit -ne $expectedCommit) {
    throw "scrcpy source must be pinned to $expectedCommit."
}

$sourceStatus = & git -C $resolvedSource status --porcelain
if ($LASTEXITCODE -ne 0 -or $sourceStatus) {
    throw 'scrcpy source checkout must be clean before applying the customization.'
}

$patchPath = Join-Path $projectRoot 'third_party\scrcpy\vrphonescreen-user-activity.patch'
& git -C $resolvedSource apply --check --unidiff-zero $patchPath
if ($LASTEXITCODE -ne 0) {
    throw 'The VRPhoneScreen Overlay scrcpy patch does not apply cleanly.'
}

& git -C $resolvedSource apply --unidiff-zero $patchPath
if ($LASTEXITCODE -ne 0) {
    throw 'Applying the VRPhoneScreen Overlay scrcpy patch failed.'
}

$previousJavaHome = $env:JAVA_HOME
$previousAndroidHome = $env:ANDROID_HOME
$previousAndroidSdkRoot = $env:ANDROID_SDK_ROOT
try {
    $env:JAVA_HOME = $resolvedJavaHome
    $env:ANDROID_HOME = $resolvedAndroidSdk
    $env:ANDROID_SDK_ROOT = $resolvedAndroidSdk
    & (Join-Path $resolvedSource 'gradlew.bat') `
        --project-dir $resolvedSource `
        server:test `
        server:assembleRelease `
        --no-daemon
    if ($LASTEXITCODE -ne 0) {
        throw 'Customized scrcpy server build failed.'
    }
} finally {
    $env:JAVA_HOME = $previousJavaHome
    $env:ANDROID_HOME = $previousAndroidHome
    $env:ANDROID_SDK_ROOT = $previousAndroidSdkRoot
}

$builtServer = Join-Path $resolvedSource `
    'server\build\outputs\apk\release\server-release-unsigned.apk'
if (-not (Test-Path -LiteralPath $builtServer -PathType Leaf)) {
    throw 'Gradle completed without producing the customized scrcpy server.'
}

$outputDirectory = Split-Path -Parent $resolvedOutput
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
Copy-Item -LiteralPath $builtServer -Destination $resolvedOutput -Force
$outputFile = Get-Item -LiteralPath $resolvedOutput
[PSCustomObject]@{
    Path = $outputFile.FullName
    Bytes = $outputFile.Length
    Sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $outputFile.FullName).Hash.ToLowerInvariant()
}
