namespace VRPhoneScreenOverlay.Contracts;

public sealed record InputBlockCount(string Reason, long Samples);

public sealed record PhoneInputPathDiagnostic(string SessionId, DateTimeOffset CapturedAt,
    long Samples, long ActionReadFailures, long ActiveActionSamples, long ObservedPressEdges,
    long RayHitSamples, long EligibleSamples, long DownCommands, long MoveCommands, long UpCommands,
    long CancelCommands, long OtherCommands, long SinkFailures, string CurrentBlock,
    int LastActionReadError, bool ActionSetEnabled, bool ActionActive, bool ActionPressed,
    bool RayHit, bool PointerVisible, IReadOnlyList<InputBlockCount> Blocks)
{
    public DateTimeOffset? LastObservedAt { get; init; }
    public long ObserverActiveSamples { get; init; }
    public long ObserverPressEdges { get; init; }
    public bool ObserverActive { get; init; }
    public bool ObserverPressed { get; init; }
    public bool AwaitingFreshVideoFrame { get; init; }
    public long RecallRequests { get; init; }
    public long RecallCompleted { get; init; }
    public string? LastRecallReason { get; init; }
    public DateTimeOffset? LastRecallAt { get; init; }
}

public sealed record InputRoutingDiagnostic(long Received, long Forwarded, long ControlNotReady,
    long GeometryMissing, string LastDecision, string? LastCommandKind, DateTimeOffset? LastAt);
