namespace VRPhoneScreenOverlay.SteamVR;

internal sealed class OpenVrDashboardInputGate
{
    private bool _waitingForRelease;

    public bool Suppressed { get; private set; }

    public void Apply(bool dashboard, OpenVrPlayspaceInputSample sample, bool sampleIsFresh,
        out bool drag, out bool reset)
    {
        drag = sample.DragPressed;
        reset = sample.ResetPressed;
        if (dashboard || sample.DashboardVisible) { _waitingForRelease = true; }
        else if (sampleIsFresh && sample.IsValid && !drag && !reset) { _waitingForRelease = false; }
        Suppressed = dashboard || sample.DashboardVisible || _waitingForRelease || !sample.IsValid;
        if (Suppressed)
        {
            drag = false;
            reset = false;
        }
    }
}
