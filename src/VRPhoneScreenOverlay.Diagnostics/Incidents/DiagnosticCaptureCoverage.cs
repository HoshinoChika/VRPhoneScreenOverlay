namespace VRPhoneScreenOverlay.Diagnostics;

public sealed record CaptureRunCoverage(string CaptureId, DateTimeOffset RecordedAt, int Attempts, int Saved, int Skipped)
{
    public string LastStatus { get; init; } = "observing";
    public string LastTrigger { get; init; } = "none";
    public int? FailureHResult { get; init; }
}
public sealed record DiagnosticCaptureCoverage(string ObserverStatus, int RecoveryAttempts, CaptureRunCoverage[] Runs)
{
    public string CollectionStatus { get; init; } = "collected";
    public int UnavailableRuns { get; init; }
}
