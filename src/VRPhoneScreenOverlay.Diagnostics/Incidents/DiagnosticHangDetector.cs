namespace VRPhoneScreenOverlay.Diagnostics;

/// <summary>Debounces scheduler/suspend gaps without discarding genuinely long UI hangs.</summary>
internal sealed class DiagnosticHangDetector
{
    private long _lastPoll;
    private long _graceUntil;

    internal bool Observe(bool stale, long now)
    {
        if (_lastPoll != 0 && now - _lastPoll > 5000) { _graceUntil = now + 10000; }
        _lastPoll = now;
        return stale && now >= _graceUntil;
    }

    internal static bool IsStale(long pulse, long now) => pulse > 0 && now >= pulse && now - pulse >= 10000;
}
