namespace VRPhoneScreenOverlay.Contracts;

public sealed record InputDispatchDiagnostic(int ReliableCapacity, int ReliableDepth, int TerminalDepth,
    long RejectedCommands, long ReservedTerminals, long CoalescedTerminals, long DroppedMoves,
    long TerminalAttempts, long TerminalWrites, string TerminationStatus, bool PointerMayBeDown,
    string? InFlightKind, string? FailureReason, string? FailureType, int? FailureHResult, bool WorkerCompleted)
{
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? SecondaryFailureType { get; init; }
    public int? SecondaryFailureHResult { get; init; }
    public long DiscardedReliable { get; init; }
    public long DiscardedTerminals { get; init; }
    public long AcceptedReliable { get; init; }
    public long AcceptedMoves { get; init; }
    public long LastAcceptedSequence { get; init; }
    public long MaintenanceCompletions { get; init; }
    public long SuppressedCommands { get; init; }
    public string? LastSuppressionReason { get; init; }
}
