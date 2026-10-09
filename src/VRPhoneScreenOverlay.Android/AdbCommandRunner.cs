using System.Diagnostics;
using System.Text;

namespace VRPhoneScreenOverlay.Android;

internal sealed record AdbCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration,
    bool TimedOut)
{
    public bool Succeeded => !TimedOut && ExitCode == 0;
}

internal interface IAdbCommandRunner
{
    public ValueTask<AdbCommandResult> RunAsync(
        IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken,
        string? standardInput = null);
}

internal sealed class AdbCommandRunner(string executablePath, bool killProcessTree = true) : IAdbCommandRunner
{
    private readonly string _executablePath = Path.GetFullPath(executablePath);
    private readonly bool _killProcessTree = killProcessTree;

    public string ExecutablePath => _executablePath;

    public async ValueTask<AdbCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string? standardInput = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();

        ProcessStartInfo startInfo = new()
        {
            FileName = _executablePath,
            WorkingDirectory = Path.GetDirectoryName(_executablePath),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        long startedAt = Stopwatch.GetTimestamp();
        if (!process.Start())
        {
            throw new InvalidOperationException(AndroidReasonCodes.ToolStartFailed);
        }

        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using CancellationTokenSource timeoutSource = new(timeout);
        using CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);

        bool timedOut = false;
        try
        {
            if (standardInput is not null)
            {
                await process.StandardInput.WriteLineAsync(standardInput.AsMemory(), linkedSource.Token)
                    .ConfigureAwait(false);
                process.StandardInput.Close();
            }
            await process.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested &&
                                                 !cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            TryKill(process);
        }
        catch
        {
            TryKill(process);
            throw;
        }

        if (timedOut)
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        string output = await CompleteReadAsync(outputTask).ConfigureAwait(false);
        string error = await CompleteReadAsync(errorTask).ConfigureAwait(false);
        TimeSpan duration = Stopwatch.GetElapsedTime(startedAt);
        return new AdbCommandResult(
            timedOut ? -1 : process.ExitCode,
            output,
            error,
            duration,
            timedOut);
    }

    private static async Task<string> CompleteReadAsync(Task<string> readTask)
    {
        try
        {
            return await readTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return string.Empty;
        }
    }

    private void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: _killProcessTree);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
