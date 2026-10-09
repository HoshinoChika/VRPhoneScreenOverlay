[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'ServiceDeployment.Common.ps1')

function Assert-ServiceTest([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-ServiceThrows([scriptblock]$Action, [string]$Message) {
    try { & $Action } catch { return }
    throw $Message
}

foreach ($name in @('Deploy-Service.ps1', 'ServiceDeployment.Common.ps1', 'Test-ServiceDeployment.ps1')) {
    $tokens = $null
    $parseErrors = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile(
        (Join-Path $PSScriptRoot $name), [ref]$tokens, [ref]$parseErrors)
    Assert-ServiceTest ($parseErrors.Count -eq 0) "$name has parse errors."
}

foreach ($name in @('Deploy-Service.ps1', 'Build-Local.ps1')) {
    $scriptText = Get-Content -LiteralPath (Join-Path $PSScriptRoot $name) -Raw -Encoding utf8
    Assert-ServiceTest ($scriptText -notmatch '(?m)&\s*\$dotnet\s+restore[^\r\n]*(?:--runtime|-r\b)') `
        "$name overrides project RIDs during locked restore. Select the runtime only during publish."
    Assert-ServiceTest ($scriptText -notmatch 'GenerateDocumentationFile=false|EnableNETAnalyzers=false') `
        "$name disables the repository's compiler/analyzer prerequisites."
}

$gitRoot = Split-Path -Parent (Split-Path -Parent (Get-Command git -ErrorAction Stop).Source)
$bash = Join-Path $gitRoot 'bin\bash.exe'
if (-not (Test-Path -LiteralPath $bash)) {
    throw 'Service deployment tests require the Bash bundled with Git for Windows.'
}
$testRoot = Join-Path $projectRoot "artifacts\service-deployment-tests\$([Guid]::NewGuid().ToString('N'))"
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$driverPath = Join-Path $testRoot 'mock-remote.sh'
$driver = @'
#!/usr/bin/env bash
set -eu
fixture="$1"
operation="$2"
failure="$3"
# These are shell mocks. No systemd, HTTP or /proc access reaches the machine.
flock() { return 0; }
timeout() { shift; "$@"; }
sleep() { :; }
systemctl() {
    case "$1" in
        cat)
            cat "$fixture/original-unit"
            for item in "$fixture/systemd/vrphonescreen.service.d/"*.conf; do
                if test -f "$item"; then cat "$item"; fi
            done
            ;;
        daemon-reload)
            if test "$failure" = reload && test -f "$fixture/systemd/vrphonescreen.service.d/20-vrphone-bundle-extraction.conf" &&
                grep -q '.dotnet-bundle' "$fixture/systemd/vrphonescreen.service.d/20-vrphone-bundle-extraction.conf"; then
                return 1
            fi
            ;;
        show) printf '%s\n' '4242';;
        is-active) return 0;;
        restart)
            printf '%s\n' "$(cat "$fixture/VRPhoneScreenOverlay.Service")" >> "$fixture/restarts"
            if test "$failure" = restart && test "$(cat "$fixture/VRPhoneScreenOverlay.Service")" = new; then return 1; fi
            ;;
        *) return 99;;
    esac
}
sha256sum() {
    if test "${1:-}" = --; then shift; fi
    if test "${1:-}" = '/proc/4242/exe'; then
        command sha256sum "$fixture/VRPhoneScreenOverlay.Service"
    else
        command sha256sum "$@"
    fi
}
curl() {
    if test "$(cat "$fixture/VRPhoneScreenOverlay.Service")" = new; then
        case "$failure:$*" in
            private:*http://127.0.0.1*|public:*https://*) return 1;;
        esac
    fi
    printf '%s\n' '{"ok":true}'
}
set -- "$operation"
source "$fixture/deploy.sh"
'@
[IO.File]::WriteAllText($driverPath, $driver.Replace("`r`n", "`n") + "`n", [Text.UTF8Encoding]::new($false))

