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
        bool diagnosticsAvailable = incident is not null;
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
                await RecordAsync(token => incident!.FailureAsync(exception, "cleanup/" + step.Name, null, token)).ConfigureAwait(false);
                await ObserveAsync(new("cleanup/" + step.Name, "failed")).ConfigureAwait(false);
            }
        }
        return failures;

        ValueTask ObserveAsync(IncidentCheckpoint checkpoint) =>
            RecordAsync(token => incident!.CheckpointAsync(checkpoint, token));

        async ValueTask RecordAsync(Func<CancellationToken, ValueTask> record)
        {
            if (!diagnosticsAvailable) { return; }
            // A recorder can block before returning its ValueTask (for example FlushToDisk).
            // Isolate diagnostics only; resource cleanup remains sequential and owned.
            Task pending = Task.Run(async () =>
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
                await record(deadline.Token).ConfigureAwait(false);
            });
            try { await pending.WaitAsync(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false); }
            catch (Exception)
            {
                diagnosticsAvailable = false;
                // At most one unfinished diagnostic operation per shutdown sequence.
                // Observe late failures and retain its cancellation source until it exits.
            }
            finally
            {
                if (pending.IsCompleted) { _ = pending.Exception; }
                else
                {
                    _ = pending.ContinueWith(completed =>
                    {
                        _ = completed.Exception;
                    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
            }
        }
    }
}
