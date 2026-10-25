namespace VRPhoneScreenOverlay.Contracts;

public sealed record DiagnosticLogHealth(
    string State, int Capacity, long Accepted, long Flushed, long Dropped,
    long Rejected, long Pending, long WriteFailures, DateTimeOffset? LastFlushAt,
    string? FailureType = null, int? FailureHResult = null)
{
    public DiagnosticLogErrorSummary[] ErrorSummaries { get; init; } = [];
    public long EvictedErrorSummaries { get; init; }
    public long SampledOut { get; init; }
    public long PriorityDropped { get; init; }
    public long RetentionFailures { get; init; }
    public static DiagnosticLogHealth Unavailable { get; } = new("not-collected", 0, 0, 0, 0, 0, 0, 0, null);
}

public sealed record DiagnosticLogErrorSummary(string EventName, string ReasonCode,
    long Count, DateTimeOffset FirstAt, DateTimeOffset LastAt);
