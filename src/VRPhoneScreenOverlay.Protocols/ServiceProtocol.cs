namespace VRPhoneScreenOverlay.Protocols;

public sealed record SignedUpdateManifest(
    string Payload,
    string Signature,
    UpdateDownloadLease? Download = null);

// The lease is transport metadata. Only the signed payload authorizes version/bytes.
public sealed record UpdateDownloadLease(string Version, string Url, DateTimeOffset ExpiresAt);

public sealed record UpdateReleasePayload(
    string Channel,
    string Version,
    DateTimeOffset PublishedAt,
    string PackageUrl,
    long PackageSize,
    string PackageSha256,
    string ReleaseNotes)
{
    public int SchemaVersion { get; init; }
    public string PackageFormat { get; init; } = string.Empty;
}

public static class UpdatePackageFormat
{
    public const int SchemaVersion = 1;
    public const string FullInstall = "full-install-v1";
}

public sealed record DiagnosticUploadInitRequest(
    string AppVersion,
    long BundleSize,
    string BundleSha256,
    DateTimeOffset CreatedAt);

public sealed record DiagnosticUploadInitResponse(
    string UploadId,
    string UploadUrl,
    string UploadToken,
    long MaximumBytes,
    DateTimeOffset ExpiresAt);

public sealed record DiagnosticUploadCompleteRequest(
    string UploadId,
    string BundleSha256);

public sealed record DiagnosticUploadCompleteResponse(
    bool Accepted,
    DateTimeOffset ReceivedAt);
