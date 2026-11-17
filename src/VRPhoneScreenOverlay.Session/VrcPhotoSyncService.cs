using System.Globalization;
using System.Text.RegularExpressions;

namespace VRPhoneScreenOverlay.Session;

/// <summary>One serial transfer, no in-memory image queue. Unsent files remain in the source album.</summary>
public sealed partial class VrcPhotoSyncService : IAsyncDisposable
{
    private readonly Func<string, CancellationToken, Task> _send;
    private readonly string _directory;
    private CancellationTokenSource? _stop;
    private Task _worker = Task.CompletedTask;
    private readonly object _lifecycle = new();
    private Task _transition = Task.CompletedTask;
    private bool _enabled;
    private bool _disposed;
    private string _status = "关闭";
    public string Status => Volatile.Read(ref _status);

    public VrcPhotoSyncService(string directory, Func<string, CancellationToken, Task> send)
    { _directory = directory; _send = send; }

    public Task SetEnabledAsync(bool enabled, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposed && enabled, this);
            // Capacity one transition: repeated stop/dispose joins it; a new start
            // during shutdown is rejected rather than queued behind stale intent.
            if (!_transition.IsCompleted)
            {
                if (enabled) { throw new InvalidOperationException("PHOTO_SYNC_OPERATION_PENDING"); }
                return _transition.WaitAsync(token);
            }
            if (_enabled == enabled) { return Task.CompletedTask; }
            _enabled = enabled;
            if (enabled)
            {
                _stop = new CancellationTokenSource();
                CancellationToken workerToken = _stop.Token;
                DateTime started = DateTime.Now;
                Volatile.Write(ref _status, "等待新照片");
                _worker = Task.Run(() => RunAsync(started, workerToken), CancellationToken.None);
                return Task.CompletedTask;
            }
            CancellationTokenSource? owner = _stop;
            _stop = null;
            _transition = StopWorkerAsync(owner, _worker);
            _ = _transition.ContinueWith(done => { _ = done.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return _transition.WaitAsync(token);
        }
    }

    private async Task StopWorkerAsync(CancellationTokenSource? owner, Task worker)
    {
        try
        {
            if (owner is not null) { await owner.CancelAsync().ConfigureAwait(false); }
            await worker.ConfigureAwait(false);
        }
        finally { owner?.Dispose(); Volatile.Write(ref _status, "关闭"); }
    }

    internal static bool IsNewPhoto(string path, DateTime started)
    {
        Match match = PhotoName().Match(Path.GetFileName(path));
        return match.Success && DateTime.TryParseExact(match.Groups["date"].Value, "yyyy-MM-dd_HH-mm-ss.fff",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime taken) && taken >= started;
    }

    [GeneratedRegex(@"^VRChat_(?:\d+x\d+_)?(?<date>\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}\.\d{3})(?:_\d+x\d+)?\.png$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PhotoName();

    private async Task RunAsync(DateTime started, CancellationToken token)
    {
        // Deduplication metadata only; file contents and pending transfers are never queued.
        var sent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                try
                {
                    if (!Directory.Exists(_directory)) { Volatile.Write(ref _status, "等待 VRChat 照片目录"); continue; }
                    bool failed = false;
                    foreach (string path in Directory.EnumerateFiles(_directory, "*.png", SearchOption.AllDirectories))
                    {
                        token.ThrowIfCancellationRequested();
                        if (sent.Contains(path) || !IsNewPhoto(path, started)) { continue; }
                        try
                        {
                            // Old photos copied into the album do not become new captures.
                            if (File.GetLastWriteTime(path) < started) { continue; }
                            long size = new FileInfo(path).Length;
                            await Task.Delay(400, token).ConfigureAwait(false);
                            if (size == 0 || new FileInfo(path).Length != size) { continue; }
                            Volatile.Write(ref _status, "正在同步新照片");
                            await _send(path, token).ConfigureAwait(false);
                            sent.Add(path);
                            Volatile.Write(ref _status, "已同步，等待新照片");
                        }
                        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
                        {
                            token.ThrowIfCancellationRequested();
                            failed = true;
                            Volatile.Write(ref _status, "部分照片待重试，请检查手机连接和文件");
                        }
                    }
                    Volatile.Write(ref _status, failed ? "部分照片待重试，请检查手机连接和文件" :
                        sent.Count == 0 ? "等待新照片" : "已同步，等待新照片");
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
                {
                    if (token.IsCancellationRequested) { break; }
                    Volatile.Write(ref _status, "等待重试，请检查手机连接和照片文件");
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifecycle)
        {
            _disposed = true;
            return new(SetEnabledAsync(false, CancellationToken.None));
        }
    }
}
