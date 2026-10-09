namespace VRPhoneScreenOverlay.Core;

public sealed record ShutdownStep(string Name, Func<ValueTask> Stop);

/// <summary>Runs every owned cleanup even when an earlier owner fails. No detached cleanup tasks.</summary>
public static class ShutdownSequence
{
    public static async Task<IReadOnlyList<string>> RunAsync(IEnumerable<ShutdownStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        List<string> failures = [];
        foreach (ShutdownStep step in steps)
        {
            try { await step.Stop().ConfigureAwait(false); }
            catch (Exception) { failures.Add(step.Name); }
        }
        return failures;
    }
}
