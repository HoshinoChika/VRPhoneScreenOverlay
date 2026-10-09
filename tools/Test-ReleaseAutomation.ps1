[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'ReleaseAutomation.Common.ps1')

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Expected,

        [Parameter(Mandatory = $true)]
        [object]$Actual,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if ($Expected -ne $Actual) {
        throw "$Message Expected: $Expected; actual: $Actual."
    }
}

function Assert-Throws {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Action,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    try {
        & $Action
    }
    catch {
        return
    }

    throw $Message
}

$version = Get-ReleaseVersion -ProjectRoot $projectRoot
Assert-ReleaseSourceUnchanged -ProjectRoot $projectRoot -SourceCommit (Get-ReleaseCommit -ProjectRoot $projectRoot)
Assert-Throws { Assert-ReleaseSourceUnchanged -ProjectRoot $projectRoot -SourceCommit 'invalid' } 'Invalid release source commit was accepted.'
$baselineCommit = (& git rev-parse 'baseline-v0.2.5-clean^{}' | Out-String).Trim()
if ($baselineCommit -notmatch '^[0-9a-f]{40}$') { throw 'Test baseline commit is unavailable.' }
Assert-Throws { Assert-ReleaseSourceUnchanged -ProjectRoot $projectRoot -SourceCommit $baselineCommit } 'A release from changed production source was accepted.'
Assert-ReleaseVersion -Version $version
Assert-Equal '0.2.8-download-test.1' (Get-DownloadTestVersion -SourceVersion '0.2.7') 'Stable download test identifier must be newer.'
Assert-Equal 1 (Compare-ReleaseVersion -Left (Get-DownloadTestVersion -SourceVersion '0.2.7') -Right '0.2.7') 'Stable build would not see the test signal.'
Assert-Equal '0.2.6-beta.13.download-test.2' (Get-DownloadTestVersion -SourceVersion '0.2.6-beta.13' -Number 2) 'Existing beta download test identifiers changed.'
Assert-DownloadTestVersion -SourceVersion '0.2.7' -Version '0.2.8-download-test.1'
Assert-DownloadTestVersion -SourceVersion '0.2.6-beta.13' -Version '0.2.6-beta.13.download-test.2'
Assert-Throws { Assert-DownloadTestVersion -SourceVersion '0.2.7' -Version '0.2.9-download-test.1' } 'Unrelated test version accepted.'
Assert-Throws { Assert-DownloadTestVersion -SourceVersion '0.2.7' -Version '0.2.8-download-test.0' } 'Zero test sequence accepted.'
Assert-Throws { Assert-DownloadTestVersion -SourceVersion '0.2.7' -Version '0.2.7.download-test.1' } 'Invalid stable test identifier accepted.'
Assert-Equal 1 (Compare-ReleaseVersion -Left '0.2.6-beta.5' -Right '0.2.6-beta.4') `
    'A newer numeric prerelease must compare higher.'
Assert-Equal -1 (Compare-ReleaseVersion -Left '0.2.6-beta.4' -Right '0.2.6') `
    'A prerelease must compare lower than the stable release.'
Assert-Equal 0 (Compare-ReleaseVersion -Left $version -Right $version) `
    'The same version must compare equal.'
Assert-Throws { Assert-ReleaseVersion -Version '0.2' } `
    'An incomplete release version was accepted.'
