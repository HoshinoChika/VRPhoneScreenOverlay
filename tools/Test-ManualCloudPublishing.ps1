[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'ReleaseAutomation.Common.ps1')
. (Join-Path $PSScriptRoot 'CloudPublishing.Common.ps1')
$fixture = Join-Path $projectRoot ('artifacts\manual-cloud-script-tests\' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
$key = Join-Path $fixture 'synthetic-key.txt'
$record = Join-Path $fixture 'server.txt'
$manifest = Join-Path $fixture 'latest.json'
$metadata = Join-Path $fixture 'upload-metadata.json'
$package = Join-Path $fixture 'package.zip'
foreach ($file in @($key, $manifest, $metadata, $package)) { [IO.File]::WriteAllText($file, 'synthetic') }
[IO.File]::WriteAllText($record, '203.0.113.7')
$config = Join-Path $fixture 'private.json'
@{ SchemaVersion = 1; Client = @{}; Deployment = @{ UpdateSigningKeyPath = $key } } | ConvertTo-Json | Set-Content -LiteralPath $config
$local = Get-ReleaseConfiguration -ConfigurationPath $config -UpdateSigningKeyPath '' -SshKeyPath '' -ServerRecordPath '' -RequireRemote:$false
if ($local.UpdateSigningKeyPath -ne $key) { throw 'Offline preparation incorrectly requires remote configuration.' }
@{ SchemaVersion = 1; Client = @{}; Deployment = @{ SshKeyPath = $key; ServerRecordPath = $record; PublicBaseUri = 'https://service.invalid' } } |
    ConvertTo-Json | Set-Content -LiteralPath $config
$remote = Get-ReleaseConfiguration -ConfigurationPath $config -UpdateSigningKeyPath '' -SshKeyPath '' -ServerRecordPath '' -RequireSigningKey:$false
if ($null -ne $remote.UpdateSigningKeyPath) { throw 'Metadata publishing incorrectly requires a signing key.' }

$guardOutput = @(& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Publish-BetaRelease.ps1') -Upload 2>&1)
if ($LASTEXITCODE -eq 0 -or ($guardOutput -join ' ') -notmatch 'Prepare the package locally first') {
    throw 'Upload without a user-uploaded prepared package was accepted.'
}

$script:transferArguments = @()
$script:remoteCommands = @()
function Get-Command([string]$Name) {
    if ($Name -eq 'ssh') { return [PSCustomObject]@{ Source = 'Invoke-TestSsh' } }
    if ($Name -eq 'scp') { return [PSCustomObject]@{ Source = 'Invoke-TestScp' } }
    throw 'Unexpected executable in isolated metadata test.'
}
function Invoke-TestSsh {
    $command = [string]$args[-1]
    $script:remoteCommands += $command
    $global:LASTEXITCODE = if ($command -match "' prepare '") { 7 } else { 0 }
}
function Invoke-TestScp {
    $script:transferArguments = @($args)
    $global:LASTEXITCODE = 0
}
$configuration = [PSCustomObject]@{
    SshKeyPath = $key; ServerRecordPath = $record
    RemoteRoot = '/var/lib/vrphonescreen-service/updates/beta'; PublicBaseUri = 'https://service.invalid'
}
$rejected = $false
try {
    Publish-CloudRelease -Configuration $configuration -Version '1.0.0' -PackagePath $package `
        -ManifestPath $manifest -UploadMetadataPath $metadata -OutputDirectory $fixture -PackageHash ('a' * 64)
} catch { $rejected = $_.Exception.Message -match 'user-uploaded 115 package is missing' }
if (-not $rejected) { throw 'An absent or wrong user upload was not rejected.' }
if ($script:transferArguments -contains $package -or
    $script:transferArguments -notcontains $manifest -or $script:transferArguments -notcontains $metadata -or
    $script:transferArguments -notcontains (Join-Path $PSScriptRoot 'Cloud-Publish.py')) {
    throw 'SCP must transfer only the signed manifest, verification metadata and helper.'
}
if (($script:remoteCommands -join ' ') -match "' activate '") { throw 'A failed cloud verification changed the active release.' }
Write-Output 'Manual cloud publishing validation passed (offline prepare, metadata-only transfer and failure before activation).'
