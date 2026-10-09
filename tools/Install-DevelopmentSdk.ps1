[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$installDirectory = Join-Path $projectRoot '.dotnet'
$dotnetExecutable = Join-Path $installDirectory 'dotnet.exe'
$requiredVersion = '10.0.400'

if (Test-Path -LiteralPath $dotnetExecutable) {
    $installedVersion = (& $dotnetExecutable --version).Trim()
    if ($installedVersion -eq $requiredVersion) {
        Write-Host "The required .NET SDK $requiredVersion is already installed."
        exit 0
    }
}

$workDirectory = Join-Path $projectRoot 'work'
New-Item -ItemType Directory -Force -Path $workDirectory | Out-Null
$installer = Join-Path $workDirectory 'dotnet-install.ps1'
Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer `
    -Version $requiredVersion `
    -InstallDir $installDirectory `
    -NoPath
if ($LASTEXITCODE -ne 0) {
    throw "Installing .NET SDK $requiredVersion failed with exit code $LASTEXITCODE."
}
