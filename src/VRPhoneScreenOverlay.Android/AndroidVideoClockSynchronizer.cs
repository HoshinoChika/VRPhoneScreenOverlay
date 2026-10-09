using System.Diagnostics;
using System.Globalization;

namespace VRPhoneScreenOverlay.Android;

internal sealed class AndroidVideoClockSynchronizer(
    long localTimestamp,
    long deviceMonotonicMicroseconds,
    TimeSpan roundTripDuration)
{
    private static readonly TimeSpan _maximumRoundTripDuration = TimeSpan.FromSeconds(1);
    private readonly long _localTimestamp = localTimestamp;
    private readonly long _deviceMonotonicMicroseconds = deviceMonotonicMicroseconds;

    public TimeSpan RoundTripDuration { get; } = roundTripDuration;

    public long? EstimateLocalCaptureTimestamp(long presentationTimeMicroseconds)
    {
        if (presentationTimeMicroseconds <= 0 ||
            RoundTripDuration < TimeSpan.Zero ||
            RoundTripDuration > _maximumRoundTripDuration)
        {
            return null;
        }

        double deltaTicks = (presentationTimeMicroseconds - _deviceMonotonicMicroseconds) *
            (double)Stopwatch.Frequency / 1_000_000d;
        double estimatedTimestamp = _localTimestamp + deltaTicks;
        if (!double.IsFinite(estimatedTimestamp) ||
            estimatedTimestamp < long.MinValue ||
            estimatedTimestamp > long.MaxValue)
        {
            return null;
        }

        return checked((long)Math.Round(estimatedTimestamp, MidpointRounding.AwayFromZero));
    }

    public static bool TryCreate(
        string deviceMonotonicSeconds,
        long commandStartedTimestamp,
        long commandCompletedTimestamp,
        out AndroidVideoClockSynchronizer? synchronizer)
    {
        synchronizer = null;
        if (commandCompletedTimestamp < commandStartedTimestamp)
        {
            return false;
        }

        if (!double.TryParse(
                deviceMonotonicSeconds,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out double monotonicSeconds) ||
            !double.IsFinite(monotonicSeconds) ||
            monotonicSeconds <= 0 ||
            monotonicSeconds > long.MaxValue / 1_000_000d)
        {
            return false;
        }

        long midpointTimestamp = commandStartedTimestamp +
            ((commandCompletedTimestamp - commandStartedTimestamp) / 2);
        TimeSpan roundTripDuration = Stopwatch.GetElapsedTime(
            commandStartedTimestamp,
            commandCompletedTimestamp);
        synchronizer = new AndroidVideoClockSynchronizer(
            midpointTimestamp,
            checked((long)Math.Round(
                monotonicSeconds * 1_000_000d,
                MidpointRounding.AwayFromZero)),
            roundTripDuration);
        return roundTripDuration <= _maximumRoundTripDuration;
    }
}
