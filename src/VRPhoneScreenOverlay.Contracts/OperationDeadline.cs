using System.Diagnostics;

namespace VRPhoneScreenOverlay.Contracts;

/// <summary>
/// Carries one monotonic timeout budget through a multi-step operation.
/// </summary>
public readonly record struct OperationDeadline
{
    private readonly long _expiresAtTimestamp;

    private OperationDeadline(long expiresAtTimestamp)
    {
        _expiresAtTimestamp = expiresAtTimestamp;
    }

    public bool IsExpired => Remaining == TimeSpan.Zero;

    public TimeSpan Remaining
    {
        get
        {
            long remainingTicks = _expiresAtTimestamp - Stopwatch.GetTimestamp();
            if (remainingTicks <= 0)
            {
                return TimeSpan.Zero;
            }

            return TimeSpan.FromSeconds((double)remainingTicks / Stopwatch.Frequency);
        }
    }

    public static OperationDeadline Start(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        long timeoutTicks = checked((long)Math.Ceiling(timeout.TotalSeconds * Stopwatch.Frequency));
        return new OperationDeadline(checked(Stopwatch.GetTimestamp() + timeoutTicks));
    }

    public TimeSpan GetRemainingUpTo(TimeSpan maximum)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximum, TimeSpan.Zero);
        TimeSpan remaining = Remaining;
        return remaining < maximum ? remaining : maximum;
    }

    public CancellationTokenSource CreateCancellationSource(CancellationToken cancellationToken)
    {
        CancellationTokenSource source =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        TimeSpan remaining = Remaining;
        if (remaining == TimeSpan.Zero)
        {
            source.Cancel();
        }
        else
        {
            source.CancelAfter(remaining);
        }

        return source;
    }
}
