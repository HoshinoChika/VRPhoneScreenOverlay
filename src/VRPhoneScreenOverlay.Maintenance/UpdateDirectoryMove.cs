namespace VRPhoneScreenOverlay.Maintenance;

internal static class UpdateDirectoryMove
{
    internal const int MaximumAttempts = 5;
    internal static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(3);

    // The standalone maintenance transaction is synchronous. Keep retries on its
    // existing worker, outside the UI, and never replay an already completed move.
    internal static void Move(string source, string destination, Action<TimeSpan>? delay = null,
        Action<string, string>? move = null)
    {
        move ??= Directory.Move;
        delay ??= Thread.Sleep;
        for (int attempt = 1; ; attempt++)
        {
            try { move(source, destination); return; }
            catch (IOException error) when (attempt < MaximumAttempts &&
                (error.HResult == unchecked((int)0x80070020) || error.HResult == unchecked((int)0x80070021)))
            {
                // Sharing/lock violations are transient. Missing paths, permission
                // failures and invalid destinations must retain their original error.
                delay(RetryInterval);
            }
        }
    }
}
