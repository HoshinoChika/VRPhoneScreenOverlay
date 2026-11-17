namespace VRPhoneScreenOverlay.Android;

// Extrema cover observed producer samples, not every frame. Counters are cumulative
// per retained stream epoch; deltas start at the preceding written observation.
public sealed record DiagnosticSamplingWindow(DateTimeOffset From, DateTimeOffset To,
    long Observations, long SuppressedTotal, long BurstsTotal, long DowngradesTotal,
    bool CounterReset, double? MinimumFps, double? MaximumFps, double? MinimumBitrateMbps,
    double? MaximumBitrateMbps, double? MinimumLatencyMilliseconds, double? MaximumLatencyMilliseconds,
    long? SubmittedFramesDelta, long? DroppedFramesDelta, long? AcceptedInputDelta,
    long? ServerLinesDelta, int PeakInputQueueDepth, double NoFrameSeconds,
    long? ServerErrorsDelta, long? RejectedInputDelta, long? ActionReadFailuresDelta);
