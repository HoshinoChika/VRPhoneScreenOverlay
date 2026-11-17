using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.App;

/// <summary>Observes worker failures immediately; failure reporting cannot fault shutdown.</summary>
internal static class PanelWorker
{
    public static async Task RunAsync(Func<Task> run, string code,
        Action<string> showFailure, IIncidentRecorder? incident, CancellationToken token)
    {
        try { await run().ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            try { showFailure(code); }
            catch (Exception) { /* Rendering may itself be unavailable; retain the incident below. */ }
            if (incident is not null)
            {
                try { await incident.FailureAsync(exception, code, null, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception) { /* A failed diagnostic sink must not fault this owned worker. */ }
            }
        }
    }
}