Assert-Throws { Assert-SafeRemoteRoot -RemoteRoot '/var/lib/../unsafe' } `
    'A traversing remote root was accepted.'

$temporaryDirectory = Join-Path `
    ([IO.Path]::GetTempPath()) `
    "VRPhoneScreenOverlay-release-test-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
try {
    $recordPath = Join-Path $temporaryDirectory 'server.txt'
    [IO.File]::WriteAllText(
        $recordPath,
        "test address: 203.0.113.7`n",
        [Text.UTF8Encoding]::new($false))
    Assert-Equal '203.0.113.7' (Get-ServerAddressFromRecord -ServerRecordPath $recordPath) `
        'The server address parser returned the wrong address.'

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    Add-Type -AssemblyName System.IO.Compression
    $portableTestPath = Join-Path $temporaryDirectory 'portable.zip'
    $archive = [IO.Compression.ZipFile]::Open($portableTestPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in @(
                'VRPhoneScreenOverlay.exe',
                'app/VRPhoneScreenOverlay.dll',
                'app/VRPhoneScreenOverlay.Maintenance.exe',
                'app/VRPhoneScreenOverlay.SteamVR.BindingTool.exe',
                'app/manifest.vrmanifest',
                'app/action_manifest.json',
                'app/resources/android-platform-tools/adb.exe',
                'app/resources/scrcpy/scrcpy-server-v4.1')) {
            $null = $archive.CreateEntry('VRPhoneScreenOverlay/' + $entry)
        }
    }
    finally {
        $archive.Dispose()
    }
    Assert-PortablePackageLayout -PackagePath $portableTestPath
    $archive = [IO.Compression.ZipFile]::Open($portableTestPath, [IO.Compression.ZipArchiveMode]::Update)
    try {
        $null = $archive.CreateEntry('VRPhoneScreenOverlay/unexpected.dll')
    }
    finally {
        $archive.Dispose()
    }
    Assert-Throws { Assert-PortablePackageLayout -PackagePath $portableTestPath } `
        'A loose dependency in the portable root was accepted.'
    $archive = [IO.Compression.ZipFile]::Open($portableTestPath, [IO.Compression.ZipArchiveMode]::Update)
    try {
        $archive.GetEntry('VRPhoneScreenOverlay/unexpected.dll').Delete()
        $null = $archive.CreateEntry('VRPhoneScreenOverlay.exe')
    }
    finally {
        $archive.Dispose()
    }
    Assert-Throws { Assert-PortablePackageLayout -PackagePath $portableTestPath } `
        'An executable outside the outer installation folder was accepted.'
}
finally {
    $portableTestPath = Join-Path $temporaryDirectory 'portable.zip'
    if (Test-Path -LiteralPath $portableTestPath) {
        Remove-Item -LiteralPath $portableTestPath -Force
    }
    $recordPath = Join-Path $temporaryDirectory 'server.txt'
    if (Test-Path -LiteralPath $recordPath) {
        Remove-Item -LiteralPath $recordPath -Force
    }
    Remove-Item -LiteralPath $temporaryDirectory -Force
}

foreach ($scriptName in @(
        'ReleaseAutomation.Common.ps1',
        'Set-ReleaseVersion.ps1',
        'Publish-TestUpdate.ps1',
        'Prepare-ReleaseFolder.ps1',
        'Build-Local.ps1',
        'Publish-BetaRelease.ps1')) {
    $tokens = $null
    $parseErrors = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile(
        (Join-Path $PSScriptRoot $scriptName),
        [ref]$tokens,
        [ref]$parseErrors)
    if ($parseErrors.Count -ne 0) {
        throw "$scriptName contains PowerShell parse errors: $($parseErrors.Message -join '; ')"
    }
}

$publishScript = Get-Content -LiteralPath `
    (Join-Path $PSScriptRoot 'Publish-BetaRelease.ps1') `
    -Raw -Encoding utf8
if ($publishScript -match '\[string\]\$ConfigurationPath\s*=\s*\([^\r\n]*\$PSScriptRoot') {
    throw 'Publish-BetaRelease resolves PSScriptRoot during parameter binding, which breaks Windows PowerShell forwarding.'
}

$verifyScript = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Verify.ps1') `
    -Raw -Encoding utf8
if ($verifyScript -match 'PropertyGroup\.VersionPrefix') {
    throw 'Verify.ps1 uses aggregate XML property access that fails under release strict mode.'
}

$snapshotGenerator = Get-Content -LiteralPath `
    (Join-Path $projectRoot 'src\VRPhoneScreenOverlay.App\UiSnapshotGenerator.cs') `
    -Raw -Encoding utf8
if ($snapshotGenerator -match '版本 0\.\d') {
    throw 'UiSnapshotGenerator still contains a hard-coded product version.'
}

Write-Host 'Release automation validation passed.' -ForegroundColor Green
