[CmdletBinding()]
param(
    [switch]$LockedRestore
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $projectRoot 'VRPhoneScreenOverlay.slnx'
$localDotnet = Join-Path $projectRoot '.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) {
    $localDotnet
} else {
    (Get-Command dotnet -ErrorAction Stop).Source
}

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

function Invoke-Checked([string]$Description, [scriptblock]$Action) {
    Write-Host "`n== $Description ==" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

Push-Location $projectRoot
try {
    $restoreArguments = @('restore', $solution)
    if ($LockedRestore) {
        $restoreArguments += '--locked-mode'
    }

    Invoke-Checked 'Restore locked dependencies' { & $dotnet @restoreArguments }
    Invoke-Checked 'Verify formatting and analyzers' {
        & $dotnet format $solution --verify-no-changes --no-restore
    }
    Invoke-Checked 'Build Release with warnings as errors' {
        & $dotnet build $solution --configuration Release --no-restore
    }
    Invoke-Checked 'Run automated tests' {
        & $dotnet test $solution --configuration Release --no-build --no-restore
    }

    Write-Host "`n== Validate central version inventory ==" -ForegroundColor Cyan
    $version = (Get-Content -LiteralPath 'VERSION' -Raw -Encoding utf8).Trim()
    $directoryBuild = [xml](Get-Content -LiteralPath 'Directory.Build.props' -Raw -Encoding utf8)
    $versionGroups = @($directoryBuild.Project.PropertyGroup | Where-Object {
            $null -ne $_.SelectSingleNode('VersionPrefix')
        })
    if ($versionGroups.Count -ne 1) {
        throw 'Directory.Build.props must contain exactly one VersionPrefix element.'
    }

    $buildVersion = ([string]$versionGroups[0].VersionPrefix).Trim()
    if ($version -ne $buildVersion) {
        throw "VERSION ($version) does not match VersionPrefix ($buildVersion)."
    }

    Invoke-Checked 'Validate release automation scripts' {
        & powershell -NoProfile -ExecutionPolicy Bypass -File `
            'tools\Test-ReleaseAutomation.ps1'
    }

    Invoke-Checked 'Validate user-uploaded cloud publishing offline' {
        & pwsh -NoProfile -File 'tools\Test-ManualCloudPublishing.ps1'
    }

    Invoke-Checked 'Validate private configuration isolation offline' {
        & powershell -NoProfile -ExecutionPolicy Bypass -File 'tools\Test-PrivateConfiguration.ps1'
    }

    Invoke-Checked 'Validate public source privacy boundaries offline' {
        & python 'tools\Test-PublicSource.py'
    }
    Invoke-Checked 'Validate complete repository audit offline' {
        & python 'tools\Test-PrivateRepositoryAudit.py'
    }

    Invoke-Checked 'Validate local release rotation offline' {
        & powershell -NoProfile -ExecutionPolicy Bypass -File `
            'tools\Test-ReleaseFolder.ps1'
    }

    Invoke-Checked 'Validate service deployment transactions offline' {
        & powershell -NoProfile -ExecutionPolicy Bypass -File `
            'tools\Test-ServiceDeployment.ps1'
    }

    $updatePublicKey = 'src/VRPhoneScreenOverlay.Update/Resources/update-public-key.pem'
    if (-not (Test-Path -LiteralPath $updatePublicKey -PathType Leaf)) {
        throw 'The embedded update verification public key is missing.'
    }
    $updatePublicKeyText = Get-Content -LiteralPath $updatePublicKey -Raw -Encoding utf8
    if ($updatePublicKeyText -notmatch '-----BEGIN PUBLIC KEY-----') {
        throw 'The update verification resource must contain a public key only.'
    }
    & git ls-files --error-unmatch -- $updatePublicKey | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'The update verification public key must be tracked by Git.'
    }

    $scrcpyManifest = Get-Content -LiteralPath `
        'src\VRPhoneScreenOverlay.Android\Resources\Scrcpy\manifest.json' `
        -Raw -Encoding utf8 | ConvertFrom-Json
    if ([string]$scrcpyManifest.version -ne '4.1') {
        throw 'The Android protocol implementation is pinned to scrcpy 4.1.'
    }
    if ([int]$scrcpyManifest.customization.controlMessageType -ne 127) {
        throw 'The customized scrcpy server must preserve USER_ACTIVITY message type 127.'
    }
    if ([int]$scrcpyManifest.customization.guardedWakeControlMessageType -ne 128) {
        throw 'The customized scrcpy server must support guarded wake message type 128.'
    }
    $scrcpyPatchPath = Join-Path $projectRoot `
        ([string]$scrcpyManifest.customization.patch -replace '/', '\')
    if (-not (Test-Path -LiteralPath $scrcpyPatchPath -PathType Leaf)) {
        throw 'The customized scrcpy server patch is missing.'
    }
    if ((Get-FileHash -LiteralPath $scrcpyPatchPath -Algorithm SHA256).Hash -ine
        [string]$scrcpyManifest.customization.patchSha256) {
        throw 'The scrcpy patch does not match the source recorded for the bundled server.'
    }
    $scrcpyRuntimeValidator = Get-Content -LiteralPath `
        'src\VRPhoneScreenOverlay.Android\ScrcpyServerResourceValidator.cs' `
        -Raw -Encoding utf8
    if ($scrcpyRuntimeValidator -match '(?i)[0-9a-f]{64}') {
        throw 'The scrcpy runtime validator must read its hash from manifest.json.'
    }

    Write-Host "`n== Validate SteamVR JSON ==" -ForegroundColor Cyan
    $actionManifest = Get-Content -LiteralPath 'action_manifest.json' -Raw -Encoding utf8 |
        ConvertFrom-Json
    $actions = @($actionManifest.actions | ForEach-Object { $_.name })
    $steamVrApplicationManifest = Get-Content -LiteralPath `
        'src\VRPhoneScreenOverlay.SteamVR\Resources\SteamVR\manifest.vrmanifest' `
        -Raw -Encoding utf8 | ConvertFrom-Json
    $steamVrApplication = @($steamVrApplicationManifest.applications) |
        Where-Object { $_.app_key -eq 'local.spacedraglite.desktop.v1' } |
        Select-Object -First 1
    if ($null -eq $steamVrApplication -or
        $steamVrApplication.binary_path_windows -ne 'VRPhoneScreenOverlay.exe') {
        throw 'SteamVR application manifest does not preserve the stable app key and new executable.'
    }
    if ($steamVrApplication.action_manifest_path -ne 'action_manifest.json') {
        throw 'SteamVR application manifest must declare the packaged action manifest path.'
    }
    if ($steamVrApplication.is_dashboard_overlay -ne $true) {
        throw 'SteamVR autolaunch requires is_dashboard_overlay=true in the application manifest.'
    }

    foreach ($defaultBinding in @($actionManifest.default_bindings)) {
        $defaultBindingPath = Join-Path $projectRoot $defaultBinding.binding_url
        if (-not (Test-Path -LiteralPath $defaultBindingPath -PathType Leaf)) {
            throw "Missing default SteamVR binding: $($defaultBinding.binding_url)."
        }
    }

    foreach ($bindingFile in Get-ChildItem -LiteralPath 'bindings' -Filter '*.json') {
        $bindingText = Get-Content -LiteralPath $bindingFile.FullName -Raw -Encoding utf8
        $null = $bindingText | ConvertFrom-Json
        foreach ($match in [regex]::Matches(
            $bindingText,
            '/actions/[A-Za-z0-9_]+/(?:in|out)/[A-Za-z0-9_]+')) {
            if ($match.Value -notin $actions) {
                throw "$($bindingFile.Name) references unknown action $($match.Value)."
            }
        }
    }

    Write-Host "`n== Validate bundled Android tools ==" -ForegroundColor Cyan
    $androidTools = 'src\VRPhoneScreenOverlay.Android\Resources\AndroidPlatformTools'
    $androidManifest = Get-Content -LiteralPath (Join-Path $androidTools 'manifest.json') `
        -Raw -Encoding utf8 | ConvertFrom-Json
    foreach ($file in $androidManifest.files.PSObject.Properties) {
        $toolPath = Join-Path $androidTools $file.Name
        if (-not (Test-Path -LiteralPath $toolPath -PathType Leaf)) {
            throw "Missing bundled Android tool: $($file.Name)."
        }

        $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $toolPath).Hash.ToLowerInvariant()
        if ($actualHash -ne [string]$file.Value) {
            throw "Bundled Android tool hash mismatch: $($file.Name)."
        }
    }

    $scrcpyResources = 'src\VRPhoneScreenOverlay.Android\Resources\Scrcpy'
    $scrcpyManifest = Get-Content -LiteralPath (Join-Path $scrcpyResources 'manifest.json') `
        -Raw -Encoding utf8 | ConvertFrom-Json
    foreach ($file in $scrcpyManifest.files.PSObject.Properties) {
        $resourcePath = Join-Path $scrcpyResources $file.Name
        if (-not (Test-Path -LiteralPath $resourcePath -PathType Leaf)) {
            throw "Missing bundled scrcpy resource: $($file.Name)."
        }

        $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $resourcePath).Hash.ToLowerInvariant()
        if ($actualHash -ne [string]$file.Value) {
            throw "Bundled scrcpy resource hash mismatch: $($file.Name)."
        }
    }

    $openVrResources = 'src\VRPhoneScreenOverlay.SteamVR\Resources\OpenVR'
    $openVrManifest = Get-Content -LiteralPath (Join-Path $openVrResources 'manifest.json') `
        -Raw -Encoding utf8 | ConvertFrom-Json
    foreach ($file in $openVrManifest.files.PSObject.Properties) {
        $resourcePath = Join-Path $openVrResources $file.Name
        if (-not (Test-Path -LiteralPath $resourcePath -PathType Leaf)) {
            throw "Missing bundled OpenVR resource: $($file.Name)."
        }

        $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $resourcePath).Hash.ToLowerInvariant()
        if ($actualHash -ne [string]$file.Value) {
            throw "Bundled OpenVR resource hash mismatch: $($file.Name)."
        }
    }

    $bindingPath = Join-Path $openVrResources $openVrManifest.binding.path
    $bindingHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $bindingPath).Hash.ToLowerInvariant()
    if ($bindingHash -ne [string]$openVrManifest.binding.packagedSha256) {
        throw 'Bundled OpenVR C# binding hash mismatch.'
    }

    Write-Host "`n== Check forbidden new-code patterns ==" -ForegroundColor Cyan
    $newSourceFiles = Get-ChildItem -Path 'src\VRPhoneScreenOverlay.*' -Recurse -Filter '*.cs'
    $forbiddenPatterns = @(
        @{ Name = 'desktop capture'; Pattern = 'PrintWindow|BitBlt' },
        @{ Name = 'modal desktop dialog'; Pattern = 'MessageBox\.Show' },
        @{ Name = 'unbounded channels'; Pattern = 'Channel\.CreateUnbounded' },
        @{ Name = 'blocking sleep'; Pattern = 'Thread\.Sleep' }
    )
    foreach ($rule in $forbiddenPatterns) {
        $matches = $newSourceFiles | Select-String -Pattern $rule.Pattern
        if ($matches) {
            throw "Forbidden $($rule.Name) usage: $($matches.Path -join ', ')."
        }
    }

    Write-Host "`n== Check release and secret inventory ==" -ForegroundColor Cyan
    $looseSourceFiles = Get-ChildItem -LiteralPath 'src' -File -Filter '*.cs'
    if ($looseSourceFiles) {
        throw "Loose legacy source files are forbidden: $($looseSourceFiles.Name -join ', ')."
    }

    $legacyPaths = @(
        'runtime',
        'scrcpy',
        'server',
        'manifest.vrmanifest',
        'release-files.txt',
        'service.example.ini',
        'tools\Publish-Update.ps1'
    )
    $remainingLegacyPaths = $legacyPaths | Where-Object { Test-Path -LiteralPath $_ }
    if ($remainingLegacyPaths) {
        throw "Legacy project content is forbidden: $($remainingLegacyPaths -join ', ')."
    }

    $trackedFiles = & git ls-files
    $forbiddenTracked = $trackedFiles | Where-Object {
        $_ -ne $updatePublicKey -and
        $_ -match '(?i)(^|/)(service\.ini|service\.private\.json|service\.config\.json|release\.local\.psd1|\.env|.*\.(pfx|pem|key)|.*Diagnostics.*\.zip)$'
    }
    if ($forbiddenTracked) {
        throw "Tracked private files: $($forbiddenTracked -join ', ')."
    }

    # Empty protocol password fields are not credentials. Print file names only,
    # so an actual finding never exposes a credential through verification logs.
    $secretPattern = @'
(BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY|uploadKey=[^C]|password[[:space:]]*[:=][[:space:]]*["'][^"'[:space:]]+["'])
'@
    $secretHits = & git grep -I -l -E $secretPattern `
        -- . ':(exclude)tools/Verify.ps1'
    if ($LASTEXITCODE -eq 0 -and $secretHits) {
        throw "Potential tracked secret patterns were found:`n$($secretHits -join "`n")"
    }

    Invoke-Checked 'Git whitespace check' { & git diff HEAD --check }
    Write-Host "`nAll VRPhoneScreen Overlay verification gates passed." -ForegroundColor Green
}
finally {
    Pop-Location
}
