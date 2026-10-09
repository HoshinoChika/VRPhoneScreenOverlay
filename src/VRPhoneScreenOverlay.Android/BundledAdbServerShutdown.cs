using System.ComponentModel;
using System.Diagnostics;

namespace VRPhoneScreenOverlay.Android;

internal interface IAdbServerShutdown
{
    public ValueTask<AdbServerShutdownResult> StopAsync(CancellationToken cancellationToken);
}

internal sealed record AdbServerShutdownResult(
    bool Succeeded,
    string ReasonCode,
    string Message);

internal sealed class BundledAdbServerShutdown(
    string resourceDirectory,
    TimeSpan commandTimeout) : IAdbServerShutdown
{
    private readonly string _adbExecutable = Path.GetFullPath(
        Path.Combine(resourceDirectory, "adb.exe"));
    private readonly TimeSpan _commandTimeout = commandTimeout;

    public async ValueTask<AdbServerShutdownResult> StopAsync(
        CancellationToken cancellationToken)
    {
        bool gracefulStopSucceeded = true;
        if (File.Exists(_adbExecutable))
        {
            try
            {
                AdbCommandResult result = await new AdbCommandRunner(_adbExecutable)
                    .RunAsync(["kill-server"], _commandTimeout, cancellationToken)
                    .ConfigureAwait(false);
                gracefulStopSucceeded = result.Succeeded;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                    InvalidOperationException or Win32Exception)
            {
                gracefulStopSucceeded = false;
            }
        }

        int forceStopped = StopRemainingBundledProcesses();
        bool remainsRunning = FindBundledProcesses().Count != 0;
        if (remainsRunning)
        {
            return new AdbServerShutdownResult(
                false,
                AndroidReasonCodes.AdbServerStopFailed,
                "内置 ADB 后台服务未能完全退出");
        }

        string message = gracefulStopSucceeded
            ? "内置 ADB 后台服务已退出"
            : $"内置 ADB 后台服务已强制退出（处理 {forceStopped} 个残留进程）";
        return new AdbServerShutdownResult(
            true,
            AndroidReasonCodes.AdbServerStopped,
            message);
    }

    private int StopRemainingBundledProcesses()
    {
        int stopped = 0;
        foreach (Process process in FindBundledProcesses())
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        if (process.WaitForExit(5_000))
                        {
                            stopped++;
                        }
                    }
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or Win32Exception or
                        NotSupportedException)
                {
                }
            }
        }

        return stopped;
    }

    private List<Process> FindBundledProcesses()
    {
        List<Process> matches = [];
        foreach (Process process in Process.GetProcessesByName("adb"))
        {
            try
            {
                if (string.Equals(
                        process.MainModule?.FileName,
                        _adbExecutable,
                        StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(process);
                    continue;
                }
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or Win32Exception or
                    NotSupportedException)
            {
            }

            process.Dispose();
        }

        return matches;
    }
}
