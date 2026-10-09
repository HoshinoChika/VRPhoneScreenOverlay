namespace VRPhoneScreenOverlay.Update;

/// <summary>
/// Stable reason codes raised by this module. Values are protocol: they are only ever
/// added, never re-spelled, and never localised.
/// </summary>
public static class UpdateReasonCodes
{
    public const string NotChecked = "UPDATE_NOT_CHECKED";
    public const string Checking = "UPDATE_CHECKING";
    public const string CheckFailed = "UPDATE_CHECK_FAILED";
    public const string Downloading = "UPDATE_DOWNLOADING";
    public const string Restarting = "UPDATE_RESTARTING";
    public const string InstallFailed = "UPDATE_INSTALL_FAILED";

    /// <summary><c>UPDATE_ALREADY_CURRENT</c></summary>
    public const string AlreadyCurrent = "UPDATE_ALREADY_CURRENT";

    /// <summary><c>UPDATE_AVAILABLE</c></summary>
    public const string Available = "UPDATE_AVAILABLE";

    /// <summary><c>UPDATE_INSTALL_PATH_INVALID</c></summary>
    public const string InstallPathInvalid = "UPDATE_INSTALL_PATH_INVALID";

    /// <summary><c>UPDATE_MAINTENANCE_MISSING</c></summary>
    public const string MaintenanceMissing = "UPDATE_MAINTENANCE_MISSING";

    /// <summary><c>UPDATE_MAINTENANCE_START_FAILED</c></summary>
    public const string MaintenanceStartFailed = "UPDATE_MAINTENANCE_START_FAILED";

    /// <summary><c>UPDATE_MANIFEST_INVALID</c></summary>
    public const string ManifestInvalid = "UPDATE_MANIFEST_INVALID";

    /// <summary><c>UPDATE_MANIFEST_PAYLOAD_INVALID</c></summary>
    public const string ManifestPayloadInvalid = "UPDATE_MANIFEST_PAYLOAD_INVALID";

    /// <summary><c>UPDATE_MANIFEST_SIGNATURE_INVALID</c></summary>
    public const string ManifestSignatureInvalid = "UPDATE_MANIFEST_SIGNATURE_INVALID";

    /// <summary><c>UPDATE_NOT_PUBLISHED</c></summary>
    public const string NotPublished = "UPDATE_NOT_PUBLISHED";

    /// <summary><c>UPDATE_PACKAGE_HASH_MISMATCH</c></summary>
    public const string PackageHashMismatch = "UPDATE_PACKAGE_HASH_MISMATCH";

    /// <summary><c>UPDATE_PACKAGE_PATH_INVALID</c></summary>
    public const string PackagePathInvalid = "UPDATE_PACKAGE_PATH_INVALID";

    /// <summary><c>UPDATE_PACKAGE_LAYOUT_INVALID</c></summary>
    public const string PackageLayoutInvalid = "UPDATE_PACKAGE_LAYOUT_INVALID";

    /// <summary><c>UPDATE_PACKAGE_SIZE_INVALID</c></summary>
    public const string PackageSizeInvalid = "UPDATE_PACKAGE_SIZE_INVALID";

    /// <summary><c>UPDATE_PACKAGE_TOO_LARGE</c></summary>
    public const string PackageTooLarge = "UPDATE_PACKAGE_TOO_LARGE";

    /// <summary><c>UPDATE_PACKAGE_TRUNCATED</c></summary>
    public const string PackageTruncated = "UPDATE_PACKAGE_TRUNCATED";

    /// <summary><c>UPDATE_VERSION_INVALID</c></summary>
    public const string VersionInvalid = "UPDATE_VERSION_INVALID";
}
