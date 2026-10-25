namespace VRPhoneScreenOverlay.Contracts;

public sealed record BindingSnapshotRetentionDiagnostic(DateTimeOffset CapturedAt, string Reason,
    int ProtectedDirectories, int ScannedDirectories, int UnreferencedKept, int DeletedDirectories,
    int SkippedDirectories, long InspectedBytes, bool ScanTruncated, string? FailureType = null)
{
    public string? Operation { get; init; }
}
