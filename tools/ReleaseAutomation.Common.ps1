Set-StrictMode -Version Latest

function Get-DownloadTestVersion {
    param([Parameter(Mandatory = $true)][string]$SourceVersion,
        [ValidateRange(1, 2147483647)][int]$Number = 1)
    Assert-ReleaseVersion -Version $SourceVersion
    if ($SourceVersion.Contains('-')) { return "$SourceVersion.download-test.$Number" }
    $parts = $SourceVersion.Split('.')
    $nextPatch = [long]::Parse($parts[2]) + 1
    return "$($parts[0]).$($parts[1]).$nextPatch-download-test.$Number"
}

function Assert-DownloadTestVersion {
    param([Parameter(Mandatory = $true)][string]$SourceVersion,
        [Parameter(Mandatory = $true)][string]$Version)
    $suffix = [regex]::Match($Version, '(?:[.-])download-test\.(?<number>[1-9][0-9]*)$')
    [int]$number = 0
    if (-not $suffix.Success -or -not [int]::TryParse($suffix.Groups['number'].Value, [ref]$number) -or
        $Version -cne (Get-DownloadTestVersion -SourceVersion $SourceVersion -Number $number)) {
        throw 'Download test identifier does not match the unchanged package version.'
    }
}

function Assert-EquivalentClientConfiguration {
    param([Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$InstalledPath)
    $canonical = @()
    foreach ($path in @($SourcePath, $InstalledPath)) {
        $data = Get-Content -LiteralPath $path -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
        if ($data.SchemaVersion -ne 1 -or $data.Count -ne 2 -or
            @($data.Keys | Where-Object { $_ -cnotin @('SchemaVersion', 'Client') }).Count -ne 0 -or
            $data.Client -isnot [Collections.IDictionary]) { throw 'Client configuration schema is invalid.' }
        $client = [ordered]@{}
        foreach ($key in @($data.Client.Keys | Sort-Object)) {
            if ($key -cnotin @('UpdateManifestUri', 'DiagnosticsInitUri', 'UsageHeartbeatUri', 'ProjectRepositoryUri', 'UpdateChannel')) {
                throw 'Client configuration contains unsupported fields.'
            }
            $client[$key] = $data.Client[$key]
        }
        $canonical += ([ordered]@{ SchemaVersion = $data.SchemaVersion; Client = $client } | ConvertTo-Json -Depth 4 -Compress)
    }
    if ($canonical[0] -cne $canonical[1]) { throw 'Client configuration values differ from the verified build.' }
}

function Assert-ExistingDownloadTestBuild {
    param(
        [Parameter(Mandatory = $true)][string]$AppDirectory,
        [Parameter(Mandatory = $true)][string]$InstallDirectory
    )
    $sourceRoot = [IO.Path]::GetFullPath($AppDirectory).TrimEnd('\', '/')
    $installedRoot = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\', '/')
    $sourceHost = Join-Path $sourceRoot 'layout\VRPhoneScreenOverlay.exe'
    $installedHost = Join-Path $installedRoot 'VRPhoneScreenOverlay.exe'
    if ((Get-FileHash -LiteralPath $sourceHost).Hash -ne (Get-FileHash -LiteralPath $installedHost).Hash) {
        throw 'Download test package launcher differs from the installed application.'
    }
    $sourceVersion = (Get-Item -LiteralPath (Join-Path $sourceRoot 'VRPhoneScreenOverlay.exe')).VersionInfo.ProductVersion
    if ($sourceVersion -ne (Get-Item -LiteralPath $installedHost).VersionInfo.ProductVersion) {
        throw 'Download test source and installation must have the same ProductVersion.'
    }
    foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -File -Recurse) {
        $relative = $file.FullName.Substring($sourceRoot.Length + 1)
        # Prepare-ReleaseFolder replaces the flat runtime host and rewrites this
        # manifest's launcher path; debug and XML documentation are not installed.
        if ($relative -in 'VRPhoneScreenOverlay.exe', 'manifest.vrmanifest' -or $file.Extension -in '.pdb', '.xml') { continue }
        $installed = Join-Path (Join-Path $installedRoot 'app') $relative
        if ($relative -ceq 'service.config.json') {
            Assert-EquivalentClientConfiguration -SourcePath $file.FullName -InstalledPath $installed
            continue
        }
        if (-not (Test-Path -LiteralPath $installed -PathType Leaf) -or
            (Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $installed).Hash) {
            throw "Download test package differs from the installed application: $relative"
        }
    }
}

. (Join-Path $PSScriptRoot 'ServiceConfiguration.Common.ps1')

function Assert-PortablePackageLayout {
    param([Parameter(Mandatory = $true)][string]$PackagePath)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $files = @($archive.Entries | Where-Object { $_.Name -ne '' } |
            ForEach-Object { $_.FullName.Replace('\', '/') })
        $prefix = 'VRPhoneScreenOverlay/'
        if (@($files | Where-Object { -not $_.StartsWith($prefix, [StringComparison]::Ordinal) }).Count -ne 0) {
            throw 'Portable ZIP must contain a single VRPhoneScreenOverlay installation folder.'
        }
        $files = @($files | ForEach-Object { $_.Substring($prefix.Length) })
        foreach ($required in @(
                'VRPhoneScreenOverlay.exe',
                'app/VRPhoneScreenOverlay.dll',
                'app/VRPhoneScreenOverlay.Maintenance.exe',
                'app/VRPhoneScreenOverlay.SteamVR.BindingTool.exe',
                'app/manifest.vrmanifest',
                'app/action_manifest.json',
                'app/resources/android-platform-tools/adb.exe',
                'app/resources/scrcpy/scrcpy-server-v4.1')) {
            if ($required -cnotin $files) {
                throw "Portable package is missing $required."
            }
        }
        $unexpected = @($files | Where-Object {
            $_ -cne 'VRPhoneScreenOverlay.exe' -and -not $_.StartsWith('app/', [StringComparison]::Ordinal)
        })
        if ($unexpected.Count -ne 0) {
            throw 'Portable installation folder must contain only the main executable and app/.'
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Get-ReleaseVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectRoot
    )

    $versionPath = Join-Path $ProjectRoot 'VERSION'
    $propsPath = Join-Path $ProjectRoot 'Directory.Build.props'
    $version = (Get-Content -LiteralPath $versionPath -Raw -Encoding utf8).Trim()
    $props = [xml](Get-Content -LiteralPath $propsPath -Raw -Encoding utf8)
    $versionGroups = @($props.Project.PropertyGroup | Where-Object {
            $null -ne $_.SelectSingleNode('VersionPrefix')
        })
    if ($versionGroups.Count -ne 1) {
        throw 'Directory.Build.props must contain exactly one VersionPrefix element.'
    }

    $buildVersion = ([string]$versionGroups[0].VersionPrefix).Trim()
    if ($version -ne $buildVersion) {
        throw "VERSION ($version) does not match VersionPrefix ($buildVersion)."
    }

    Assert-ReleaseVersion -Version $version
    return $version
}

function Assert-ReleaseVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$') {
        throw "Invalid release version: $Version"
    }
}

function Assert-TrackedWorkingTreeClean {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectRoot
    )

    $status = @(& git -C $ProjectRoot status --porcelain=v1 --untracked-files=no)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect the Git working tree.'
    }

    if ($status.Count -ne 0) {
        throw "Tracked working tree must be clean before publishing:`n$($status -join "`n")"
    }
}

function Assert-ReleaseSourceUnchanged {
    param([string]$ProjectRoot, [string]$SourceCommit)
    if ($SourceCommit -notmatch '^[0-9a-f]{40}$') { throw 'Invalid release source commit.' }
    & git -C $ProjectRoot merge-base --is-ancestor $SourceCommit HEAD
    if ($LASTEXITCODE -ne 0) { throw 'The existing release must come from an ancestor of the current branch.' }
    & git -C $ProjectRoot diff --quiet $SourceCommit HEAD -- src bindings third_party `
        Directory.Build.props Directory.Packages.props global.json VERSION action_manifest.json .editorconfig
    if ($LASTEXITCODE -ne 0) { throw 'Production source changed; rebuild rather than resume the existing package.' }
}

function Get-ReleaseCommit {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectRoot
    )

    $commit = (& git -C $ProjectRoot rev-parse HEAD | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') {
        throw 'Unable to read the release Git commit.'
    }

    return $commit
}

