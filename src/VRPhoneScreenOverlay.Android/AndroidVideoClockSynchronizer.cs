using VRPhoneScreenOverlay.Contracts;
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

    internal VideoClockDiagnostic? Describe(long now)
    {
        if (now < _localTimestamp || RoundTripDuration < TimeSpan.Zero || RoundTripDuration > _maximumRoundTripDuration) { return null; }
        double age = ((double)now - _localTimestamp) * 1000 / Stopwatch.Frequency;
        return double.IsFinite(age) ? new(RoundTripDuration.TotalMilliseconds, age) : null;
    }

    public long? EstimateLocalCaptureTimestamp(long presentationTimeMicroseconds)
    {
        if (presentationTimeMicroseconds <= 0 ||
            RoundTripDuration < TimeSpan.Zero ||
            RoundTripDuration > _maximumRoundTripDuration)
        {
            return null;
        }

        double deltaTicks = ((double)presentationTimeMicroseconds - _deviceMonotonicMicroseconds) *
            (double)Stopwatch.Frequency / 1_000_000d;
        double estimatedTimestamp = _localTimestamp + deltaTicks;
        if (!double.IsFinite(estimatedTimestamp) ||
            estimatedTimestamp < long.MinValue ||
            estimatedTimestamp >= (double)long.MaxValue)
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
        if (commandStartedTimestamp < 0 || commandCompletedTimestamp < commandStartedTimestamp)
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
            monotonicSeconds >= (double)long.MaxValue / 1_000_000d)
        {
            return false;
        }

        double deviceMicroseconds = Math.Round(monotonicSeconds * 1_000_000d, MidpointRounding.AwayFromZero);
        // Int64.MaxValue rounds up to 2^63 as a double, so the upper bound must
        // be exclusive after rounding as well as before multiplying.
        if (deviceMicroseconds < 1 || deviceMicroseconds >= (double)long.MaxValue) { return false; }

        long midpointTimestamp = commandStartedTimestamp +
            ((commandCompletedTimestamp - commandStartedTimestamp) / 2);
        TimeSpan roundTripDuration = Stopwatch.GetElapsedTime(
            commandStartedTimestamp,
            commandCompletedTimestamp);
        if (roundTripDuration > _maximumRoundTripDuration) { return false; }
        synchronizer = new AndroidVideoClockSynchronizer(
            midpointTimestamp,
            checked((long)deviceMicroseconds),
            roundTripDuration);
        return true;
    }
}
