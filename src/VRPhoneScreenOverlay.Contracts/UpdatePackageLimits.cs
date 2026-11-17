namespace VRPhoneScreenOverlay.Contracts;

// The publisher reads these literal values too, so prepared packages obey the
// same expansion policy as the client. These do not replace signature/hash checks.
public static class UpdatePackageLimits
{
    public const int MaximumEntries = 4096;
    public const long MaximumEntryBytes = 536870912;
    public const long MaximumExpandedBytes = 2147483648;
    public const int MaximumCompressionRatio = 1000;
    public const int RatioCheckMinimumBytes = 1048576;
    public const int MaximumEntryPathCharacters = 1024;
    public const int ExtractionTimeoutSeconds = 300;
}
