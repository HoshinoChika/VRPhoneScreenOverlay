[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'ReleaseAutomation.Common.ps1')
Assert-ReleaseVersion -Version $Version
Assert-TrackedWorkingTreeClean -ProjectRoot $projectRoot
$branch = (& git -C $projectRoot branch --show-current | Out-String).Trim()
if (-not $branch.StartsWith('codex/', [StringComparison]::Ordinal)) {
    throw "Version preparation must run on a codex/* task branch; current branch is $branch."
}

$currentVersion = Get-ReleaseVersion -ProjectRoot $projectRoot
if ($currentVersion -eq $Version) {
    Write-Host "Release version is already $Version; no files changed." -ForegroundColor Green
    exit 0
}

$propsPath = Join-Path $projectRoot 'Directory.Build.props'
$propsText = [IO.File]::ReadAllText($propsPath)
$versionPattern = '(<VersionPrefix>)[^<]+(</VersionPrefix>)'
$matches = [regex]::Matches($propsText, $versionPattern)
if ($matches.Count -ne 1) {
    throw 'Directory.Build.props must contain exactly one VersionPrefix element.'
}

$updatedProps = [regex]::Replace(
    $propsText,
    $versionPattern,
    "`${1}$Version`${2}")
[IO.File]::WriteAllText($propsPath, $updatedProps, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText(
    (Join-Path $projectRoot 'VERSION'),
    $Version + "`n",
    [Text.UTF8Encoding]::new($false))

$localDotnet = Join-Path $projectRoot '.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) {
    $localDotnet
} elseif (Get-Command dotnet -ErrorAction SilentlyContinue) {
    (Get-Command dotnet -ErrorAction Stop).Source
} else {
    & (Join-Path $PSScriptRoot 'Install-DevelopmentSdk.ps1')
    $localDotnet
}
& $dotnet restore (Join-Path $projectRoot 'VRPhoneScreenOverlay.slnx') --force-evaluate
if ($LASTEXITCODE -ne 0) {
    throw "Dependency lock refresh failed with exit code $LASTEXITCODE."
}

$changedFiles = @(& git -C $projectRoot diff --name-only)
$unexpected = @($changedFiles | Where-Object {
        $_ -ne 'Directory.Build.props' -and
        $_ -ne 'VERSION' -and
        -not $_.EndsWith('/packages.lock.json', [StringComparison]::Ordinal)
    })
if ($unexpected.Count -ne 0) {
    throw "Version preparation changed unexpected files:`n$($unexpected -join "`n")"
}

Write-Host "Release version prepared: $currentVersion -> $Version" -ForegroundColor Green
Write-Host 'Only the central version inventory and generated dependency locks changed.'
