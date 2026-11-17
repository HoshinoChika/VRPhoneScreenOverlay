namespace VRPhoneScreenOverlay.Android;

// Owned by discovery's operation gate. At most 64 saved-target cooldowns;
// evict the oldest attempt if preferences change. No queued attempts or worker.
internal sealed class ManagedWirelessRetry
{
    private readonly Dictionary<string, (int Failures, long NotBefore, long LastAttempt)> _targets = new(StringComparer.Ordinal);
    private long _nextGlobalAttempt;

    internal bool TryBegin(string target, long now)
    {
        if (now < _nextGlobalAttempt || (_targets.TryGetValue(target, out var state) && now < state.NotBefore)) { return false; }
        _nextGlobalAttempt = Add(now, 10000);
        return true;
    }

    internal void Complete(string target, bool connected, long now)
    {
        _nextGlobalAttempt = Add(now, 10000);
        if (connected) { _targets.Remove(target); return; }
        int failures = _targets.TryGetValue(target, out var previous) ? Math.Min(3, previous.Failures + 1) : 1;
        if (!_targets.ContainsKey(target) && _targets.Count >= 64)
        { _targets.Remove(_targets.MinBy(entry => entry.Value.LastAttempt).Key); }
        _targets[target] = (failures, Add(now, Math.Min(60000, 10000 << failures)), now);
    }

    internal void Reset() { _targets.Clear(); _nextGlobalAttempt = 0; }
    private static long Add(long now, int delay) => now > long.MaxValue - delay ? long.MaxValue : now + delay;
}
