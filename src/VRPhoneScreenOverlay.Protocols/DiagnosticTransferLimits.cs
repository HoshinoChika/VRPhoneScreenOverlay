namespace VRPhoneScreenOverlay.Protocols;

public static class DiagnosticTransferLimits
{
    // Capture export is bounded at 513 MiB; leave room for the accompanying logs.
    public const long MaximumBundleBytes = 544L * 1024 * 1024;
    public const long MaximumMemoryAttachmentBytes = 513L * 1024 * 1024;
    public const long MaximumExpandedBundleBytes = 768L * 1024 * 1024;
}
