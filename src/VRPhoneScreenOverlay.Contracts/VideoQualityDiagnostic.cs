namespace VRPhoneScreenOverlay.Contracts;

public sealed record VideoQualityDiagnostic(double FramesPerSecond, double BitrateMbps,
    double LatencyMilliseconds, long SubmittedFrames, long DroppedFrames)
{
    public double? ObservedFramesPerSecond { get; init; }
    public double PeakBitrateMbps { get; init; }
    public int RequestedResolutionPercent { get; init; }
    public int RequestedBitrateMbps { get; init; }
    public int RequestedMaximumFramesPerSecond { get; init; }
}
