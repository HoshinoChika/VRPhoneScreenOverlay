using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Session;

internal static class PhoneInputEvidenceSampler
{
    // One task per overlay lifetime, independent of video arrivals. No input events
    // are queued here; both producers expose cumulative, bounded summaries.
    internal static async Task RunAsync(IOpenVrSceneOverlay overlay, PhoneInputRoutingEvidence routing,
        Action<OpenVrPhoneInteractionSnapshot, InputRoutingDiagnostic> sample, CancellationToken token)
    {
        void Sample() => sample(overlay.Interaction with { InputPath = overlay.CaptureInputEvidence() }, routing.Snapshot);
        try
        {
            using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
            do { Sample(); }
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { Sample(); }
    }
}
