using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Session;

// Fixed 1001 buckets, 8008 bytes of counts per stage. Single frame-loop owner;
// no raw samples, tasks or per-frame allocations. Invalid samples are counted.
internal sealed class VideoLatencyHistogram
{
    private readonly long[] _buckets = new long[1001];
    private double _sum;
    public long SampleCount { get; private set; }
    public long MissingSamples { get; private set; }
    public long RejectedSamples { get; private set; }
    public double? Mean => SampleCount == 0 ? null : _sum / SampleCount;

    public void Record(double? milliseconds)
    {
        if (milliseconds is not double value) { MissingSamples++; return; }
        if (!double.IsFinite(value) || value is < 0 or > 5000) { RejectedSamples++; return; }
        _buckets[(int)Math.Ceiling(value / 5)]++;
        _sum += value;
        SampleCount++;
    }

    public VideoLatencyDistribution Snapshot() => new(SampleCount, MissingSamples, RejectedSamples,
        Mean, Percentile(0.95), Percentile(0.99));

    private double? Percentile(double fraction)
    {
        if (SampleCount == 0) { return null; }
        long target = (long)Math.Ceiling(SampleCount * fraction);
        long cumulative = 0;
        for (int i = 0; i < _buckets.Length; i++)
        {
            cumulative += _buckets[i];
            if (cumulative >= target) { return i * 5d; }
        }
        return 5000;
    }
}
