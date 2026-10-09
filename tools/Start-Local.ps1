[CmdletBinding()]
param(
    [string]$ExecutablePath = '',
    [string]$ArgumentString = ''
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
    $ExecutablePath = Join-Path $projectRoot 'release\VRPhoneScreenOverlay\VRPhoneScreenOverlay.exe'
}
if (-not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf)) {
    throw 'The release executable is missing. Run build.bat to build and verify the final release folder first.'
}
$executable = (Resolve-Path -LiteralPath $ExecutablePath).ProviderPath
$rootPrefix = [IO.Path]::GetFullPath($projectRoot).TrimEnd('\') + '\'
if (-not $executable.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($executable) -ne 'VRPhoneScreenOverlay.exe') {
    throw 'Start-Local only starts this repository application.'
}
if (@(Get-Process VRPhoneScreenOverlay -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'Close the running application before starting a new instance.'
}

# asInvoker inherits elevation from its parent. Ask the existing desktop shell
# to launch, so elevated development terminals cannot accidentally elevate VR.
# https://devblogs.microsoft.com/oldnewthing/20131118-00/?p=2643
$shell = $windows = $desktop = $view = $application = $null
$startedAfter = (Get-Date).AddSeconds(-1)
try {
    $shell = New-Object -ComObject Shell.Application
    $windows = $shell.Windows()
    $location = [object]0
    $root = [object]0
    $desktopHwnd = 0
    $desktop = $windows.FindWindowSW([ref]$location, [ref]$root, 8, [ref]$desktopHwnd, 1)
    if ($null -eq $desktop) { throw 'The interactive Windows desktop is unavailable.' }
    $view = $desktop.Document
    $application = $view.Application
    $application.ShellExecute($executable, $ArgumentString, (Split-Path -Parent $executable), 'open', 1)
} finally {
    foreach ($comObject in @($application, $view, $desktop, $windows, $shell)) {
        if ($null -ne $comObject -and [Runtime.InteropServices.Marshal]::IsComObject($comObject)) {
            [void][Runtime.InteropServices.Marshal]::ReleaseComObject($comObject)
        }
    }
}

$deadline = (Get-Date).AddSeconds(15)
do {
    $instance = Get-Process VRPhoneScreenOverlay -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $executable -and $_.StartTime -ge $startedAfter } |
        Select-Object -First 1
    if ($null -ne $instance -and $instance.MainWindowHandle -ne 0) {
        Write-Output "Started through Windows desktop: PID=$($instance.Id); path=$executable"
        return
    }
    Start-Sleep -Milliseconds 200
} while ((Get-Date) -lt $deadline)
throw 'Application did not create its main window within 15 seconds.'
