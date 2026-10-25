using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Core;

public sealed record ShutdownStep(string Name, Func<ValueTask> Stop);

/// <summary>Runs every owned cleanup even when an earlier owner fails. No detached cleanup tasks.</summary>
public static class ShutdownSequence
{
    public static async Task<IReadOnlyList<string>> RunAsync(IEnumerable<ShutdownStep> steps, IIncidentRecorder? incident = null)
    {
        ArgumentNullException.ThrowIfNull(steps);
        List<string> failures = [];
        foreach (ShutdownStep step in steps)
        {
            await ObserveAsync(new("cleanup/" + step.Name, "started")).ConfigureAwait(false);
            try
            {
                await step.Stop().ConfigureAwait(false);
                await ObserveAsync(new("cleanup/" + step.Name, "succeeded")).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(step.Name);
                if (incident is not null)
                {
                    try { await incident.FailureAsync(exception, "cleanup/" + step.Name, null, CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception) { /* A diagnostic sink must never skip the next owned cleanup. */ }
                }
                await ObserveAsync(new("cleanup/" + step.Name, "failed")).ConfigureAwait(false);
            }
        }
        return failures;

        async ValueTask ObserveAsync(IncidentCheckpoint checkpoint)
        {
            if (incident is null) { return; }
            try { await incident.CheckpointAsync(checkpoint, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception) { /* Continue releasing resources even when diagnostics are unavailable. */ }
        }
    }
}
