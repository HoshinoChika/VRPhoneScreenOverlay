namespace VRPhoneScreenOverlay.Contracts;

public static class TranslationHistoryLimits
{
    public const int MaximumRecordBytes = 131072;
    public const long MaximumDirectoryBytes = 134217728;
    public const int MaximumRecords = TranslationPreferences.MaximumHistoryLimit;

    public static bool IsRecordName(string name) => name.Length == 37 &&
        name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && Guid.TryParseExact(name.AsSpan(0, 32), "N", out _);

    public static bool IsTemporaryName(string name) =>
        (name.Length == 41 && IsRecordName(name[..37]) && name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) ||
        (name.Length == 74 && IsRecordName(name[..37]) && name[37] == '.' &&
            Guid.TryParseExact(name.AsSpan(38, 32), "N", out _) && name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
}
