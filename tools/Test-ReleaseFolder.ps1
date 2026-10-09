[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'ReleaseFolder.Common.ps1')
$prepareScript = Join-Path $PSScriptRoot 'Prepare-ReleaseFolder.ps1'
$testRoot = Join-Path $projectRoot ('artifacts\release-folder-test-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $testRoot 'published'
$current = Join-Path $testRoot 'current'
$previous = $current + '-previous'

function Assert-ReleaseTest {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-ReleaseTestThrows {
    param([scriptblock]$Action, [string]$ExpectedMessage)
    try { & $Action } catch {
        if ($_.Exception.Message -notlike "*$ExpectedMessage*") {
            throw "Unexpected rejection: $($_.Exception.Message); expected: $ExpectedMessage"
        }
        return
    }
    throw "Expected release preparation to reject: $ExpectedMessage"
}

function Assert-TestReleasesUnchanged {
    Assert-ReleaseTest ((Get-ReleaseContentHash $current $projectRoot) -eq $script:currentHash) `
        'The current release changed after a failed or identical preparation.'
    Assert-ReleaseTest ((Get-ReleaseContentHash $previous $projectRoot) -eq $script:previousHash) `
        'The previous release changed after a failed or identical preparation.'
}

Assert-ReleasePath -Path $testRoot -RepositoryRoot $projectRoot
New-Item -ItemType Directory -Path (Join-Path $source 'layout') -Force | Out-Null
try {
    # Tiny inert executables exercise the real Start-Process/exit-code path without
    # starting Android, SteamVR, or the product. Windows PowerShell supplies csc.
    $goodHost = Join-Path $source 'layout\VRPhoneScreenOverlay.exe'
    Add-Type -TypeDefinition @'
public static class ReleaseFolderSmokeFixture {
    public static int Main(string[] args) {
        return args.Length == 1 && args[0] == "--smoke-test" ? 0 : 91;
    }
}
'@ -OutputAssembly $goodHost -OutputType ConsoleApplication
    $badHost = Join-Path $testRoot 'bad-smoke.exe'
    Add-Type -TypeDefinition 'public static class ReleaseFolderBadSmokeFixture { public static int Main() { return 37; } }' `
        -OutputAssembly $badHost -OutputType ConsoleApplication
    $goodHostBackup = Join-Path $testRoot 'good-smoke.exe'
    Copy-Item -LiteralPath $goodHost -Destination $goodHostBackup
    foreach ($relative in @(
            'VRPhoneScreenOverlay.exe',
            'VRPhoneScreenOverlay.dll',
            'VRPhoneScreenOverlay.runtimeconfig.json',
            'VRPhoneScreenOverlay.deps.json',
            'VRPhoneScreenOverlay.Maintenance.exe',
            'VRPhoneScreenOverlay.SteamVR.BindingTool.exe',
            'action_manifest.json',
            'openvr_api.dll',
            'resources\scrcpy\scrcpy-server-v4.1',
            'resources\android-platform-tools\adb.exe',
            'resources\android-platform-tools\AdbWinApi.dll',
            'resources\android-platform-tools\AdbWinUsbApi.dll',
            'symbols.pdb',
            'documentation.xml')) {
        $path = Join-Path $source $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
        [IO.File]::WriteAllText($path, 'fixture-v1')
    }
    $manifestPath = Join-Path $source 'manifest.vrmanifest'
    # Include Unicode, HTML-sensitive characters, quotes, and deliberate property
    # order so the two PowerShell JSON implementations must produce identical bytes.
    $validManifest = '{"applications":[{"app_key":"local.spacedraglite.desktop.v1","binary_path_windows":"VRPhoneScreenOverlay.exe","strings":{"zh_cn":{"name":"\u4e2d\u6587","description":"\u003c\u003e\u0026\u0027\u0022 / \\"},"en_us":{"name":"VRPhoneScreen Overlay"}},"z_order":2,"a_order":1}],"source":"builtin"}'
    [IO.File]::WriteAllText($manifestPath, $validManifest)
    $payloadPath = Join-Path $source 'VRPhoneScreenOverlay.dll'

    & $prepareScript -SourceDirectory $source -OutputDirectory $current
    Assert-ReleaseTest (Test-Path -LiteralPath (Join-Path $current 'VRPhoneScreenOverlay.exe')) `
        'The first preparation did not produce the root executable.'
    Assert-ReleaseTest (-not (Test-Path -LiteralPath $previous)) 'The first preparation invented a previous version.'
    Assert-ReleaseTest (@(Get-ChildItem -LiteralPath $current -File).Count -eq 1) `
        'The root contains files other than the main executable.'
    Assert-ReleaseTest (-not (Test-Path -LiteralPath (Join-Path $current 'app\symbols.pdb'))) `
        'Debug symbols were copied into the final product.'
    Assert-ReleaseTest (-not (Test-Path -LiteralPath (Join-Path $current 'app\VRPhoneScreenOverlay.exe'))) `
        'The old app host was copied into the runtime directory.'
    $manifest = Get-Content -LiteralPath (Join-Path $current 'app\manifest.vrmanifest') -Raw | ConvertFrom-Json
    Assert-ReleaseTest ($manifest.applications[0].binary_path_windows -eq '../VRPhoneScreenOverlay.exe') `
        'The portable manifest does not launch the root executable.'

    [IO.File]::WriteAllText($payloadPath, 'fixture-v2')
    & $prepareScript -SourceDirectory $source -OutputDirectory $current
    Assert-ReleaseTest ([IO.File]::ReadAllText((Join-Path $previous 'app\VRPhoneScreenOverlay.dll')) -eq 'fixture-v1') `
        'The second preparation did not retain version one.'
    [IO.File]::WriteAllText($payloadPath, 'fixture-v3')
    & $prepareScript -SourceDirectory $source -OutputDirectory $current
    Assert-ReleaseTest ([IO.File]::ReadAllText((Join-Path $current 'app\VRPhoneScreenOverlay.dll')) -eq 'fixture-v3') `
        'The third preparation did not publish version three.'
    Assert-ReleaseTest ([IO.File]::ReadAllText((Join-Path $previous 'app\VRPhoneScreenOverlay.dll')) -eq 'fixture-v2') `
        'The third preparation did not retain version two.'
    $script:currentHash = Get-ReleaseContentHash $current $projectRoot
    $script:previousHash = Get-ReleaseContentHash $previous $projectRoot
    $creationTime = (Get-Item -LiteralPath $current).CreationTimeUtc
    & $prepareScript -SourceDirectory $source -OutputDirectory $current
    Assert-TestReleasesUnchanged
    Assert-ReleaseTest ((Get-Item -LiteralPath $current).CreationTimeUtc -eq $creationTime) `
        'An identical preparation replaced the current directory.'

    $powerShell7 = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($null -eq $powerShell7) {
        Write-Host 'SKIP PowerShell 5/7 release determinism: pwsh is not installed.' -ForegroundColor Yellow
    } else {
        $windowsPowerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
        foreach ($hostExecutable in @($windowsPowerShell, $powerShell7.Source, $windowsPowerShell)) {
            & $hostExecutable -NoProfile -ExecutionPolicy Bypass -File $prepareScript `
                -SourceDirectory $source -OutputDirectory $current
            Assert-ReleaseTest ($LASTEXITCODE -eq 0) "Release preparation failed in $hostExecutable."
            Assert-TestReleasesUnchanged
            Assert-ReleaseTest ((Get-Item -LiteralPath $current).CreationTimeUtc -eq $creationTime) `
                "Switching PowerShell hosts replaced the identical current directory: $hostExecutable"
        }
        Write-Host 'PowerShell 5 -> 7 -> 5 preserved both releases, including Unicode/escaping/property order.' -ForegroundColor Green
    }

    [IO.File]::WriteAllText($manifestPath, '{"applications":[]}')
    Assert-ReleaseTestThrows { & $prepareScript -SourceDirectory $source -OutputDirectory $current } `
        'stable application identity'
    Assert-TestReleasesUnchanged
    [IO.File]::WriteAllText($manifestPath, $validManifest)
    Remove-Item -LiteralPath $payloadPath
    Assert-ReleaseTestThrows { & $prepareScript -SourceDirectory $source -OutputDirectory $current } `
        'missing or has an empty required file'
    Assert-TestReleasesUnchanged
    [IO.File]::WriteAllText($payloadPath, 'fixture-v4')
    Copy-Item -LiteralPath $badHost -Destination $goodHost -Force
    Assert-ReleaseTestThrows { & $prepareScript -SourceDirectory $source -OutputDirectory $current } `
        'smoke test failed with exit code 37'
    Assert-TestReleasesUnchanged
    Copy-Item -LiteralPath $goodHostBackup -Destination $goodHost -Force

    foreach ($lockedDirectory in @($current, $previous)) {
        $lockedFile = [IO.File]::Open((Join-Path $lockedDirectory 'app\VRPhoneScreenOverlay.dll'),
            [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
        try {
            Assert-ReleaseTestThrows { & $prepareScript -SourceDirectory $source -OutputDirectory $current } `
                'being used by another process'
        } finally {
            $lockedFile.Dispose()
        }
        Assert-TestReleasesUnchanged
    }

    # Deny renaming staging, but allow file reads. This reaches the commit step
    # after both existing releases moved and exercises actual rollback renames.
    Add-Type -TypeDefinition @'
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
public static class ReleaseFolderDirectoryLock {
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern SafeFileHandle CreateFile(string name, uint access, uint sharing,
        System.IntPtr security, uint disposition, uint flags, System.IntPtr template);
}
'@
    $rollbackTransaction = Join-Path $testRoot 'rollback-transaction'
    $rollbackStaging = Join-Path $rollbackTransaction 'staging'
    New-Item -ItemType Directory -Path $rollbackStaging -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $rollbackStaging 'fixture.txt'), 'new-content')
    $directoryLock = [ReleaseFolderDirectoryLock]::CreateFile($rollbackStaging, [uint32]2147483648, 3,
        [IntPtr]::Zero, 3, 0x02000000, [IntPtr]::Zero)
    try {
        Assert-ReleaseTest (-not $directoryLock.IsInvalid) 'Could not lock the staged directory for rollback testing.'
        Assert-ReleaseTestThrows {
            Publish-PreparedRelease -StagingDirectory $rollbackStaging -OutputDirectory $current `
                -TransactionDirectory $rollbackTransaction -RepositoryRoot $projectRoot
        } 'being used by another process'
    } finally {
        $directoryLock.Dispose()
    }
    Assert-TestReleasesUnchanged
    Assert-ReleaseTest (-not (Test-Path -LiteralPath (Join-Path $rollbackTransaction 'retired-previous'))) `
        'Rollback did not restore the previous release to its original directory.'
    Assert-ReleaseTestThrows { & $prepareScript -SourceDirectory $source -OutputDirectory $source } `
        'must not overlap'
    Assert-ReleaseTestThrows { & $prepareScript -SourceDirectory $source -OutputDirectory $projectRoot } `
        'isolated directory under artifacts'
    Assert-TestReleasesUnchanged

    # A directory junction needs no symlink privilege on Windows. Verify refusal
    # before enumeration and unlink only this exact fixture with Directory.Delete.
    $junction = Join-Path $source 'linked-files'
    $null = New-Item -ItemType Junction -Path $junction -Target $previous
    try {
        Assert-ReleaseTestThrows { & $prepareScript -SourceDirectory $source -OutputDirectory $current } `
            'reparse points'
    } finally {
        Assert-ReleasePath -Path $source -RepositoryRoot $projectRoot
        [IO.Directory]::Delete($junction)
    }
    Assert-TestReleasesUnchanged
    & $prepareScript -SourceDirectory $source -OutputDirectory $current
    Assert-ReleaseTest ([IO.File]::ReadAllText((Join-Path $current 'app\VRPhoneScreenOverlay.dll')) -eq 'fixture-v4') `
        'Preparation did not recover after rejected attempts.'
    Assert-ReleaseTest ([IO.File]::ReadAllText((Join-Path $previous 'app\VRPhoneScreenOverlay.dll')) -eq 'fixture-v3') `
        'The final preparation retained the wrong previous version.'
    Write-Host 'Release folder transactions passed: rotation, identical content, invalid staging, smoke failure, locks, rollback, and path protection.' -ForegroundColor Green
} finally {
    Remove-ReleaseTree -Path $testRoot -RepositoryRoot $projectRoot
}
