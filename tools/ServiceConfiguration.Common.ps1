Set-StrictMode -Version Latest

function Read-PrivateServiceConfiguration {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw 'Private service configuration is missing; copy service.config.example.json and configure it locally.'
    }
    try { $value = Get-Content -LiteralPath $Path -Raw -Encoding utf8 | ConvertFrom-Json }
    catch { throw 'Private service configuration is invalid JSON.' }
    if ($value.SchemaVersion -ne 1 -or $null -eq $value.Client -or $null -eq $value.Deployment) {
        throw 'Private service configuration has an unsupported schema.'
    }
    return $value
}

function Read-DeploymentConfiguration {
    param([Parameter(Mandatory = $true)][string]$Path)
    # Explicit legacy files remain supported without becoming the default again.
    if ([IO.Path]::GetExtension($Path) -eq '.psd1') { return Import-PowerShellDataFile -LiteralPath $Path }
    $value = Read-PrivateServiceConfiguration -Path $Path
    $settings = @{}
    foreach ($property in $value.Deployment.PSObject.Properties) { $settings[$property.Name] = $property.Value }
    return $settings
}

function Write-ClientServiceConfiguration {
    param([Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][string]$OutputDirectory,
        [string]$ConfigurationPath = '')
    $privatePath = if ($ConfigurationPath -and [IO.Path]::GetExtension($ConfigurationPath) -ne '.psd1') { $ConfigurationPath } else { Join-Path $ProjectRoot 'service.private.json' }
    $templatePath = Join-Path $ProjectRoot 'service.config.example.json'
    if ($ConfigurationPath -and [IO.Path]::GetExtension($ConfigurationPath) -ne '.psd1' -and
        -not (Test-Path -LiteralPath $privatePath)) { throw 'The explicitly requested client configuration is missing.' }
    $value = Read-PrivateServiceConfiguration -Path $(if (Test-Path -LiteralPath $privatePath) { $privatePath } else { $templatePath })
    $allowed = @('UpdateManifestUri', 'DiagnosticsInitUri', 'UsageHeartbeatUri', 'ProjectRepositoryUri', 'UpdateChannel')
    $client = [ordered]@{}
    foreach ($property in $value.Client.PSObject.Properties) {
        if ($property.Name -cnotin $allowed) { throw 'Client configuration contains an unsupported field; maintenance credentials cannot be distributed.' }
        if ($property.Name -eq 'UpdateChannel') {
            if ($property.Value -cnotin @('beta', 'stable')) { throw 'Client update channel is invalid.' }
        } elseif ($null -ne $property.Value) {
            [Uri]$uri = $null
            if (-not [Uri]::TryCreate([string]$property.Value, [UriKind]::Absolute, [ref]$uri) -or
                $uri.Scheme -ne 'https' -or $uri.UserInfo -or $uri.Fragment) { throw 'Client endpoint must use HTTPS without embedded credentials.' }
            foreach ($part in $uri.Query.TrimStart('?').Split('&', [StringSplitOptions]::RemoveEmptyEntries)) {
                $queryKey = [Uri]::UnescapeDataString(($part -split '=', 2)[0]).ToLowerInvariant()
                if ($queryKey -in @('token', 'access_token', 'api_key', 'apikey', 'password', 'secret', 'authorization')) {
                    throw 'Client endpoint must not embed maintenance credentials in query parameters.'
                }
            }
        }
        $client[$property.Name] = $property.Value
    }
    if (-not $client.Contains('UpdateChannel')) { $client['UpdateChannel'] = 'beta' }
    $destination = Join-Path $OutputDirectory 'service.config.json'
    $json = @{ SchemaVersion = 1; Client = $client } | ConvertTo-Json -Depth 4
    [IO.File]::WriteAllText($destination + '.new', $json, [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath ($destination + '.new') -Destination $destination -Force
}
