using System.Text.Json.Serialization;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Diagnostics;

public sealed record IncidentEntry(long Sequence, DateTimeOffset RecordedAt, IncidentCheckpoint Checkpoint);
public sealed record IncidentTimelineEntry(DateTimeOffset Timestamp, string Kind, IncidentCheckpoint? Checkpoint = null, IncidentFailure? Failure = null);

public sealed record IncidentState(
    int SchemaVersion, string SessionId, int ProcessId, long ProcessStartTicks,
    string Build, DateTimeOffset StartedAt, bool CleanShutdown, string MonitorStatus,
    long DiscardedCheckpoints, IncidentEntry[] Checkpoints)
{
    public IncidentEnvironment? Environment { get; init; }
    public long StorageFailures { get; init; }
    public DiagnosticRuntimeHealth? HealthAtCheckpoint { get; init; }
}

public sealed record IncidentDisplayDriver(string? Description, string? Version, string? Provider);
public sealed record IncidentEnvironment(string OperatingSystem, string OsArchitecture,
    string ProcessArchitecture, string Framework, int LogicalProcessors,
    string DisplayDriverStatus, bool DriversTruncated, IncidentDisplayDriver[] DisplayDrivers);

public sealed record IncidentFailure(
    DateTimeOffset RecordedAt, string Kind, string ExceptionType, int HResult,
    int? NativeErrorCode, string[] Frames)
{
    public string? AttemptId { get; init; }
    public IncidentExceptionCause[] Causes { get; init; } = [];
    public bool CausesTruncated { get; init; }
}

public sealed record IncidentExceptionCause(int Index, int? ParentIndex, string ExceptionType,
    int HResult, int? NativeErrorCode, string[] Frames, bool FramesTruncated)
{
    public VideoGeometryDiagnostic? Geometry { get; init; }
}

public sealed record IncidentFailureHistory(long OmittedFailures, IncidentFailure[] Failures);

public sealed record DiagnosticRuntimeHealth(DateTimeOffset CapturedAt, string IncidentStorageStatus,
    string HealthPersistenceStatus, DiagnosticLogHealth Log)
{
    public string? SessionId { get; init; }
    public long IncidentStorageFailures { get; init; }
    public DiagnosticHardwareObservation? Hardware { get; init; }
    public string HardwareCollectionStatus { get; init; } = "not-collected";
}

public sealed record DiagnosticHardwareObservation(DateTimeOffset CapturedAt, string Status,
    bool PhoneIsCurrent, bool VideoIsCurrent, DiagnosticHardwareSummary Snapshot);

public sealed record IncidentExit(
    DateTimeOffset ObservedAt, int? ExitCode, string Status, bool CleanShutdown);

public sealed record IncidentObserverHealth(DateTimeOffset SampledAt, int ProcessId, long ProcessStartTicks);

public sealed record IncidentReport(string SessionId, string CollectionStatus,
    IncidentState? State, IncidentFailure? Failure, IncidentExit? Exit,
    IncidentCrashEvidence? CrashEvidence = null,
    IncidentFailureHistory? FailureHistory = null,
    DiagnosticRuntimeHealth? Health = null,
    IncidentObserverHealth? Observer = null,
    string ObserverStatus = "not-collected");

public sealed record IncidentCrashEvent(DateTimeOffset RecordedAt, string Module, string ExceptionCode, string FaultOffset);

public sealed record IncidentCrashEvidence(string Status, IncidentCrashEvent[] Events);

public sealed record IncidentCollectionReport(int SchemaVersion, string Status,
    int MaximumSessions, int MaximumBytesPerFile, IncidentReport[] Sessions)
{
    public int AvailableSessions { get; init; }
    public int OmittedSessions { get; init; }
    public bool EnumerationTruncated { get; init; }
    public DateTimeOffset? WindowStart { get; init; }
    public string SelectionPolicy { get; init; } = "time-window";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(IncidentState))]
[JsonSerializable(typeof(IncidentTimelineEntry))]
[JsonSerializable(typeof(IncidentFailure))]
[JsonSerializable(typeof(IncidentFailureHistory))]
[JsonSerializable(typeof(DiagnosticRuntimeHealth))]
[JsonSerializable(typeof(IncidentExit))]
[JsonSerializable(typeof(IncidentObserverHealth))]
[JsonSerializable(typeof(IncidentCrashEvidence))]
[JsonSerializable(typeof(IncidentCollectionReport))]
internal sealed partial class IncidentJsonContext : JsonSerializerContext;
