namespace VRPhoneScreenOverlay.Session;

internal sealed class PhoneAutoStartPolicy
{
    private bool _suppressed;
    private int _attempts;

    public void Reset() { _suppressed = false; _attempts = 0; }

    public void Suppress() => _suppressed = true;

    public bool TryBegin(bool enabled, bool deviceReady, bool runtimeReady, bool sessionStopped)
    {
        if (_suppressed || !enabled || !deviceReady || !runtimeReady || !sessionStopped || _attempts >= 3)
        {
            return false;
        }

        _attempts++;
        return true;
    }
}
