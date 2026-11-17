using System.Globalization;

namespace VRPhoneScreenOverlay.Android;

internal static class ScrcpyForwardPort
{
    internal static async Task<int> ResolveAsync(string output, IAdbCommandRunner runner, string serial,
        string socket, string reason, string message)
    {
        if (int.TryParse(output.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int port) && port is >= 1 and <= 65535) { return port; }
        Exception? cleanup = await CleanupAsync(runner, serial, socket).ConfigureAwait(false);
        throw new AndroidConnectionException(reason, message, cleanup);
    }
    internal static async Task<int> CreateAsync(Func<Task<string>> create, IAdbCommandRunner runner, string serial,
        string socket, string reason, string message)
    {
        string output;
        try { output = await create().ConfigureAwait(false); }
        catch
        {
            // The daemon may have installed the mapping before cancellation or
            // a client-side error hid the reply. Never replace that primary error.
            _ = await CleanupAsync(runner, serial, socket).ConfigureAwait(false);
            throw;
        }
        return await ResolveAsync(output, runner, serial, socket, reason, message).ConfigureAwait(false);
    }

    private static async Task<Exception?> CleanupAsync(IAdbCommandRunner runner, string serial, string socket)
    {
        // The forward command already succeeded. Caller cancellation must not skip
        // its bounded cleanup; the primary invalid-port failure remains authoritative.
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(5));
        try
        {
            AdbCommandResult list = await runner.RunAsync(["forward", "--list"], TimeSpan.FromSeconds(2), deadline.Token).ConfigureAwait(false);
            if (!list.Succeeded) { throw new IOException("SCRCPY_FORWARD_LIST_FAILED"); }
            string[] matches = list.StandardOutput.Split('\n')
                .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                .Where(parts => parts.Length == 3 && parts[0] == serial && parts[2] == "localabstract:" + socket)
                .Select(parts => parts[1]).Distinct(StringComparer.Ordinal).Take(2).ToArray();
            if (matches.Length != 1 || !matches[0].StartsWith("tcp:", StringComparison.Ordinal) ||
                !int.TryParse(matches[0].AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out int recovered) || recovered is < 1 or > 65535)
            { throw new IOException("SCRCPY_FORWARD_OWNER_UNCONFIRMED"); }
            AdbCommandResult removed = await runner.RunAsync(["-s", serial, "forward", "--remove", $"tcp:{recovered}"],
                TimeSpan.FromSeconds(3), deadline.Token).ConfigureAwait(false);
            if (!removed.Succeeded) { throw new IOException("SCRCPY_FORWARD_REMOVE_FAILED"); }
        }
        catch (Exception error)
        {
            // Cleanup is secondary: preserve even an unexpected cleanup exception
            // as evidence without replacing the original launch/cancellation failure.
            return error;
        }
        return null;
    }

}
