namespace VRPhoneScreenOverlay.Contracts;

public readonly record struct AudioPlaybackDiagnostic(long RequestedFrames, long DeliveredFrames,
    long SilenceFrames, long UnderrunCallbacks, double? DevicePositionMilliseconds,
    double? EngineLatencyEstimateMilliseconds, bool DevicePositionAvailable)
{
    public long ReprimeSilenceFrames { get; init; }
    public long InitialSilenceFrames { get; init; }
    public DateTimeOffset? DeviceClockSampledAt { get; init; }
    public long TimestampDiscontinuities { get; init; }
    public double CorrectionRatio { get; init; }
    public long AdjustedFrames { get; init; }
    public double ResamplerPendingMilliseconds { get; init; }
    public long RegulatorHeadroomResets { get; init; }
}
