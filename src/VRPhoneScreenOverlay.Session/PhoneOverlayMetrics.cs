namespace VRPhoneScreenOverlay.Session;

/// <summary>
/// Throughput and latency counters for a running phone overlay session.
/// </summary>
/// <remarks>
/// Kept separate from the frame loop because these are twelve interlocking counters whose only
/// job is arithmetic. All time arrives as explicit elapsed values, so the maths is deterministic
/// and testable without a clock.
/// </remarks>
public sealed class PhoneOverlayMetrics
{
    private long _framesAtLastSample;
    private long _encodedBytesAtLastSample;
    private double _totalLatencyMilliseconds;
    private long _latencySampleCount;

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
    /// timestamp.
    /// </summary>
    public double AverageLatencyMilliseconds { get; private set; }

    /// <summary>
    /// Counts one submitted frame, folding in its end-to-end latency when measurable.
    /// </summary>
    /// <param name="latencyMilliseconds">
    /// Capture-to-submit latency, or <see langword="null"/> when the frame carried no capture
    /// timestamp. Values outside 0–5000 ms are ignored as implausible rather than allowed to
    /// distort the mean.
    /// </param>
    public void RecordSubmittedFrame(double? latencyMilliseconds)
    {
        SubmittedFrames++;
        if (latencyMilliseconds is not double latency || latency is < 0 or > 5_000)
        {
            return;
        }

        _totalLatencyMilliseconds += latency;
        _latencySampleCount++;
        AverageLatencyMilliseconds = _totalLatencyMilliseconds / _latencySampleCount;
    }

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
