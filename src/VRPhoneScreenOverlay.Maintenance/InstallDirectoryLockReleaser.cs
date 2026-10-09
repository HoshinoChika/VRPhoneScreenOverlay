using System.ComponentModel;
using System.Diagnostics;

namespace VRPhoneScreenOverlay.Maintenance;

internal static class InstallDirectoryLockReleaser
{
    private const int _commandTimeoutMilliseconds = 10_000;

    public static void Release(string installDirectory) =>
        Release(installDirectory, RunCommand, StopRemainingBundledAdbProcesses);

    internal static void Release(
        string installDirectory,
        Func<ProcessStartInfo, int> commandRunner,
        Action<string> stopRemainingProcesses)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        ArgumentNullException.ThrowIfNull(commandRunner);
        ArgumentNullException.ThrowIfNull(stopRemainingProcesses);

        string adbExecutable = Path.Combine(
            Path.GetFullPath(installDirectory),
            "app", "resources",
            "android-platform-tools",
            "adb.exe");
        if (!File.Exists(adbExecutable)) { return; }

        ProcessStartInfo startInfo = new()
        {
            FileName = adbExecutable,
            WorkingDirectory = Path.GetDirectoryName(adbExecutable),
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("kill-server");
        int exitCode = commandRunner(startInfo);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Bundled ADB server did not stop cleanly (exit code {exitCode}).");
        }

        stopRemainingProcesses(adbExecutable);
    }

    private static int RunCommand(ProcessStartInfo startInfo)
    {
        Process process;
        try
        {
            process = Process.Start(startInfo) ??
                throw new InvalidOperationException("Bundled ADB stop command did not start.");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException(
                "Bundled ADB stop command did not start.",
                exception);
        }

        using (process)
        {
            if (!process.WaitForExit(_commandTimeoutMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5_000);
                }
                catch (InvalidOperationException)
                {
                }

                throw new InvalidOperationException("Bundled ADB server did not stop in time.");
            }

            return process.ExitCode;
        }
    }

    private static void StopRemainingBundledAdbProcesses(string adbExecutable)
    {
        foreach (Process process in Process.GetProcessesByName("adb"))
        {
            using (process)
            {
                try
                {
                    if (string.Equals(
                            process.MainModule?.FileName,
                            adbExecutable,
                            StringComparison.OrdinalIgnoreCase) &&
                        !process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        if (!process.WaitForExit(5_000))
                        {
                            throw new InvalidOperationException(
                                "Bundled ADB process remained active after shutdown.");
                        }
                    }
                }
                catch (Exception exception) when (
                    exception is Win32Exception or NotSupportedException)
                {
                    throw new InvalidOperationException(
                        "Bundled ADB process could not be inspected or stopped.",
                        exception);
                }
            }
        }
    }
}
