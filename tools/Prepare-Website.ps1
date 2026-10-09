[CmdletBinding()]
param([string]$PortableZip = '', [string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'artifacts/public-website/vrpso' }
$destination = [IO.Path]::GetFullPath($OutputDirectory)
$allowed = [IO.Path]::GetFullPath((Join-Path $root 'artifacts')).TrimEnd('\') + '\'
if (-not $destination.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Website output must stay in repository artifacts.' }
[IO.Directory]::CreateDirectory($destination) | Out-Null
foreach ($name in @('index.html', 'styles.css', 'app.js', 'release-notes.html', 'README.md')) {
    Copy-Item -LiteralPath (Join-Path $root "website/vrpso/$name") -Destination (Join-Path $destination $name)
}
$version = (Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()
$config = Get-Content -LiteralPath (Join-Path $root 'website/vrpso/release.template.json') -Raw | ConvertFrom-Json
$config.version = $version
if ($PortableZip) {
    $zip = (Resolve-Path -LiteralPath $PortableZip).Path
    $downloads = Join-Path $destination 'downloads'
    [IO.Directory]::CreateDirectory($downloads) | Out-Null
    Copy-Item -LiteralPath $zip -Destination (Join-Path $downloads ([IO.Path]::GetFileName($zip)))
    $config.downloadUrl = './downloads/' + [IO.Path]::GetFileName($zip)
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $downloads 'SHA256SUMS.txt'), "$hash  $([IO.Path]::GetFileName($zip))`n", [Text.UTF8Encoding]::new($false))
}
[IO.File]::WriteAllText((Join-Path $destination 'release.json'), ($config | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
Write-Host "Website prepared locally: $destination"
