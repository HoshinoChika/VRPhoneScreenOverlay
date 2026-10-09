namespace VRPhoneScreenOverlay.SteamVR;

// OVRAS advances movement once per compositor frame. Re-reading a pose at the
// worker's 4 ms polling rate must not overwrite a throw velocity with zero.
internal sealed class OpenVrPlayspaceFrameClock
{
    private ulong? _lastFrame;
    private double? _lastSeconds;

    public bool TryAdvance(ulong? frame, double now, out float seconds)
    {
        seconds = 0;
        if (_lastSeconds is not double previous)
        {
            _lastSeconds = now;
            _lastFrame = frame;
            return false;
        }
        if (frame.HasValue ? frame == _lastFrame : now - previous < 1.0 / 120)
        {
            return false;
        }
        seconds = (float)(now - previous);
        _lastSeconds = now;
        _lastFrame = frame;
        return true;
    }
}
