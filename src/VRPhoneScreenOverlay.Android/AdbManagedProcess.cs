using System.Diagnostics;
using System.Text;

namespace VRPhoneScreenOverlay.Android;

internal sealed class AdbManagedProcess : IAsyncDisposable
{
    private const int _maximumTailLines = 80;
    private readonly Process _process;
    private readonly Queue<string> _tail = new(_maximumTailLines);
    private readonly object _tailGate = new();
    private bool _disposed;

    private AdbManagedProcess(Process process)
    {
        _process = process;
        _process.OutputDataReceived += OnOutputDataReceived;
        _process.ErrorDataReceived += OnErrorDataReceived;
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public bool HasExited => _process.HasExited;

    public static AdbManagedProcess Start(
        string executablePath,
        IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);
        string fullPath = Path.GetFullPath(executablePath);
        ProcessStartInfo startInfo = new()
        {
            FileName = fullPath,
            WorkingDirectory = Path.GetDirectoryName(fullPath),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process = new() { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new AndroidConnectionException(
                    AndroidReasonCodes.ServerStartFailed,
                    "无法启动手机服务");
            }

            return new AdbManagedProcess(process);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    public string GetOutputTail()
    {
        lock (_tailGate)
        {
            return string.Join(" | ", _tail);
        }
    }

    public async ValueTask StopAsync(TimeSpan gracefulTimeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(gracefulTimeout, TimeSpan.Zero);
        if (_process.HasExited)
        {
            return;
        }

        using CancellationTokenSource timeout = new(gracefulTimeout);
        try
        {
            await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            TryKill();
            await _process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync(TimeSpan.Zero).ConfigureAwait(false);
        _process.OutputDataReceived -= OnOutputDataReceived;
        _process.ErrorDataReceived -= OnErrorDataReceived;
        _process.Dispose();
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs eventArgs) =>
        AddTail(eventArgs.Data);

    private void OnErrorDataReceived(object sender, DataReceivedEventArgs eventArgs) =>
        AddTail(eventArgs.Data);

    private void AddTail(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (_tailGate)
        {
            if (_tail.Count == _maximumTailLines)
            {
                _tail.Dequeue();
            }

            _tail.Enqueue(line.Trim());
        }
    }

    private void TryKill()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
