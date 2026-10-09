[CmdletBinding()]
param([string]$ConfigurationPath = '')

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'ReleaseAutomation.Common.ps1')
if ([string]::IsNullOrWhiteSpace($ConfigurationPath)) {
    $ConfigurationPath = Join-Path $projectRoot 'service.private.json'
}
$configuration = Read-DeploymentConfiguration -Path $ConfigurationPath
$key = [string]$configuration.SshKeyPath
$record = [string]$configuration.ServerRecordPath
$address = Get-ServerAddressFromRecord -ServerRecordPath $record
if (-not $configuration.ContainsKey('UsageStatisticsUri')) { throw 'Configure UsageStatisticsUri before querying.' }
[Uri]$statsUri = [string]$configuration.UsageStatisticsUri
if ($statsUri.Host -ne '127.0.0.1' -or $statsUri.Scheme -ne 'http' -or $statsUri.UserInfo -or $statsUri.Fragment) { throw 'Statistics URI must target the private local listener.' }
# Only aggregates leave the server, never identities or credentials.
$script = @'
import datetime, json, pathlib, subprocess, urllib.request
pid = subprocess.check_output(['systemctl', 'show', 'vrphonescreen.service', '--property=MainPID', '--value'], text=True).strip()
if not pid.isdigit() or pid == '0':
    raise SystemExit('Service is not running')
env = dict(item.split('=', 1) for item in pathlib.Path('/proc/' + pid + '/environ').read_text().split('\0') if '=' in item)
token = env.get('USAGE_STATS_TOKEN', '')
if len(token) >= 32:
    request = urllib.request.Request(__STATS_URI__, headers={'Authorization': 'Bearer ' + token})
    with urllib.request.build_opener(urllib.request.ProxyHandler({})).open(request, timeout=5) as response:
        print(response.read(4096).decode())
else:
    path = pathlib.Path(env.get('DATA_ROOT', '/data')) / 'usage' / 'installations.json'
    now = datetime.datetime.now(datetime.timezone.utc)
    if not path.exists():
        raise SystemExit('No heartbeat snapshot yet; wait at least 60 seconds after a client connects')
    users = json.loads(path.read_text())
    seen = [datetime.datetime.fromisoformat(item['LastSeen'].replace('Z', '+00:00')) for item in users]
    count = lambda seconds: sum(t > now - datetime.timedelta(seconds=seconds) for t in seen)
    print(json.dumps(dict(totalInstallations=len(users), active24Hours=count(86400), active7Days=count(604800), active30Days=count(2592000), online=count(180), asOf=now.isoformat(), onlineTimeoutSeconds=180, source='private-snapshot', snapshotAgeSeconds=max(0, int(now.timestamp()-path.stat().st_mtime)))))
'@
$script = $script.Replace('__STATS_URI__', ($statsUri.AbsoluteUri | ConvertTo-Json -Compress))
$encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($script))
$remote = "python3 -c `"import base64; exec(base64.b64decode('$encoded'))`""
& ssh -i $key -o BatchMode=yes -o ConnectTimeout=15 -o StrictHostKeyChecking=yes "root@$address" $remote
if ($LASTEXITCODE -ne 0) { throw 'Private usage statistics query failed.' }
