using System.Diagnostics;
using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class AndroidVideoClockSynchronizerTests
{
    [Fact]
    public void MapsPhonePresentationTimeToLocalMonotonicClock()
    {
        long startedTimestamp = Stopwatch.GetTimestamp();
        long completedTimestamp = startedTimestamp + (Stopwatch.Frequency / 100);

        bool created = AndroidVideoClockSynchronizer.TryCreate(
            "100.000",
            startedTimestamp,
            completedTimestamp,
            out AndroidVideoClockSynchronizer? synchronizer);

        Assert.True(created);
        long estimatedCaptureTimestamp = Assert.IsType<long>(
            synchronizer!.EstimateLocalCaptureTimestamp(100_050_000));
        long expectedTimestamp = startedTimestamp +
            (Stopwatch.Frequency / 200) +
            (Stopwatch.Frequency / 20);
        Assert.InRange(
            estimatedCaptureTimestamp,
            expectedTimestamp - 1,
            expectedTimestamp + 1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("0.00 0.00")]
    public void RejectsInvalidPhoneMonotonicTime(string output)
    {
        long startedTimestamp = Stopwatch.GetTimestamp();

        bool created = AndroidVideoClockSynchronizer.TryCreate(
            output,
            startedTimestamp,
            startedTimestamp,
            out AndroidVideoClockSynchronizer? synchronizer);

        Assert.False(created);
        Assert.Null(synchronizer);
    }
}
