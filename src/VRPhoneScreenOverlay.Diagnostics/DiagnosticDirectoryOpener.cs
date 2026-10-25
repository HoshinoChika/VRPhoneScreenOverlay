using System.Diagnostics;

namespace VRPhoneScreenOverlay.Diagnostics;

/// <summary>One user-requested shell operation at a time, independent of the UI thread.</summary>
internal sealed class DiagnosticDirectoryOpener
{
    private readonly object _gate = new();
    private readonly Action<string> _open;
    private readonly TimeSpan _timeout;
    // Capacity one, no queue: a timed-out shell call keeps ownership until it
    // returns. Repeated clicks cannot accumulate blocked thread-pool workers.
    private Task<Exception?>? _pending;

    public DiagnosticDirectoryOpener() : this(OpenDirectory, TimeSpan.FromSeconds(5)) { }

    internal DiagnosticDirectoryOpener(Action<string> open, TimeSpan timeout)
    {
        _open = open;
        _timeout = timeout;
    }

    public async ValueTask OpenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        CancellationToken token = deadline.Token;
        Task<Exception?> pending;
        lock (_gate)
        {
            if (_pending is { IsCompleted: false })
            { throw new DiagnosticsException("DIAGNOSTICS_FOLDER_OPEN_BUSY", "文件夹打开请求仍在处理，请稍后重试"); }
            pending = _pending = Task.Run<Exception?>(() =>
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    _open(DiagnosticLocalFiles.LogsDirectory);
                    token.ThrowIfCancellationRequested();
                    _open(DiagnosticLocalFiles.DiagnosticsDirectory);
                    return null;
                }
                catch (Exception error)
                {
                    // Retain a completed result even if the caller timed out;
                    // a late shell failure must not become an unobserved task.
                    return error;
                }
            }, CancellationToken.None);
        }
        Exception? failure;
        try { failure = await pending.WaitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new DiagnosticsException("DIAGNOSTICS_FOLDER_OPEN_TIMEOUT", "打开日志文件夹超时，请稍后重试"); }
        cancellationToken.ThrowIfCancellationRequested();
        if (failure is OperationCanceledException)
        { throw new DiagnosticsException("DIAGNOSTICS_FOLDER_OPEN_TIMEOUT", "打开日志文件夹超时，请稍后重试"); }
        if (failure is not null)
        { throw new DiagnosticsException("DIAGNOSTICS_FOLDER_OPEN_FAILED", "无法打开日志文件夹，请检查本地访问权限", failure); }
    }

    private static void OpenDirectory(string path)
    {
        Directory.CreateDirectory(path);
        using Process? process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
