namespace VRPhoneScreenOverlay.Contracts;

// Session-wide histogram of submitted frames only. Percentiles are bucket upper
// bounds (5 ms resolution), excluding missing, nonfinite and >5000 ms samples.
public sealed record VideoLatencyDistribution(long SampleCount, long MissingSamples, long RejectedSamples,
    double? MeanMilliseconds, double? P95UpperBoundMilliseconds, double? P99UpperBoundMilliseconds);

public sealed record VideoLatencyBreakdown(long SubmittedFrames,
    VideoLatencyDistribution CaptureToSubmit,
    VideoLatencyDistribution ReceiveToDecodeStart,
    VideoLatencyDistribution DecodeStartToOutput,
    VideoLatencyDistribution OutputToSubmit,
    VideoLatencyDistribution ConversionCall,
    VideoLatencyDistribution SubmissionCall,
    VideoLatencyDistribution CaptureToReceive)
{
    public int BucketWidthMilliseconds { get; } = 5;
    public int MaximumAcceptedMilliseconds { get; } = 5000;
    public string Scope { get; } = "session-submitted-frames";
}
