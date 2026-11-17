namespace VRPhoneScreenOverlay.Contracts;

public sealed record VideoQualityDiagnostic(double FramesPerSecond, double BitrateMbps,
    double LatencyMilliseconds, long SubmittedFrames, long DroppedFrames)
{
    // The mean covers accepted samples over this session, not the latest frame.
    public long EncodedQueueBytes { get; init; }
    public long PeakEncodedQueueBytes { get; init; }
    public long EncodedQueueByteOverflows { get; init; }
    public VideoClockDiagnostic? VideoClock { get; init; }
    public VideoColorMetadata? VideoColor { get; init; }
    public VideoLatencyBreakdown? LatencyBreakdown { get; init; }
    public long LatencySampleCount { get; init; }
    public long UnavailableLatencySampleCount { get; init; }
    public long RejectedLatencySampleCount { get; init; }
    public bool HasLatencySamples => LatencySampleCount > 0 && double.IsFinite(LatencyMilliseconds);
    public double? ObservedFramesPerSecond { get; init; }
    public double PeakBitrateMbps { get; init; }
    public int RequestedResolutionPercent { get; init; }
    public int RequestedBitrateMbps { get; init; }
    public int RequestedMaximumFramesPerSecond { get; init; }
}
