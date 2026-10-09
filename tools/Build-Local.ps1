[CmdletBinding()]
param(
    [switch]$FocusedValidation,
    [switch]$PackageOnly,
    [string]$ConfigurationPath = ''
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'ReleaseFolder.Common.ps1')
. (Join-Path $PSScriptRoot 'ServiceConfiguration.Common.ps1')
# Verify/publish share project outputs and artifacts/app. Serialize the complete
# build before touching them, rather than locking only the final folder rotation.
$buildLockPath = Join-Path $projectRoot 'artifacts\build-local.lock'
Assert-ReleasePath -Path $buildLockPath -RepositoryRoot $projectRoot
New-Item -ItemType Directory -Path (Split-Path -Parent $buildLockPath) -Force | Out-Null
$buildLock = [IO.File]::Open($buildLockPath, [IO.FileMode]::OpenOrCreate,
    [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    $localDotnet = Join-Path $projectRoot '.dotnet\dotnet.exe'
    $dotnet = if (Test-Path -LiteralPath $localDotnet) {
        $localDotnet
    } elseif (Get-Command dotnet -ErrorAction SilentlyContinue) {
        (Get-Command dotnet -ErrorAction Stop).Source
    } else {
        & (Join-Path $PSScriptRoot 'Install-DevelopmentSdk.ps1')
        $localDotnet
    }

    $isCi = [string]::Equals($env:CI, 'true', [StringComparison]::OrdinalIgnoreCase)
    if (-not $FocusedValidation) {
        & (Join-Path $PSScriptRoot 'Verify.ps1') -LockedRestore:$isCi
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }

    $appProject = Join-Path $projectRoot 'src\VRPhoneScreenOverlay.App\VRPhoneScreenOverlay.App.csproj'
    $bindingToolProject = Join-Path $projectRoot `
        'src\VRPhoneScreenOverlay.SteamVR.BindingTool\VRPhoneScreenOverlay.SteamVR.BindingTool.csproj'
    $maintenanceProject = Join-Path $projectRoot `
        'src\VRPhoneScreenOverlay.Maintenance\VRPhoneScreenOverlay.Maintenance.csproj'
    # Preserve each project's declared RID set, including shared Linux protocols.
    # The publish commands below still choose the Windows runtime explicitly.
    & $dotnet restore $appProject --locked-mode
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
    & $dotnet restore $bindingToolProject --locked-mode
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
    & $dotnet restore $maintenanceProject --locked-mode
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    $output = Join-Path $projectRoot 'artifacts\app'
    $rootPrefix = [IO.Path]::GetFullPath($projectRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $resolvedOutput = [IO.Path]::GetFullPath($output)
    Assert-ReleasePath -Path $resolvedOutput -RepositoryRoot $projectRoot
    if (-not $resolvedOutput.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path -Leaf $resolvedOutput) -ne 'app') {
        throw "Refusing to clean unexpected publish path: $resolvedOutput"
    }

    if (Test-Path -LiteralPath $resolvedOutput) {
        Assert-ReleaseNotInUse -Path $resolvedOutput -RepositoryRoot $projectRoot
        Remove-ReleaseTree -Path $resolvedOutput -RepositoryRoot $projectRoot
    }

    & $dotnet publish `
        $bindingToolProject `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --no-restore `
        --output $resolvedOutput
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    & $dotnet publish `
        $maintenanceProject `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --no-restore `
        --output $resolvedOutput
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    & $dotnet publish `
        $appProject `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --no-restore `
        --output $resolvedOutput
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    $requiredTransportFiles = @(
        'resources\android-platform-tools\adb.exe',
        'resources\android-platform-tools\AdbWinApi.dll',
        'resources\android-platform-tools\AdbWinUsbApi.dll',
        'resources\scrcpy\scrcpy-server-v4.1'
    )
    foreach ($relativePath in $requiredTransportFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $resolvedOutput $relativePath) -PathType Leaf)) {
            throw "Published application is missing required transport file: $relativePath"
        }
    }

    $bindingToolExecutable = Join-Path $resolvedOutput `
        'VRPhoneScreenOverlay.SteamVR.BindingTool.exe'
    if (-not (Test-Path -LiteralPath $bindingToolExecutable -PathType Leaf)) {
        throw 'Published application is missing the SteamVR local-binding activation tool.'
    }

    $maintenanceExecutable = Join-Path $resolvedOutput 'VRPhoneScreenOverlay.Maintenance.exe'
    if (-not (Test-Path -LiteralPath $maintenanceExecutable -PathType Leaf)) {
        throw 'Published application is missing the external update maintenance helper.'
    }

    $publishedExecutable = Join-Path $resolvedOutput 'VRPhoneScreenOverlay.exe'
    Write-ClientServiceConfiguration -ProjectRoot $projectRoot -OutputDirectory $resolvedOutput -ConfigurationPath $ConfigurationPath
    $smokeProcess = Start-Process `
        -FilePath $publishedExecutable `
        -ArgumentList '--smoke-test' `
        -Wait `
        -PassThru `
        -WindowStyle Hidden
    if ($smokeProcess.ExitCode -ne 0) {
        throw "Published application smoke test failed with exit code $($smokeProcess.ExitCode)."
    }

    # Focused callers run affected tests/snapshots separately. Compilation,
    # staging smoke checks and transactional release rotation always remain.
    if (-not $FocusedValidation) {
        $uiSnapshotOutput = Join-Path $projectRoot 'artifacts\ui-snapshots'
        $snapshotProcess = Start-Process `
            -FilePath $publishedExecutable `
            -ArgumentList @('--ui-snapshot', '--ui-snapshot-output', $uiSnapshotOutput) `
            -Wait `
            -PassThru `
            -WindowStyle Hidden
        if ($snapshotProcess.ExitCode -ne 0) {
            throw "Published UI snapshot generation failed with exit code $($snapshotProcess.ExitCode)."
        }

        $requiredUiSnapshots = @(
            'home-empty.png',
            'wireless-empty.png',
            'wireless-refreshed.png',
            'wireless-busy.png',
            'wireless-error.png',
            'wireless-ready.png',
            'wireless-code.png',
            'wireless-code-busy.png',
            'wireless-code-error.png',
            'wireless-manual.png',
            'wireless-manual-busy.png',
            'wireless-manual-error.png',
            'wireless-manual-connect.png',
            'home-connected.png',
            'home-wireless-busy.png',
            'home-opening.png',
            'home-running.png',
            'home-closing.png',
            'home-error.png',
            'home-reset-unavailable.png',
            'motion-default.png',
            'motion-advanced.png',
            'motion-reset-all.png',
            'settings.png',
            'video.png',
            'about.png',
            'diagnostic-report.png',
            'manifest.json'
        )
        foreach ($fileName in $requiredUiSnapshots) {
            $snapshotPath = Join-Path $uiSnapshotOutput $fileName
            if (-not (Test-Path -LiteralPath $snapshotPath -PathType Leaf) -or
                (Get-Item -LiteralPath $snapshotPath).Length -le 0) {
                throw "UI snapshot generation did not produce: $fileName"
            }
        }

        Write-Host "Local test build: $resolvedOutput" -ForegroundColor Green
        Write-Host "UI snapshots: $uiSnapshotOutput" -ForegroundColor Green
    }

    if ($PackageOnly) {
        # Release preparation leaves the user's running installation untouched.
        $packageInstall = Join-Path $projectRoot 'artifacts\package-install\VRPhoneScreenOverlay'
        & (Join-Path $PSScriptRoot 'Prepare-ReleaseFolder.ps1') -SourceDirectory $resolvedOutput -OutputDirectory $packageInstall
    } else {
        & (Join-Path $PSScriptRoot 'Prepare-ReleaseFolder.ps1') -SourceDirectory $resolvedOutput
    }
    # Prepare-ReleaseFolder validates the staged portable host before rotating the
    # latest and previous final products. A failed validation leaves both untouched.
} finally {
    $buildLock.Dispose()
}
