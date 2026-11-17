using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Session;

public readonly record struct VideoFrameStageDurations(double? ReceiveToDecodeStart, double? DecodeStartToOutput,
    double? OutputToSubmit, double? ConversionCall, double? SubmissionCall, double? CaptureToReceive = null);

/// <summary>
/// Throughput and latency counters for a running phone overlay session.
/// </summary>
/// <remarks>
/// Kept separate from the frame loop because these are related counters whose only
/// job is arithmetic. All time arrives as explicit elapsed values, so the maths is deterministic
/// and testable without a clock.
/// </remarks>
public sealed class PhoneOverlayMetrics
{
    private long _framesAtLastSample;
    private long _encodedBytesAtLastSample;
    private readonly VideoLatencyHistogram _capture = new();
    private readonly VideoLatencyHistogram _receive = new();
    private readonly VideoLatencyHistogram _decode = new();
    private readonly VideoLatencyHistogram _output = new();
    private readonly VideoLatencyHistogram _conversion = new();
    private readonly VideoLatencyHistogram _submission = new();
    private readonly VideoLatencyHistogram _captureToReceive = new();
    public long LatencySampleCount => _capture.SampleCount;
    public long UnavailableLatencySampleCount => _capture.MissingSamples;
    public long RejectedLatencySampleCount => _capture.RejectedSamples;
    public bool HasLatencySamples => LatencySampleCount > 0;
    public VideoLatencyBreakdown? LatencyBreakdown { get; private set; }

    /// <summary>Frames submitted to the headset since the session started.</summary>
    public long SubmittedFrames { get; private set; }

    /// <summary>Encoded bytes received, as last sampled.</summary>
    public long TotalEncodedBytes { get; private set; }

    /// <summary>Submitted frames per second over the most recent sampling window.</summary>
    public double FramesPerSecond { get; private set; }

    /// <summary>Mean encoded bitrate over the whole session.</summary>
    public double AverageBitrateMbps { get; private set; }

    /// <summary>Highest bitrate seen in any single sampling window.</summary>
    public double PeakBitrateMbps { get; private set; }

    /// <summary>
    /// Mean capture-to-submit latency. Zero until at least one frame carried a usable capture
    /// timestamp; consult HasLatencySamples to distinguish unavailable data from a measured zero.
    /// </summary>
    public double AverageLatencyMilliseconds => _capture.Mean ?? 0;

    /// <summary>
    /// Counts one submitted frame, folding in its end-to-end latency when measurable.
    /// </summary>
    /// <param name="latencyMilliseconds">
    /// Capture-to-submit latency, or <see langword="null"/> when the frame carried no capture
    /// timestamp. Values outside 0–5000 ms are ignored as implausible rather than allowed to
    /// distort the mean.
    /// </param>
    /// <param name="stages">Local stage durations; unavailable values remain null.</param>
    public void RecordSubmittedFrame(double? latencyMilliseconds, VideoFrameStageDurations stages = default)
    {
        SubmittedFrames++;
        _capture.Record(latencyMilliseconds);
        _receive.Record(stages.ReceiveToDecodeStart);
        _decode.Record(stages.DecodeStartToOutput);
        _output.Record(stages.OutputToSubmit);
        _conversion.Record(stages.ConversionCall);
        _submission.Record(stages.SubmissionCall);
        _captureToReceive.Record(stages.CaptureToReceive);
        if (SubmittedFrames == 1) { SampleLatency(); }
    }

    private void SampleLatency() => LatencyBreakdown = new(SubmittedFrames, _capture.Snapshot(),
        _receive.Snapshot(), _decode.Snapshot(), _output.Snapshot(), _conversion.Snapshot(), _submission.Snapshot(), _captureToReceive.Snapshot());

    /// <summary>
    /// Recomputes frame rate and bitrate once <paramref name="sinceLastSample"/> reaches one
    /// second, and reports whether a new sample was taken.
    /// </summary>
    /// <param name="sinceLastSample">Elapsed time since the previous accepted sample.</param>
    /// <param name="sinceStart">Elapsed time since the session started.</param>
    /// <param name="totalEncodedBytes">Encoded bytes received so far.</param>
    public bool TrySampleThroughput(
        TimeSpan sinceLastSample,
        TimeSpan sinceStart,
        long totalEncodedBytes)
    {
        double windowSeconds = sinceLastSample.TotalSeconds;
        if (windowSeconds < 1)
        {
            return false;
        }

        SampleLatency();
        TotalEncodedBytes = totalEncodedBytes;
        long windowBytes = Math.Max(0, totalEncodedBytes - _encodedBytesAtLastSample);
        FramesPerSecond = (SubmittedFrames - _framesAtLastSample) / windowSeconds;
        double windowBitrateMbps = windowBytes * 8d / windowSeconds / 1_000_000d;
        PeakBitrateMbps = Math.Max(PeakBitrateMbps, windowBitrateMbps);
        AverageBitrateMbps = totalEncodedBytes * 8d /
            Math.Max(sinceStart.TotalSeconds, 0.001d) /
            1_000_000d;
        _encodedBytesAtLastSample = totalEncodedBytes;
        _framesAtLastSample = SubmittedFrames;
        return true;
    }
}
