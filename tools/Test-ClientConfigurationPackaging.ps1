[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'ReleaseAutomation.Common.ps1')
$fixture = Join-Path $projectRoot ('artifacts\client-config-package-tests\' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
$source = Join-Path $fixture 'source.json'
$installed = Join-Path $fixture 'installed.json'
[IO.File]::WriteAllText($source, '{"SchemaVersion":1,"Client":{"UpdateManifestUri":"https://service.invalid/update","UpdateChannel":"beta"}}')
[IO.File]::WriteAllText($installed, '{ "Client": { "UpdateChannel": "beta", "UpdateManifestUri": "https://service.invalid/update" }, "SchemaVersion": 1 }')
Assert-EquivalentClientConfiguration -SourcePath $source -InstalledPath $installed
foreach ($invalid in @(
        '{"SchemaVersion":1,"Client":{"UpdateManifestUri":"https://service.invalid/other","UpdateChannel":"beta"}}',
        '{"SchemaVersion":1,"Client":{"UpdateManifestUri":"https://service.invalid/update","UpdateChannel":"beta","Secret":"synthetic"}}',
        '{"SchemaVersion":1,"Client":{"UpdateManifestUri":"https://service.invalid/update","UpdateChannel":"beta"},"Deployment":{}}')) {
    [IO.File]::WriteAllText($installed, $invalid)
    $rejected = $false
    try { Assert-EquivalentClientConfiguration -SourcePath $source -InstalledPath $installed }
    catch { $rejected = $true }
    if (-not $rejected) { throw 'Changed or private client configuration was accepted.' }
}
Write-Output 'Client configuration packaging checks passed (format-only equality and three rejected value/field changes).'