function Get-ReleaseConfiguration {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ConfigurationPath,

        [AllowEmptyString()]
        [string]$UpdateSigningKeyPath,

        [AllowEmptyString()]
        [string]$SshKeyPath,

        [AllowEmptyString()]
        [string]$ServerRecordPath,

        [bool]$RequireRemote = $true,

        [bool]$RequireSigningKey = $true
    )

    $configuration = @{}
    if (Test-Path -LiteralPath $ConfigurationPath -PathType Leaf) {
        $configuration = Read-DeploymentConfiguration -Path $ConfigurationPath
    }

    $signingKey = if ($RequireSigningKey) { Select-ReleaseSetting `
        -Override $UpdateSigningKeyPath `
        -Configuration $configuration `
        -Name 'UpdateSigningKeyPath' } else { '' }
    if ($RequireSigningKey -and -not (Test-Path -LiteralPath $signingKey -PathType Leaf)) {
        throw 'The configured signing key is missing.'
    }
    if (-not $RequireRemote) {
        return [PSCustomObject]@{ UpdateSigningKeyPath = [IO.Path]::GetFullPath($signingKey) }
    }
    $sshKey = Select-ReleaseSetting `
        -Override $SshKeyPath `
        -Configuration $configuration `
        -Name 'SshKeyPath'
    $serverRecord = Select-ReleaseSetting `
        -Override $ServerRecordPath `
        -Configuration $configuration `
        -Name 'ServerRecordPath'
    foreach ($requiredPath in @($sshKey, $serverRecord)) {
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "Release configuration file does not exist: $requiredPath"
        }
    }

    $remoteRoot = if ($configuration.ContainsKey('RemoteRoot')) {
        [string]$configuration.RemoteRoot
    } else {
        '/var/lib/vrphonescreen-service/updates/beta'
    }
    Assert-SafeRemoteRoot -RemoteRoot $remoteRoot
    $publicBaseUri = if ($configuration.ContainsKey('PublicBaseUri')) {
        [string]$configuration.PublicBaseUri
    } else {
        ''
    }
    $baseUri = [Uri]$publicBaseUri
    if (-not $baseUri.IsAbsoluteUri -or $baseUri.Scheme -ne 'https') {
        throw 'PublicBaseUri must be an absolute HTTPS URI.'
    }

    $serviceName = if ($configuration.ContainsKey('ServiceName')) {
        [string]$configuration.ServiceName
    } else {
        'vrphonescreen.service'
    }
    if ($serviceName -notmatch '^[A-Za-z0-9_.@-]+\.service$') {
        throw 'ServiceName is invalid.'
    }

    return [PSCustomObject]@{
        UpdateSigningKeyPath = if ($RequireSigningKey) { [IO.Path]::GetFullPath($signingKey) } else { $null }
        SshKeyPath = [IO.Path]::GetFullPath($sshKey)
        ServerRecordPath = [IO.Path]::GetFullPath($serverRecord)
        RemoteRoot = $remoteRoot.TrimEnd('/')
        PublicBaseUri = $baseUri.AbsoluteUri.TrimEnd('/')
        ServiceName = $serviceName
    }
}

function Get-ServerAddressFromRecord {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ServerRecordPath
    )

    $content = Get-Content -LiteralPath $ServerRecordPath -Raw
    foreach ($match in [regex]::Matches(
        $content,
        '(?<!\d)(?:\d{1,3}\.){3}\d{1,3}(?!\d)')) {
        [System.Net.IPAddress]$address = $null
        if ([System.Net.IPAddress]::TryParse($match.Value, [ref]$address) -and
            $address.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetwork) {
            return $match.Value
        }
    }

    throw 'No IPv4 server address was found in the server record.'
}

function Assert-SafeRemoteRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RemoteRoot
    )

    if ($RemoteRoot -notmatch '^/[A-Za-z0-9._/-]+$' -or
        $RemoteRoot -match '(^|/)\.\.(/|$)' -or
        $RemoteRoot -eq '/') {
        throw "Unsafe remote release root: $RemoteRoot"
    }
}

function Compare-ReleaseVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Left,

        [Parameter(Mandatory = $true)]
        [string]$Right
    )

    Assert-ReleaseVersion -Version $Left
    Assert-ReleaseVersion -Version $Right
    $leftMatch = [regex]::Match(
        $Left,
        '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<pre>.+))?$')
    $rightMatch = [regex]::Match(
        $Right,
        '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<pre>.+))?$')
    foreach ($name in @('major', 'minor', 'patch')) {
        $compared = [long]::Parse($leftMatch.Groups[$name].Value).CompareTo(
            [long]::Parse($rightMatch.Groups[$name].Value))
        if ($compared -ne 0) {
            return $compared
        }
    }

    $leftPre = $leftMatch.Groups['pre'].Value
    $rightPre = $rightMatch.Groups['pre'].Value
    if ([string]::IsNullOrEmpty($leftPre)) {
        if ([string]::IsNullOrEmpty($rightPre)) {
            return 0
        }

        return 1
    }

    if ([string]::IsNullOrEmpty($rightPre)) {
        return -1
    }

    $leftParts = $leftPre.Split('.')
    $rightParts = $rightPre.Split('.')
    for ($index = 0; $index -lt [Math]::Max($leftParts.Length, $rightParts.Length); $index++) {
        if ($index -ge $leftParts.Length) {
            return -1
        }

        if ($index -ge $rightParts.Length) {
            return 1
        }

        [long]$leftNumber = 0
        [long]$rightNumber = 0
        $leftNumeric = [long]::TryParse($leftParts[$index], [ref]$leftNumber)
        $rightNumeric = [long]::TryParse($rightParts[$index], [ref]$rightNumber)
        $compared = if ($leftNumeric -and $rightNumeric) {
            $leftNumber.CompareTo($rightNumber)
        } elseif ($leftNumeric) {
            -1
        } elseif ($rightNumeric) {
            1
        } else {
            [string]::Compare(
                $leftParts[$index],
                $rightParts[$index],
                [StringComparison]::Ordinal)
        }
        if ($compared -ne 0) {
            return $compared
        }
    }

    return 0
}

function Read-VerifiedManifestPayload {
    param([Parameter(Mandatory = $true)][string]$ManifestPath,
        [Parameter(Mandatory = $true)][string]$PublicKeyPath)
    $manifest = Get-Content -LiteralPath $ManifestPath -Raw -Encoding utf8 | ConvertFrom-Json
    $payloadBytes = [Convert]::FromBase64String([string]$manifest.payload)
    $signature = [Convert]::FromBase64String([string]$manifest.signature)
    $ecdsa = [Security.Cryptography.ECDsa]::Create()
    try {
        $ecdsa.ImportFromPem([IO.File]::ReadAllText($PublicKeyPath))
        if (-not $ecdsa.VerifyData($payloadBytes, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256)) {
            throw 'Signed update manifest verification failed.'
        }
    } finally { $ecdsa.Dispose() }
    return [Text.Encoding]::UTF8.GetString($payloadBytes) | ConvertFrom-Json
}

function Read-VerifiedSignedManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ManifestPath,

        [Parameter(Mandatory = $true)]
        [string]$PackagePath,

        [Parameter(Mandatory = $true)]
        [string]$PublicKeyPath,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedVersion
    )

    if ($PSVersionTable.PSVersion.Major -lt 7) {
        throw 'Signed manifest verification requires PowerShell 7 or later.'
    }

    $payload = Read-VerifiedManifestPayload -ManifestPath $ManifestPath -PublicKeyPath $PublicKeyPath
    $package = Get-Item -LiteralPath $PackagePath
    $hash = (Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ([int]$payload.schemaVersion -ne 1 -or [string]$payload.packageFormat -ne 'full-install-v1' -or
        [string]$payload.channel -ne 'beta' -or
        [string]$payload.version -ne $ExpectedVersion -or
        [long]$payload.packageSize -ne $package.Length -or
        [string]$payload.packageSha256 -ne $hash) {
        throw 'Signed manifest does not match the release package.'
    }

    return [PSCustomObject]@{
        Payload = $payload
        PackageBytes = $package.Length
        PackageSha256 = $hash
    }
}

function Select-ReleaseSetting {
    param(
        [AllowEmptyString()]
        [string]$Override,

        [Parameter(Mandatory = $true)]
        [hashtable]$Configuration,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $value = if (-not [string]::IsNullOrWhiteSpace($Override)) {
        $Override
    } elseif ($Configuration.ContainsKey($Name)) {
        [string]$Configuration[$Name]
    } else {
        string.Empty
    }
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Missing release setting: $Name"
    }

    return $value
}