function ConvertTo-ServiceTestUnixPath([string]$Path) {
    $normalized = $Path.Replace('\', '/')
    return '/' + $normalized.Substring(0, 1).ToLowerInvariant() + $normalized.Substring(2)
}

function New-ServiceFixture([string]$Name, [switch]$Tamper, [switch]$ExistingDropIn) {
    $directory = Join-Path $testRoot $Name
    $token = '0123456789abcdef0123456789abcdef'
    $stage = Join-Path $directory ".service-deploy-$token"
    [IO.Directory]::CreateDirectory($stage) | Out-Null
    $current = Join-Path $directory 'VRPhoneScreenOverlay.Service'
    $candidate = Join-Path $stage 'VRPhoneScreenOverlay.Service.upload'
    [IO.File]::WriteAllText($current, 'old', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($candidate, 'new', [Text.UTF8Encoding]::new($false))
    $dropInDirectory = Join-Path $directory 'systemd\vrphonescreen.service.d'
    [IO.Directory]::CreateDirectory($dropInDirectory) | Out-Null
    [IO.File]::WriteAllText((Join-Path $directory 'original-unit'), 'unchanged original unit')
    [IO.File]::WriteAllText((Join-Path $dropInDirectory '10-loopback.conf'), 'unchanged loopback isolation')
    $managedDropIn = Join-Path $dropInDirectory '20-vrphone-bundle-extraction.conf'
    if ($ExistingDropIn) { [IO.File]::WriteAllText($managedDropIn, 'previous custom extraction setting') }
    $hash = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToLowerInvariant()
    $script = New-ServiceDeploymentScript -Token $token -ExpectedHash $hash `
        -PublicHealthUri 'https://example.invalid/vrphonescreen/api/v1/health'
    $script = $script.Replace(
        (ConvertTo-ServiceShellLiteral '/opt/vrphonescreen-service'),
        (ConvertTo-ServiceShellLiteral (ConvertTo-ServiceTestUnixPath $directory)))
    $script = $script.Replace('/etc/systemd/system/', (ConvertTo-ServiceTestUnixPath $directory) + '/systemd/')
    Assert-ServiceTest (-not $script.Contains('/etc/systemd/system/')) 'Fixture would touch real systemd paths.'
    [IO.File]::WriteAllText((Join-Path $directory 'deploy.sh'), $script, [Text.UTF8Encoding]::new($false))
    if ($Tamper) { [IO.File]::WriteAllText($candidate, 'tampered') }
    return [PSCustomObject]@{
        Directory = $directory; Current = $current; Stage = $stage; ManagedDropIn = $managedDropIn
    }
}

function Invoke-ServiceFixture([object]$Fixture, [string]$Mode, [string]$Failure = 'none') {
    $savedPreference = $ErrorActionPreference
    try {
        # Fault scenarios intentionally emit stderr; capture it as evidence.
        $ErrorActionPreference = 'Continue'
        $lines = @(& $bash --noprofile --norc (ConvertTo-ServiceTestUnixPath $driverPath) `
            (ConvertTo-ServiceTestUnixPath $Fixture.Directory) $Mode $Failure 2>&1)
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $savedPreference
    }
    [IO.File]::WriteAllText((Join-Path $Fixture.Directory "$Mode-$Failure.output.txt"), ($lines -join "`n"))
    return $code
}

$success = New-ServiceFixture 'success' -ExistingDropIn
Assert-ServiceTest ((Invoke-ServiceFixture $success 'apply') -eq 0) 'Healthy service deployment failed.'
Assert-ServiceTest ([IO.File]::ReadAllText($success.Current) -eq 'new') 'Healthy deployment did not install new binary.'
Assert-ServiceTest ([IO.File]::ReadAllText((Join-Path $success.Stage 'previous')) -eq 'old') 'Old rollback binary was not preserved.'
Assert-ServiceTest ([IO.File]::ReadAllText($success.ManagedDropIn).Contains('DOTNET_BUNDLE_EXTRACT_BASE_DIR=/var/lib/vrphonescreen-service/.dotnet-bundle')) `
    'Deployment did not provide the service extraction directory.'
Assert-ServiceTest ((Invoke-ServiceFixture $success 'verify') -eq 0) 'Installed/running hash verification failed.'
Assert-ServiceTest ((Invoke-ServiceFixture $success 'apply') -ne 0) 'Reusing a transaction token overwrote its backup.'
Assert-ServiceTest ((Invoke-ServiceFixture $success 'rollback') -eq 0) 'Explicit rollback after failed independent health check failed.'
Assert-ServiceTest ([IO.File]::ReadAllText($success.Current) -eq 'old') 'Rollback did not restore old binary.'
Assert-ServiceTest ([IO.File]::ReadAllText($success.ManagedDropIn) -eq 'previous custom extraction setting') `
    'Rollback did not restore an existing managed drop-in.'

