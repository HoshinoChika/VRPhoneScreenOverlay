[CmdletBinding()]
param(
    [string]$SourceDirectory = '',
    [string]$OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'ReleaseFolder.Common.ps1')
if ([string]::IsNullOrWhiteSpace($SourceDirectory)) {
    $SourceDirectory = Join-Path $projectRoot 'artifacts\app'
}
$officialOutput = Join-Path $projectRoot 'release\VRPhoneScreenOverlay'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = $officialOutput }
$resolvedSource = [IO.Path]::GetFullPath($SourceDirectory).TrimEnd('\', '/')
$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\', '/')
$artifactsPrefix = (Join-Path $projectRoot 'artifacts') + '\'
$transactionRoot = Join-Path $projectRoot 'artifacts\release-folder-transactions'
if ($resolvedOutput -ne $officialOutput -and
    -not $resolvedOutput.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Custom release output must be an isolated directory under artifacts/.'
}
foreach ($target in @($resolvedOutput, ($resolvedOutput + '-previous'), $transactionRoot)) {
    Assert-ReleasePath -Path $target -RepositoryRoot $projectRoot
    if ($resolvedSource -eq $target -or
        $resolvedSource.StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase) -or
        $target.StartsWith($resolvedSource + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Release source, output, previous version, and transaction paths must not overlap.'
    }
}
if ($resolvedOutput -eq $transactionRoot -or
    $resolvedOutput.StartsWith($transactionRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $transactionRoot.StartsWith($resolvedOutput + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Release output must not overlap the transaction directory.'
}
$sourceHash = Get-ReleaseContentHash -Path $resolvedSource -RepositoryRoot $projectRoot
$sourceFiles = @(Get-ReleaseFiles -Path $resolvedSource -RepositoryRoot $projectRoot)
$portableHost = Join-Path $resolvedSource 'layout\VRPhoneScreenOverlay.exe'
foreach ($sourceFile in @('VRPhoneScreenOverlay.exe', 'layout\VRPhoneScreenOverlay.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $resolvedSource $sourceFile) -PathType Leaf)) {
        throw "Published application is missing $sourceFile. Run Build-Local.ps1 first."
    }
}

New-Item -ItemType Directory -Path $transactionRoot -Force | Out-Null
$sha256 = [Security.Cryptography.SHA256]::Create()
try {
    $lockName = [BitConverter]::ToString($sha256.ComputeHash(
            [Text.Encoding]::UTF8.GetBytes($resolvedOutput.ToLowerInvariant()))).Replace('-', '') + '.lock'
} finally {
    $sha256.Dispose()
}
$lockPath = Join-Path $transactionRoot $lockName
Assert-ReleasePath -Path $lockPath -RepositoryRoot $projectRoot
$publishLock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate,
    [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
$transaction = Join-Path $transactionRoot ([Guid]::NewGuid().ToString('N'))
$staging = Join-Path $transaction 'staging'
$preserveTransaction = $false
try {
    New-Item -ItemType Directory -Path (Join-Path $staging 'app') -Force | Out-Null
    $runtimeOutput = Join-Path $staging 'app'
    foreach ($file in $sourceFiles) {
        $relative = $file.FullName.Substring($resolvedSource.Length + 1)
        if ($relative -eq 'VRPhoneScreenOverlay.exe' -or $file.Extension -in '.pdb', '.xml') { continue }
        $destination = Join-Path $runtimeOutput $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
    Copy-Item -LiteralPath $portableHost -Destination (Join-Path $staging 'VRPhoneScreenOverlay.exe')
    # Build-Local serializes builds. A separately invoked Prepare must also reject
    # a source that another build changed while the staged copy was being made.
    if ((Get-ReleaseContentHash -Path $resolvedSource -RepositoryRoot $projectRoot) -ne $sourceHash) {
        throw 'Published source changed during release preparation; retry after the build has completed.'
    }
    $manifestPath = Join-Path $runtimeOutput 'manifest.vrmanifest'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8 | ConvertFrom-Json
    $applications = @($manifest.applications | Where-Object { $_.app_key -eq 'local.spacedraglite.desktop.v1' })
    if ($applications.Count -ne 1) {
        throw 'The SteamVR application manifest must preserve the stable application identity.'
    }
    $applications[0].binary_path_windows = '../VRPhoneScreenOverlay.exe'
    # Windows PowerShell and PowerShell 7 indent JSON differently. Both must
    # produce the same bytes or an unchanged build would evict the previous one.
    # PowerShell 5 also escapes <, >, &, and apostrophes by default. Normalize only
    # those raw JSON string characters: newer hosts' EscapeHtml additionally uses
    # \u0022 for embedded double quotes where PowerShell 5 uses \".
    $jsonArguments = @{ Depth = 20; Compress = $true }
    if ((Get-Command ConvertTo-Json).Parameters.ContainsKey('EscapeHandling')) {
        $jsonArguments.EscapeHandling = 'Default'
    }
    $manifestJson = ($manifest | ConvertTo-Json @jsonArguments).
        Replace('<', '\u003c').Replace('>', '\u003e').Replace('&', '\u0026').Replace("'", '\u0027')
    [IO.File]::WriteAllText($manifestPath, $manifestJson, [Text.UTF8Encoding]::new($false))

    foreach ($relativePath in @(
            'VRPhoneScreenOverlay.exe',
            'app\VRPhoneScreenOverlay.dll',
            'app\VRPhoneScreenOverlay.runtimeconfig.json',
            'app\VRPhoneScreenOverlay.deps.json',
            'app\VRPhoneScreenOverlay.Maintenance.exe',
            'app\VRPhoneScreenOverlay.SteamVR.BindingTool.exe',
            'app\manifest.vrmanifest',
            'app\action_manifest.json',
            'app\openvr_api.dll',
            'app\resources\scrcpy\scrcpy-server-v4.1',
            'app\resources\android-platform-tools\adb.exe',
            'app\resources\android-platform-tools\AdbWinApi.dll',
            'app\resources\android-platform-tools\AdbWinUsbApi.dll')) {
        $requiredPath = Join-Path $staging $relativePath
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf) -or
            (Get-Item -LiteralPath $requiredPath).Length -eq 0) {
            throw "Release folder is missing or has an empty required file: $relativePath"
        }
    }
    $unexpectedRootFiles = @(Get-ChildItem -LiteralPath $staging -Force |
        Where-Object { $_.Name -cne 'VRPhoneScreenOverlay.exe' -and $_.Name -cne 'app' })
    if ($unexpectedRootFiles.Count -ne 0) { throw 'Release root must contain only the main executable and app/.' }

    $smoke = Start-Process -FilePath (Join-Path $staging 'VRPhoneScreenOverlay.exe') `
        -ArgumentList '--smoke-test' -WorkingDirectory $projectRoot -PassThru -WindowStyle Hidden
    try {
        if (-not $smoke.WaitForExit(30000)) {
            $smoke.Kill()
            $null = $smoke.WaitForExit(5000)
            throw 'Staged portable application smoke test timed out after 30 seconds.'
        }
        $smoke.Refresh()
        if ($smoke.ExitCode -ne 0) {
            throw "Staged portable application smoke test failed with exit code $($smoke.ExitCode)."
        }
    } finally {
        $smoke.Dispose()
    }
    $outputParent = Split-Path -Parent $resolvedOutput
    Assert-ReleasePath -Path $outputParent -RepositoryRoot $projectRoot
    New-Item -ItemType Directory -Path $outputParent -Force | Out-Null
    Publish-PreparedRelease -StagingDirectory $staging -OutputDirectory $resolvedOutput `
        -TransactionDirectory $transaction -RepositoryRoot $projectRoot
} catch {
    $preserveTransaction = [bool]$_.Exception.Data['PreserveReleaseTransaction']
    throw
} finally {
    try {
        # Never delete a rollback backup if publication could not restore it.
        if (-not $preserveTransaction -and
            -not (Test-Path -LiteralPath (Join-Path $transaction 'retired-previous'))) {
            Remove-ReleaseTree -Path $transaction -RepositoryRoot $projectRoot
        }
    } finally {
        $publishLock.Dispose()
    }
}
