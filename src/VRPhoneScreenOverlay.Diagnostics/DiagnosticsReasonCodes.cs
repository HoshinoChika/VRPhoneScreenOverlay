namespace VRPhoneScreenOverlay.Diagnostics;

/// <summary>
/// Stable reason codes raised by this module. Values are protocol: they are only ever
/// added, never re-spelled, and never localised.
/// </summary>
public static class DiagnosticsReasonCodes
{
    /// <summary><c>DIAGNOSTICS_BUNDLE_SIZE_INVALID</c></summary>
    public const string BundleSizeInvalid = "DIAGNOSTICS_BUNDLE_SIZE_INVALID";

    /// <summary><c>DIAGNOSTICS_COMPLETE_INVALID</c></summary>
    public const string CompleteInvalid = "DIAGNOSTICS_COMPLETE_INVALID";

    /// <summary><c>DIAGNOSTICS_INIT_INVALID</c></summary>
    public const string InitInvalid = "DIAGNOSTICS_INIT_INVALID";

    /// <summary><c>DIAGNOSTICS_NOT_ACCEPTED</c></summary>
    public const string NotAccepted = "DIAGNOSTICS_NOT_ACCEPTED";

    /// <summary><c>DIAGNOSTICS_UPLOAD_POLICY_INVALID</c></summary>
    public const string UploadPolicyInvalid = "DIAGNOSTICS_UPLOAD_POLICY_INVALID";

    /// <summary><c>DIAGNOSTICS_UPLOADED</c></summary>
    public const string Uploaded = "DIAGNOSTICS_UPLOADED";
}
