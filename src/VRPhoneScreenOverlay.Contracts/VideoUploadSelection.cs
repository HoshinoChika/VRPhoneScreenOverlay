namespace VRPhoneScreenOverlay.Contracts;

public sealed record VideoUploadSelection(string SavedMode, string RequestedMode, string Source, bool IsValid)
{
    public static bool IsMode(string value) => value is "auto" or "staging" or "direct";
    public static VideoUploadSelection Resolve(string saved, string? environment)
    {
        string normalized = environment?.Trim().ToLowerInvariant() ?? string.Empty;
        return string.IsNullOrEmpty(environment)
            ? new(saved, saved, "settings", IsMode(saved))
            : new(saved, IsMode(normalized) ? normalized : "invalid", "environment", IsMode(normalized));
    }
}

public sealed record VideoUploadDiagnostic(string SavedMode, string RequestedMode, string Source,
    string ActualPath, string SelectionReason, int FallbackCount = 0, string? FailureStage = null, int? FailureHResult = null,
    uint VendorId = 0, uint DeviceId = 0, string? DriverVersion = null, string DriverVersionSource = "unavailable");
