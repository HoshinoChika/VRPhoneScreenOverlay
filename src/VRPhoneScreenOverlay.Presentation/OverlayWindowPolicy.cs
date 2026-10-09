namespace VRPhoneScreenOverlay.Presentation;

// One minimize per successful open. Repeated metrics, preference changes and
// quality/binding restarts must not minimize a manually restored window again.
public sealed class OverlayWindowPolicy
{
    private bool _opened;

    public bool Observe(bool running, bool sessionEnded, bool enabled)
    {
        if (sessionEnded) { _opened = false; }
        if (!running || _opened) { return false; }
        _opened = true;
        return enabled;
    }
}
