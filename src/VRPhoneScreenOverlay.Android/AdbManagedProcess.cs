using System.Diagnostics;
using System.Text;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Android;

internal sealed class AdbManagedProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly ServerOutputEvidence _evidence = new();
    private readonly CancellationTokenSource _readStop = new();
    private readonly Task _readers;
    private readonly Task _reporter;
    private readonly Action<ServerOutputDiagnostic>? _observe;
    private string? _reportingFailure;
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private bool _disposed;

    private AdbManagedProcess(Process process, Action<ServerOutputDiagnostic>? observe)
    {
        _process = process;
        _observe = observe;
        _readers = Task.WhenAll(_evidence.ReadAsync(process.StandardOutput, false, _readStop.Token),
            _evidence.ReadAsync(process.StandardError, true, _readStop.Token));
        _reporter = ReportAsync(_readStop.Token);
    }

    public bool HasExited => _process.HasExited;

    public static AdbManagedProcess Start(
        string executablePath,
        IReadOnlyList<string> arguments, Action<ServerOutputDiagnostic>? observe = null)
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

            return new AdbManagedProcess(process, observe);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    public string GetOutputTail()
    {
        return _evidence.GetTail();
    }

    internal ServerOutputDiagnostic OutputDiagnostic => _evidence.Snapshot with
    { ReportingFailureType = Volatile.Read(ref _reportingFailure), SessionId = _sessionId };

    private void Report(ServerOutputDiagnostic snapshot)
    {
        try { _observe?.Invoke(snapshot); }
        catch (Exception error) { Volatile.Write(ref _reportingFailure, error.GetType().FullName); }
    }

    private async Task ReportAsync(CancellationToken token)
    {
        if (_observe is null) { return; }
        try
        {
            using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                ServerOutputDiagnostic snapshot = OutputDiagnostic;
                Report(snapshot);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
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
            using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(2));
            try { await _process.WaitForExitAsync(cleanup.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cleanup.IsCancellationRequested)
            { throw new IOException("Owned ADB client did not exit within the cleanup deadline."); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try { await StopAsync(TimeSpan.Zero).ConfigureAwait(false); }
        finally
        {
            // Give final stderr/unterminated lines a bounded chance to reach EOF first.
            try { await _readers.WaitAsync(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false); }
            catch (TimeoutException) { }
            await _readStop.CancelAsync().ConfigureAwait(false);
            _process.StandardOutput.Dispose();
            _process.StandardError.Dispose();
            try { await _readers.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException or TimeoutException) { }
            await _reporter.ConfigureAwait(false);
            Report(OutputDiagnostic);
            _process.Dispose();
            _readStop.Dispose();
        }
    }

    private void TryKill()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: false);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
