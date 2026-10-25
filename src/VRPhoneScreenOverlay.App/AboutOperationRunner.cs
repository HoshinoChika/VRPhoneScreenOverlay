namespace VRPhoneScreenOverlay.App;

// The asynchronous button boundary returns safe inline text, including errors
// from the shared network layer. Unexpected exceptions never escape async void.
internal static class AboutOperationRunner
{
    public static async Task<string?> RunAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            await operation(cancellationToken).ConfigureAwait(true);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            return UserFacingErrorMessages.FromException(exception);
        }
    }
}