foreach ($failure in @('reload', 'restart', 'private', 'public')) {
    $fixture = New-ServiceFixture $failure
    Assert-ServiceTest ((Invoke-ServiceFixture $fixture 'apply' $failure) -ne 0) "$failure failure was accepted."
    Assert-ServiceTest ([IO.File]::ReadAllText($fixture.Current) -eq 'old') "$failure failure did not roll back."
    Assert-ServiceTest ([IO.File]::ReadAllText((Join-Path $fixture.Stage 'state')).Trim() -eq 'rolled-back') `
        "$failure failure did not confirm rollback."
    Assert-ServiceTest (-not (Test-Path -LiteralPath $fixture.ManagedDropIn)) "$failure rollback kept the newly added drop-in."
    Assert-ServiceTest ([IO.File]::ReadAllText((Join-Path $fixture.Directory 'systemd\vrphonescreen.service.d\10-loopback.conf')) -eq 'unchanged loopback isolation') `
        "$failure changed the existing loopback drop-in."
    Assert-ServiceTest ([IO.File]::ReadAllText((Join-Path $fixture.Directory 'original-unit')) -eq 'unchanged original unit') `
        "$failure changed the existing service unit."
}

$tampered = New-ServiceFixture 'hash-mismatch' -Tamper
Assert-ServiceTest ((Invoke-ServiceFixture $tampered 'apply') -ne 0) 'Mismatched upload hash was accepted.'
Assert-ServiceTest ([IO.File]::ReadAllText($tampered.Current) -eq 'old') 'Hash rejection changed installed binary.'
Assert-ServiceTest (-not (Test-Path -LiteralPath (Join-Path $tampered.Directory 'restarts'))) 'Hash rejection restarted service.'

$changed = New-ServiceFixture 'outside-change'
Assert-ServiceTest ((Invoke-ServiceFixture $changed 'apply') -eq 0) 'Outside-change fixture setup failed.'
[IO.File]::WriteAllText($changed.Current, 'different-admin-binary')
Assert-ServiceTest ((Invoke-ServiceFixture $changed 'rollback') -ne 0) 'Rollback overwrote an unrelated later binary.'
Assert-ServiceTest ([IO.File]::ReadAllText($changed.Current) -eq 'different-admin-binary') 'Foreign binary was modified.'

$changedDropIn = New-ServiceFixture 'outside-dropin-change'
Assert-ServiceTest ((Invoke-ServiceFixture $changedDropIn 'apply') -eq 0) 'Drop-in fixture setup failed.'
[IO.File]::WriteAllText($changedDropIn.ManagedDropIn, 'different administrator setting')
Assert-ServiceTest ((Invoke-ServiceFixture $changedDropIn 'rollback') -ne 0) 'Rollback overwrote a later administrator drop-in.'
Assert-ServiceTest ([IO.File]::ReadAllText($changedDropIn.ManagedDropIn) -eq 'different administrator setting') 'Foreign drop-in was modified.'

$invalidBinary = Join-Path $testRoot 'not-linux.exe'
[IO.File]::WriteAllText($invalidBinary, 'not an ELF file')
Assert-ServiceThrows { Assert-ServiceBinary $invalidBinary } 'A non-Linux artifact was accepted.'
Assert-ServiceThrows {
    New-ServiceDeploymentScript -Token ('a' * 32) -ExpectedHash ('b' * 64) `
        -PublicHealthUri 'http://example.invalid/vrphonescreen/api/v1/health'
} 'Plain HTTP public probe was accepted.'
Assert-ServiceThrows {
    New-ServiceDeploymentScript -Token ('a' * 32) -ExpectedHash ('b' * 64) `
        -PublicHealthUri 'https://example.invalid/vrphonescreen/api/v1/health' -InstallDirectory '/opt/../etc'
} 'Traversing deployment directory was accepted.'

Write-Host 'Service deployment validation passed (offline shell transaction, rollback and hash checks).' -ForegroundColor Green
exit 0
