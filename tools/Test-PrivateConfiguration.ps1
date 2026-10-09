[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ServiceConfiguration.Common.ps1')
$root = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path $root ('artifacts/configuration-tests/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'service.config.example.json') -Destination $fixture
Write-ClientServiceConfiguration -ProjectRoot $fixture -OutputDirectory $fixture
$public = Get-Content -LiteralPath (Join-Path $fixture 'service.config.json') -Raw | ConvertFrom-Json
if ($null -ne $public.Client.UpdateManifestUri -or $public.PSObject.Properties.Name -contains 'Deployment') { throw 'Unconfigured source build leaked maintenance fields.' }
$private = Get-Content -LiteralPath (Join-Path $fixture 'service.config.example.json') -Raw | ConvertFrom-Json
$private.Client.UpdateManifestUri = 'https://service.invalid/manifest'
$private.Deployment | Add-Member -NotePropertyName Token -NotePropertyValue 'synthetic-maintenance-secret'
$private | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $fixture 'service.private.json') -Encoding utf8
Write-ClientServiceConfiguration -ProjectRoot $fixture -OutputDirectory $fixture
$text = Get-Content -LiteralPath (Join-Path $fixture 'service.config.json') -Raw
if ($text.Contains('synthetic-maintenance-secret') -or $text.Contains('Deployment')) { throw 'Client projection leaked a credential.' }
$private.Client | Add-Member -NotePropertyName Token -NotePropertyValue 'synthetic-maintenance-secret'
$private | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $fixture 'service.private.json') -Encoding utf8
$rejected = $false
try { Write-ClientServiceConfiguration -ProjectRoot $fixture -OutputDirectory $fixture }
catch { $rejected = $true }
if (-not $rejected) { throw 'Client credential field was accepted.' }
$private.Client.PSObject.Properties.Remove('Token')
$private.Client.UpdateManifestUri = 'https://service.invalid/manifest?%74oken=synthetic'
$private | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $fixture 'service.private.json') -Encoding utf8
$rejected = $false
try { Write-ClientServiceConfiguration -ProjectRoot $fixture -OutputDirectory $fixture }
catch { $rejected = $true }
if (-not $rejected) { throw 'Client endpoint query credential was accepted.' }
Write-Host 'Private configuration isolation validation passed.'
