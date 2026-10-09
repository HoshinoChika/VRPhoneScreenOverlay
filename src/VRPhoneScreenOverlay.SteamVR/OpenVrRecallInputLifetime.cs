namespace VRPhoneScreenOverlay.SteamVR;

// Keep the native gesture active through physical release, so its progress UI
// gets its completion/release sample before normal phone bindings take over.
internal sealed class OpenVrRecallInputLifetime
{
    public bool Finishing { get; private set; }
    private bool _awaitingNewSample;
    public void Recalled() { Finishing = true; _awaitingNewSample = true; }
    public void Observe(bool active, bool pressed, bool dashboard)
    {
        if (dashboard) { Finishing = false; _awaitingNewSample = false; return; }
        // The trigger frame still used the old observer priority. Its aggregate
        // action may be active through unrelated buttons while recall is masked.
        if (_awaitingNewSample) { _awaitingNewSample = false; return; }
        if (active && !pressed) { Finishing = false; }
    }
}
