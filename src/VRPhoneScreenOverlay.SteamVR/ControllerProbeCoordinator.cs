namespace VRPhoneScreenOverlay.SteamVR;

// One in-flight probe, no queue; failed isolated probes back off for 30 seconds.
internal sealed class ControllerProbeCoordinator
{
    private int _active;
    private long _nextProbe;
    private OpenVrControllerHand _lastHand;
    private ControllerDiscoverySnapshot _last = new(false, null, "CONTROLLER_PROBE_WAITING");

    internal async Task<ControllerDiscoverySnapshot> ReadAsync(OpenVrControllerHand hand, Func<bool> available,
        Func<OpenVrControllerHand, ControllerDiscoverySnapshot?> readExisting,
        Func<OpenVrControllerHand, CancellationToken, Task<ControllerDiscoverySnapshot>> probe,
        long now, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0) { return new(false, null, "CONTROLLER_PROBE_BUSY"); }
        try
        {
            if (readExisting(hand) is { } existing) { return existing with { ProbeStage = "shared-runtime" }; }
            if (!available()) { return new(false, null, "CONTROLLER_RUNTIME_UNAVAILABLE") { ProbeStage = "preflight" }; }
            if (now < _nextProbe)
            { return _lastHand == hand ? _last : new(false, null, "CONTROLLER_PROBE_WAITING"); }
            _lastHand = hand;
            _nextProbe = now + 30_000;
            _last = new(false, null, "CONTROLLER_PROBE_WAITING");
            _last = await probe(hand, token).ConfigureAwait(false);
            return _last;
        }
        finally { Volatile.Write(ref _active, 0); }
    }
}
