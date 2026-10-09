[CmdletBinding()]
param([switch]$Upload)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'ReleaseAutomation.Common.ps1')
$scriptPath = Join-Path $projectRoot 'website\hoshinochika\vrphone-download.js'
$helper = Join-Path $PSScriptRoot 'Deploy-WebsiteDownload.py'
$hash = (Get-FileHash -LiteralPath $scriptPath -Algorithm SHA256).Hash.ToLowerInvariant()
$version = Get-ReleaseVersion -ProjectRoot $projectRoot
if (-not $Upload) { Write-Output "Website download script ready: $version"; return }
Assert-TrackedWorkingTreeClean -ProjectRoot $projectRoot
$configuration = Get-ReleaseConfiguration -ConfigurationPath (Join-Path $projectRoot 'service.private.json') `
    -UpdateSigningKeyPath '' -SshKeyPath '' -ServerRecordPath '' -RequireSigningKey:$false
$address = Get-ServerAddressFromRecord -ServerRecordPath $configuration.ServerRecordPath
$gitRoot = Split-Path -Parent (Split-Path -Parent (Get-Command git).Source)
$ssh = Join-Path $gitRoot 'usr\bin\ssh.exe'
$scp = Join-Path $gitRoot 'usr\bin\scp.exe'
$options = @('-i', $configuration.SshKeyPath, '-o', 'BatchMode=yes', '-o', 'ConnectTimeout=15',
    '-o', 'ProxyCommand=none', '-o', 'StrictHostKeyChecking=accept-new')
$target = "root@$address"
$stage = '/opt/photo-wall/.vrphone-download-' + [Guid]::NewGuid().ToString('N')
& $ssh @options $target "install -d -m 0700 '$stage'"
if ($LASTEXITCODE -ne 0) { throw 'Website download preflight failed.' }
& $scp @options -- $scriptPath $helper "${target}:$stage/"
if ($LASTEXITCODE -ne 0) { throw 'Website download script transfer failed.' }
& $ssh @options $target "python3 '$stage/Deploy-WebsiteDownload.py' install '$stage' '$version' '$hash'"
if ($LASTEXITCODE -ne 0) { throw 'Website download link deployment failed.' }
try {
    $site = Invoke-WebRequest -Uri $configuration.PublicBaseUri -NoProxy -TimeoutSec 15
    if ($site.Content -notmatch 'data-vrphone-download' -or $site.Content -notmatch '/photo-assets/vrphone-download.js') {
        throw 'Public website did not show the new download link.'
    }
    $served = Invoke-WebRequest -Uri ($configuration.PublicBaseUri + '/photo-assets/vrphone-download.js?v=' + $hash.Substring(0,12)) -NoProxy -TimeoutSec 15
    $servedHash = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($served.Content)))
    if ($servedHash -ne $hash) { throw 'Public download script hash differs.' }
} catch {
    & $ssh @options $target "python3 '$stage/Deploy-WebsiteDownload.py' rollback '$stage' '$version' '$hash'"
    throw
}
Write-Output 'Public website download link verified.'
