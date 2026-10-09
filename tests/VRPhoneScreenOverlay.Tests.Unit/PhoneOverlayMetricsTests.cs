using VRPhoneScreenOverlay.Session;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class PhoneOverlayMetricsTests
{
    [Fact]
    public void ThroughputIsNotSampledBeforeAFullSecond()
    {
        PhoneOverlayMetrics metrics = new();
        metrics.RecordSubmittedFrame(null);

        Assert.False(metrics.TrySampleThroughput(
            TimeSpan.FromMilliseconds(999),
            TimeSpan.FromMilliseconds(999),
            1_000));
        Assert.Equal(0, metrics.FramesPerSecond);
    }

    [Fact]
    public void FrameRateUsesTheFramesInTheSamplingWindow()
    {
        PhoneOverlayMetrics metrics = new();
        for (int index = 0; index < 60; index++)
        {
            metrics.RecordSubmittedFrame(null);
        }

        Assert.True(metrics.TrySampleThroughput(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            0));

        Assert.Equal(60, metrics.SubmittedFrames);
        Assert.Equal(60, metrics.FramesPerSecond, 3);
    }

    [Fact]
    public void SecondWindowCountsOnlyItsOwnFramesAndBytes()
    {
        PhoneOverlayMetrics metrics = new();
        for (int index = 0; index < 60; index++)
        {
            metrics.RecordSubmittedFrame(null);
        }

        metrics.TrySampleThroughput(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 1_000_000);
        for (int index = 0; index < 30; index++)
        {
            metrics.RecordSubmittedFrame(null);
        }

        metrics.TrySampleThroughput(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 1_250_000);

        Assert.Equal(90, metrics.SubmittedFrames);
        Assert.Equal(30, metrics.FramesPerSecond, 3);

        // Window two carried 250 KB in one second = 2 Mbps; window one carried 8 Mbps and
        // therefore remains the peak.
        Assert.Equal(8, metrics.PeakBitrateMbps, 3);

        // Average is over the whole session: 1.25 MB in two seconds = 5 Mbps.
        Assert.Equal(5, metrics.AverageBitrateMbps, 3);
    }

    [Fact]
    public void AverageLatencyIgnoresFramesWithoutACaptureTimestamp()
    {
        PhoneOverlayMetrics metrics = new();
        metrics.RecordSubmittedFrame(40);
        metrics.RecordSubmittedFrame(null);
        metrics.RecordSubmittedFrame(60);

        Assert.Equal(3, metrics.SubmittedFrames);
        Assert.Equal(50, metrics.AverageLatencyMilliseconds, 3);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5_001)]
    public void ImplausibleLatencyIsDiscardedRatherThanSkewingTheMean(double latency)
    {
        PhoneOverlayMetrics metrics = new();
        metrics.RecordSubmittedFrame(100);
        metrics.RecordSubmittedFrame(latency);

        Assert.Equal(2, metrics.SubmittedFrames);
        Assert.Equal(100, metrics.AverageLatencyMilliseconds, 3);
    }

    [Fact]
    public void LatencyStaysZeroUntilAFrameCarriesATimestamp()
    {
        PhoneOverlayMetrics metrics = new();
        metrics.RecordSubmittedFrame(null);

        Assert.Equal(0, metrics.AverageLatencyMilliseconds);
    }

    [Fact]
    public void EncodedByteCounterGoingBackwardsDoesNotProduceNegativeBitrate()
    {
        PhoneOverlayMetrics metrics = new();
        metrics.TrySampleThroughput(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 1_000_000);
        metrics.TrySampleThroughput(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), 500_000);

        Assert.True(metrics.PeakBitrateMbps >= 0);
        Assert.True(metrics.AverageBitrateMbps >= 0);
    }
}
