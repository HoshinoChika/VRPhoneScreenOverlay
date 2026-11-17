using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Session;

public enum VrMenuRequestSource { Unspecified, MainWindow }

// Application lifetime, independent of phone connection and video decoding.
// One worker and one native menu; unavailable runtimes retry at a bounded rate.
public sealed class VrMenuService(
    VrMenuOverlayOwner owner,
    Func<IOpenVrSceneOverlay> createOverlay,
    Func<bool> runtimeReady,
    IAndroidConnectionLogSink log,
    Func<long>? runtimeIdentity = null,
    Func<Task>? resetRuntime = null,
    Func<Task>? stopVideo = null) : IAsyncDisposable, IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private Task? _worker;
    private Task? _stopTask;
    private Task? _disposeTask;
    private readonly object _requestGate = new();
    private Task _request = Task.CompletedTask;
    private bool _stopping;
    private int _disposed;
    private long _observedIdentity;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private volatile bool _openRequested;
    private bool _closeCompleted;
    private volatile bool _autoOpenEnabled;
    private int _autoOpenEpoch;
    private int _observedAutoOpenEpoch;
    private bool _manualCloseHold;
    public event EventHandler? StateChanged;

    public void ConfigureAutoOpen(bool enabled)
    {
        lock (_requestGate)
        {
            if (_stopping || _disposed != 0) { return; }
            if (enabled && !_autoOpenEnabled) { Interlocked.Increment(ref _autoOpenEpoch); }
            _autoOpenEnabled = enabled;
        }
    }
    public bool OpenRequested => _openRequested;
    public bool RuntimeReady => runtimeReady();

    public async Task SetOpenAsync(bool open, CancellationToken token, VrMenuRequestSource source = VrMenuRequestSource.Unspecified)
    {
        token.ThrowIfCancellationRequested();
        Task request;
        lock (_requestGate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0 || _stopping, this);
            if (!_request.IsCompleted) { throw new InvalidOperationException("VR_MENU_OPERATION_PENDING"); }
            // One owned operation, not one detached cleanup per click. Caller cancellation
            // stops waiting but must not release the native lifecycle gate prematurely.
            request = _request = Task.Run(() => SetOpenCoreAsync(open, source, token), CancellationToken.None);
            _ = request.ContinueWith(done => { _ = done.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        await request.WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
    }

    private async Task SetOpenCoreAsync(bool open, VrMenuRequestSource source, CancellationToken token)
    {
        await _lifecycle.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (open && _openRequested) { return; }
            string origin = source == VrMenuRequestSource.MainWindow ? "main-window" : "unspecified";
            log.TryWrite(new(DateTimeOffset.UtcNow, "vr_menu_request",
                open ? "VR_MENU_OPEN_REQUESTED" : "VR_MENU_CLOSE_REQUESTED",
                $"VR 浮窗请求：{(open ? "打开" : "关闭")}；来源={origin}"));
            if (open && runtimeReady() && !owner.IsReady)
            {
                _observedIdentity = runtimeIdentity?.Invoke() ?? 1;
                IOpenVrSceneOverlay overlay = createOverlay();
                try { owner.Attach(overlay); }
                catch { overlay.Dispose(); throw; }
            }
            if (!open)
            {
                owner.SuspendVideoAcquisition();
                if (stopVideo is not null) { await stopVideo().ConfigureAwait(false); }
                owner.Detach();
            }
            _openRequested = open;
            _closeCompleted = !open;
            _manualCloseHold = !open;
            _observedAutoOpenEpoch = Volatile.Read(ref _autoOpenEpoch);
        }
        catch (Exception)
        {
            log.TryWrite(new(DateTimeOffset.UtcNow, "vr_menu_request",
                open ? "VR_MENU_OPEN_FAILED" : "VR_MENU_CLOSE_FAILED", "VR 浮窗请求未完成；不代表进程异常退出"));
            throw;
        }
        finally { _lifecycle.Release(); }
    }

    public void Start()
    {
        lock (_requestGate)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0 || _stopping, this);
            if (_worker is not null) { throw new InvalidOperationException("VR menu already started."); }
            _worker = Task.Run(RunAsync);
        }
    }

    private async Task RunAsync()
    {
        string? previous = null;
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                string reason;
                await _lifecycle.WaitAsync(_stop.Token).ConfigureAwait(false);
                try
                {
                    long current = runtimeIdentity?.Invoke() ?? 1;
                    if (_observedIdentity != 0 && _observedIdentity != current)
                    {
                        owner.SuspendVideoAcquisition();
                        if (resetRuntime is not null) { await resetRuntime().ConfigureAwait(false); }
                        else { owner.Detach(); }
                    }
                    _observedIdentity = current;
                    int epoch = Volatile.Read(ref _autoOpenEpoch);
                    if (epoch != _observedAutoOpenEpoch)
                    { _observedAutoOpenEpoch = epoch; _manualCloseHold = false; }
                    if (_autoOpenEnabled && !_manualCloseHold && !_openRequested && current != 0 && runtimeReady())
                    {
                        _openRequested = true;
                        _closeCompleted = false;
                        log.TryWrite(new(DateTimeOffset.UtcNow, "vr_menu_request", "VR_MENU_AUTO_OPEN_REQUESTED",
                            "SteamVR 已就绪，按已保存设置自动打开 VR 浮窗"));
                    }
                    if (!_openRequested || current == 0 || (!owner.IsReady && !runtimeReady()))
                    {
                        if (_openRequested) { owner.Detach(); }
                        reason = _openRequested ? "VR_MENU_WAITING" : _closeCompleted ? "VR_MENU_CLOSED" : "VR_MENU_NOT_REQUESTED";
                    }
                    else
                    {
                        if (!owner.IsReady)
                        {
                            IOpenVrSceneOverlay overlay = createOverlay();
                            try { owner.Attach(overlay); }
                            catch { overlay.Dispose(); throw; }
                        }
                        reason = "VR_MENU_READY";
                    }
                }
                catch (Exception error) when (error is OpenVrOverlayException or InvalidOperationException)
                {
                    reason = "VR_MENU_RETRY";
                }
                finally { _lifecycle.Release(); }
                if (reason != previous)
                {
                    log.TryWrite(new(DateTimeOffset.UtcNow, "vr_menu", reason,
                        reason == "VR_MENU_READY" ? "VR 菜单已就绪" :
                        reason == "VR_MENU_CLOSED" ? "VR 浮窗关闭请求已完成" :
                        reason == "VR_MENU_NOT_REQUESTED" ? "尚未请求打开 VR 浮窗" :
                        reason == "VR_MENU_RETRY" ? "VR 菜单创建或恢复失败，等待重试" : "已请求打开 VR 浮窗，等待运行时就绪"));
                    previous = reason;
                    try { StateChanged?.Invoke(this, EventArgs.Empty); }
                    catch (Exception) { /* Presentation observers cannot terminate the native menu worker. */ }
                }
                await Task.Delay(TimeSpan.FromSeconds(1), _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    public ValueTask StopAsync()
    {
        lock (_requestGate) { return new(_stopTask ??= StopCoreAsync()); }
    }

    private async Task StopCoreAsync()
    {
        Task request;
        lock (_requestGate) { _stopping = true; request = _request; }
        // WhenAll waits for every owner, including when cancellation or a request
        // faults. An early exception must not let Dispose release native resources
        // while the runtime reset worker is still using them.
        await Task.WhenAll(_stop.CancelAsync(), ObserveRequestAsync(request), _worker ?? Task.CompletedTask).ConfigureAwait(false);
    }

    private static async Task ObserveRequestAsync(Task request)
    {
        try { await request.ConfigureAwait(false); }
        catch (Exception error) when (error is OperationCanceledException or IOException or InvalidOperationException) { }
    }

    public ValueTask DisposeAsync()
    {
        lock (_requestGate) { return new(_disposeTask ??= DisposeCoreAsync()); }
    }

    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        try { await StopAsync().ConfigureAwait(false); }
        finally { owner.Dispose(); _stop.Dispose(); _lifecycle.Dispose(); }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
