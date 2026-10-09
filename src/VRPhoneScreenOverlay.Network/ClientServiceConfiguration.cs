using System.Text.Json;
using System.Text.Json.Serialization;

namespace VRPhoneScreenOverlay.Network;

/// <summary>Immutable client endpoints. Maintenance credentials never belong in this file.</summary>
public sealed record ClientServiceConfiguration
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    public Uri? UpdateManifestUri { get; init; }
    public Uri? DiagnosticsInitUri { get; init; }
    public Uri? UsageHeartbeatUri { get; init; }
    public Uri? ProjectRepositoryUri { get; init; }
    public string UpdateChannel { get; init; } = "beta";

    public static ClientServiceConfiguration Disabled { get; } = new();

    public static ClientServiceConfiguration LoadDefault() =>
        Load(Path.Combine(AppContext.BaseDirectory, "service.config.json")).Configuration;

    public static ClientServiceConfigurationResult Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            if (!File.Exists(path)) { return new(Disabled, "SERVICE_CONFIG_MISSING"); }
            using FileStream stream = File.OpenRead(path);
            if (stream.Length > 64 * 1024) { return new(Disabled, "SERVICE_CONFIG_INVALID"); }
            ServiceFile? file = JsonSerializer.Deserialize<ServiceFile>(stream, _jsonOptions);
            ClientServiceConfiguration? config = file?.Client;
            if (file?.SchemaVersion != 1 || config is null || config.UpdateChannel is not "beta" and not "stable" ||
                !ValidEndpoint(config.UpdateManifestUri) || !ValidEndpoint(config.DiagnosticsInitUri) ||
                !ValidEndpoint(config.UsageHeartbeatUri) || !ValidEndpoint(config.ProjectRepositoryUri))
            { return new(Disabled, "SERVICE_CONFIG_INVALID"); }
            return new(config, "SERVICE_CONFIG_LOADED");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        { return new(Disabled, "SERVICE_CONFIG_INVALID"); }
    }

    private static bool ValidEndpoint(Uri? uri) => uri is null || (uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps &&
        uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 && !HasCredentialQuery(uri));

    private static bool HasCredentialQuery(Uri uri) => uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => Uri.UnescapeDataString(part.Split('=', 2)[0]).ToLowerInvariant())
        .Any(key => key is "token" or "access_token" or "api_key" or "apikey" or "password" or "secret" or "authorization");

    private sealed record ServiceFile(int SchemaVersion, ClientServiceConfiguration? Client);
}

public sealed record ClientServiceConfigurationResult(ClientServiceConfiguration Configuration, string ReasonCode);
