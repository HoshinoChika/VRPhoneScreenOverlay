namespace VRPhoneScreenOverlay.Service;

public sealed record CloudStorageOptions(Uri? Endpoint, string? TokenFile, string MountPath)
{
    public bool Enabled => Endpoint is not null && !string.IsNullOrEmpty(TokenFile);
    public static CloudStorageOptions FromEnvironment() => new(
        Uri.TryCreate(Environment.GetEnvironmentVariable("VRPSO_OPENLIST_URL"), UriKind.Absolute, out Uri? endpoint) ? endpoint : null,
        Environment.GetEnvironmentVariable("VRPSO_OPENLIST_TOKEN_FILE"),
        Environment.GetEnvironmentVariable("VRPSO_OPENLIST_MOUNT") ?? "/vrpso");
}

public sealed class CloudTransferException(string reason) : Exception(reason)
{
    public string ReasonCode { get; } = reason;
}
