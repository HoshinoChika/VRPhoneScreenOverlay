using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.Input;

namespace VRPhoneScreenOverlay.Session;

public enum PhoneControlState
{
    Stopped,
    Starting,
    Ready,
    Faulted,
    Stopping,
}

public sealed record PhoneControlSnapshot(
    PhoneControlState State,
    string ReasonCode,
    string Message,
    string? DeviceKey,
    long SentCommands,
    long ReplacedPointerMoves,
    double LastQueueDelayMilliseconds,
    bool ScreenGuardEnabled = false,
    bool ScreenGuardFaulted = false);

public sealed class PhoneControlChangedEventArgs(PhoneControlSnapshot snapshot) : EventArgs
{
    public PhoneControlSnapshot Snapshot { get; } = snapshot;
}

public sealed class PhoneControlServiceException(
    string reasonCode,
    string message,
    Exception? inner = null) : Exception(message, inner)
{
    public string ReasonCode { get; } = reasonCode;
}

public interface IPhoneControlService : IAsyncDisposable
{
    public void ConfigurePhysicalScreen(bool turnOffScreen);
    public PhoneControlSnapshot Snapshot { get; }

    public PhoneControlSnapshot DiagnosticSnapshot { get; }

    public event EventHandler<PhoneControlChangedEventArgs>? StateChanged;

    public ValueTask StartAsync(string? deviceKey, CancellationToken cancellationToken);

    public ValueTask SendAsync(
        PhoneInputCommandKind kind,
        float normalizedX,
        float normalizedY,
        int screenWidth,
        int screenHeight,
        float scrollDelta,
        CancellationToken cancellationToken,
        int unlockDigit = -1);

    public void QueueFromSteamVr(
        PhoneInputCommandKind kind,
        float normalizedX,
        float normalizedY,
        int screenWidth,
        int screenHeight,
        float scrollDelta = 0,
        int unlockDigit = -1);

    public ValueTask StopAsync(CancellationToken cancellationToken);
}

public sealed class PhoneControlService(
    IAndroidControlSessionFactory sessions,
    IAndroidConnectionLogSink log) : IPhoneControlService
{
    private const long _primaryPointerId = -2;
    private readonly IAndroidControlSessionFactory _sessions = sessions;
    private readonly IAndroidConnectionLogSink _log = log;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _stateGate = new();
    private PhoneInputDispatcher? _dispatcher;
    private IAndroidControlSession? _session;
    private long _sequence;
    private bool _disposed;
    private volatile bool _turnOffScreen;
    private bool _defaultScreenOff;
    // One parity slot owned by ScreenGuardSettingsController. Toggle pairs cancel;
    // overflow coalesces without losing the final intent or allocating per frame.
    private bool _pendingScreenGuardToggle;
    internal long ScreenGuardRequests { get; private set; }
    internal long CoalescedScreenGuardRequests { get; private set; }

    internal bool TakeScreenGuardToggle()
    {
        lock (_stateGate)
        {
            bool pending = _pendingScreenGuardToggle;
            _pendingScreenGuardToggle = false;
            return pending;
        }
    }

    internal void ReportScreenGuardSaveFailure()
    {
        lock (_stateGate) { _guardFaulted = true; }
        Publish(Snapshot with { ReasonCode = "SCREEN_GUARD_SAVE_FAILED", Message = "防误触设置保存失败，请重试" });
    }
    private bool _guardApplied;
    private bool _guardFaulted;
    private CancellationTokenSource? _displayLifetime;
    private Task _displayTask = Task.CompletedTask;

    public void ConfigurePhysicalScreen(bool turnOffScreen)
    {
        lock (_stateGate)
        {
            _turnOffScreen = turnOffScreen;
            _defaultScreenOff = turnOffScreen;
        }
    }
    private PhoneControlSnapshot _diagnosticSnapshot = new(
        PhoneControlState.Stopped,
        PhoneReasonCodes.ControlNotStarted,
        "手机控制尚未启动",
        null,
        0,
        0,
        0);

    public PhoneControlSnapshot Snapshot { get; private set; } = new(
        PhoneControlState.Stopped,
        PhoneReasonCodes.ControlNotStarted,
        "手机控制尚未启动",
        null,
        0,
        0,
        0);

    public PhoneControlSnapshot DiagnosticSnapshot
    {
        get
        {
            lock (_stateGate)
            {
                return _diagnosticSnapshot;
            }
        }
    }

    public event EventHandler<PhoneControlChangedEventArgs>? StateChanged;

    public async ValueTask StartAsync(
        string? deviceKey,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(deviceKey))
        {
            throw new PhoneControlServiceException(
                PhoneReasonCodes.ControlDeviceNotReady,
                "没有可用于控制的手机");
        }

        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_stateGate)
            {
                if (_dispatcher is not null &&
                    _session is not null &&
                    string.Equals(_session.DeviceKey, deviceKey, StringComparison.Ordinal) &&
                    Snapshot.State == PhoneControlState.Ready)
                {
                    return;
                }
            }

            await StopOwnedAsync().ConfigureAwait(false);
            Publish(new PhoneControlSnapshot(
                PhoneControlState.Starting,
                PhoneReasonCodes.ControlStarting,
                "正在建立独立手机控制通道",
                deviceKey,
                0,
                0,
                0));
            try
            {
                IAndroidControlSession session = await _sessions.OpenAsync(
                    deviceKey,
                    cancellationToken).ConfigureAwait(false);
                PhoneInputDispatcher dispatcher = new(session.SendAsync);
                lock (_stateGate)
                {
                    _session = session;
                    _dispatcher = dispatcher;
                    _turnOffScreen = _defaultScreenOff;
                    _guardApplied = false;
                    _guardFaulted = false;
                }

                Publish(new PhoneControlSnapshot(
                    PhoneControlState.Ready,
                    PhoneReasonCodes.ControlReady,
                    "手机控制已就绪",
                    session.DeviceKey,
                    0,
                    0,
                    0));
                _displayLifetime = new CancellationTokenSource();
                _displayTask = MaintainPhysicalScreenAsync(_displayLifetime.Token);
                WriteLog(
                    "control_pipeline",
                    PhoneReasonCodes.ControlReady,
                    "手机控制已就绪",
                    session.DeviceKey);
            }
            catch (Exception exception) when (exception is AndroidConnectionException or IOException)
            {
                Publish(new PhoneControlSnapshot(
                    PhoneControlState.Faulted,
                    PhoneReasonCodes.ControlStartFailed,
                    "手机控制通道启动失败",
                    deviceKey,
                    0,
                    0,
                    0));
                throw new PhoneControlServiceException(
                    PhoneReasonCodes.ControlStartFailed,
                    "手机控制通道启动失败",
                    exception);
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async ValueTask SendAsync(
        PhoneInputCommandKind kind,
        float normalizedX,
        float normalizedY,
        int screenWidth,
        int screenHeight,
        float scrollDelta,
        CancellationToken cancellationToken,
        int unlockDigit = -1)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        PhoneInputDispatcher? dispatcher;
        string? deviceKey;
        PhoneControlState state;
        lock (_stateGate)
        {
            dispatcher = _dispatcher;
            deviceKey = _session?.DeviceKey;
            state = Snapshot.State;
        }

        if (state == PhoneControlState.Faulted && !string.IsNullOrWhiteSpace(deviceKey))
        {
            await StartAsync(deviceKey, cancellationToken).ConfigureAwait(false);
            lock (_stateGate)
            {
                dispatcher = _dispatcher;
                deviceKey = _session?.DeviceKey;
            }
        }

        if (dispatcher is null || string.IsNullOrWhiteSpace(deviceKey))
        {
            throw new PhoneControlServiceException(
                PhoneReasonCodes.ControlNotReady,
                "手机控制尚未就绪");
        }

        bool positioned = kind is PhoneInputCommandKind.PointerDown or
            PhoneInputCommandKind.PointerMove or
            PhoneInputCommandKind.PointerUp or
            PhoneInputCommandKind.PointerCancel or
            PhoneInputCommandKind.Scroll;
        if (positioned && (screenWidth < 1 || screenHeight < 1))
        {
            throw new PhoneControlServiceException(
                PhoneReasonCodes.ControlVideoSizeMissing,
                "尚未获得手机画面尺寸，无法发送触控");
        }

        PhoneInputCommand command = new(
            Interlocked.Increment(ref _sequence),
            _turnOffScreen && kind == PhoneInputCommandKind.WakeScreen
                ? PhoneInputCommandKind.WakeScreenWhileDisplayOff : kind,
            _primaryPointerId,
            normalizedX,
            normalizedY,
            positioned ? screenWidth : 0,
            positioned ? screenHeight : 0,
            DateTimeOffset.UtcNow,
            scrollDelta,
            unlockDigit);
        try
        {
            await dispatcher.EnqueueAsync(command, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is AndroidConnectionException or IOException or InvalidOperationException)
        {
            PhoneInputDispatcherSnapshot metrics = dispatcher.Snapshot;
            Publish(new PhoneControlSnapshot(
                PhoneControlState.Faulted,
                PhoneReasonCodes.ControlSendFailed,
                "手机控制指令发送失败；下次操作会重新建立通道",
                deviceKey,
                metrics.SentCommands,
                metrics.ReplacedPointerMoves,
                metrics.LastQueueDelayMilliseconds));
            throw new PhoneControlServiceException(
                PhoneReasonCodes.ControlSendFailed,
                "手机控制指令发送失败",
                exception);
        }

        if (kind != PhoneInputCommandKind.PointerMove)
        {
            PhoneInputDispatcherSnapshot metrics = dispatcher.Snapshot;
            Publish(new PhoneControlSnapshot(
                PhoneControlState.Ready,
                PhoneReasonCodes.ControlCommandSent,
                $"手机操作已发送：{CommandName(kind)}",
                deviceKey,
                metrics.SentCommands,
                metrics.ReplacedPointerMoves,
                metrics.LastQueueDelayMilliseconds));
        }
    }

    public void QueueFromSteamVr(
        PhoneInputCommandKind kind,
        float normalizedX,
        float normalizedY,
        int screenWidth,
        int screenHeight,
        float scrollDelta = 0,
        int unlockDigit = -1)
    {
        if (kind == PhoneInputCommandKind.ToggleScreenGuard)
        {
            lock (_stateGate)
            {
                ScreenGuardRequests++;
                if (_pendingScreenGuardToggle) { CoalescedScreenGuardRequests++; }
                _pendingScreenGuardToggle = !_pendingScreenGuardToggle;
            }
            return;
        }
        ValueTask pending = SendAsync(
            kind,
            normalizedX,
            normalizedY,
            screenWidth,
            screenHeight,
            scrollDelta,
            CancellationToken.None,
            unlockDigit);
        if (!pending.IsCompletedSuccessfully)
        {
            _ = ObserveSteamVrCommandAsync(pending);
        }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }

        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Publish(Snapshot with
            {
                State = PhoneControlState.Stopping,
                ReasonCode = PhoneReasonCodes.ControlStopping,
                Message = "正在停止手机控制",
            });
            await StopOwnedAsync().ConfigureAwait(false);
            Publish(new PhoneControlSnapshot(
                PhoneControlState.Stopped,
                PhoneReasonCodes.ControlStopped,
                "手机控制已停止",
                null,
                0,
                0,
                0));
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _disposed = true;
        _lifecycle.Dispose();
    }

    private async ValueTask StopOwnedAsync()
    {
        if (_displayLifetime is not null)
        {
            await _displayLifetime.CancelAsync().ConfigureAwait(false);
            await _displayTask.ConfigureAwait(false);
            _displayLifetime.Dispose();
            _displayLifetime = null;
        }
        PhoneInputDispatcher? dispatcher;
        IAndroidControlSession? session;
        lock (_stateGate)
        {
            dispatcher = _dispatcher;
            session = _session;
            _dispatcher = null;
            _session = null;
            _guardApplied = false;
            _guardFaulted = false;
        }

        if (dispatcher is not null)
        {
            PhoneInputDispatcherSnapshot metrics = dispatcher.Snapshot;
            await dispatcher.DisposeAsync().ConfigureAwait(false);
            _log.TryWrite(new AndroidConnectionLogEntry(
                DateTimeOffset.UtcNow,
                "control_metrics",
                PhoneReasonCodes.ControlMetrics,
                $"手机控制统计；已发送 {metrics.SentCommands}；替换 MOVE {metrics.ReplacedPointerMoves}；" +
                $"最后排队 {metrics.LastQueueDelayMilliseconds:F1} ms",
                session?.DeviceKey,
                PacketCount: metrics.SentCommands,
                QueueDepth: metrics.ReliableQueueDepth,
                ReplacedMoveCount: metrics.ReplacedPointerMoves));
        }

        if (session is not null)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task MaintainPhysicalScreenAsync(CancellationToken cancellationToken)
    {
        bool appliedOff = false;
        long nextActivity = 0;
        try
        {
            using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(100));
            do
            {
                bool desiredOff = _turnOffScreen;
                if (desiredOff)
                {
                    // One awaited producer: at most one guard command queued/in flight,
                    // no catch-up burst when the socket or reliable input queue is busy.
                    // Activity refresh is not a wake request. A periodic wake would
                    // deliberately light the panel before turning it off again.
                    if (!appliedOff || Environment.TickCount64 >= nextActivity)
                    {
                        await SendDisplayCommandAsync(PhoneInputCommandKind.UserActivity, cancellationToken).ConfigureAwait(false);
                        nextActivity = Environment.TickCount64 + 2000;
                    }
                    await SendDisplayCommandAsync(PhoneInputCommandKind.MaintainDisplayOff, cancellationToken).ConfigureAwait(false);
                }
                else if (appliedOff)
                {
                    // The single producer has finished its last off command first.
                    await SendDisplayCommandAsync(PhoneInputCommandKind.WakeScreen, cancellationToken).ConfigureAwait(false);
                }
                if (appliedOff != desiredOff)
                {
                    appliedOff = desiredOff;
                    SetGuardState(desiredOff, false);
                }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) when (exception is PhoneControlServiceException or AndroidConnectionException or IOException or InvalidOperationException)
        {
            SetGuardState(false, true);
            Publish(Snapshot with { State = PhoneControlState.Faulted, ReasonCode = PhoneReasonCodes.ControlSendFailed });
            WriteLog("physical_screen", PhoneReasonCodes.ControlSendFailed,
                "实体屏幕控制中断，请重新连接手机浮窗", Snapshot.DeviceKey);
        }
    }

    private async ValueTask SendDisplayCommandAsync(PhoneInputCommandKind kind, CancellationToken cancellationToken)
    {
        PhoneInputDispatcher? dispatcher;
        lock (_stateGate) { dispatcher = _dispatcher; }
        if (dispatcher is null) { return; }
        await dispatcher.EnqueueAsync(new PhoneInputCommand(Interlocked.Increment(ref _sequence),
            kind, _primaryPointerId, 0, 0, 0, 0, DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
    }

    private static async Task ObserveSteamVrCommandAsync(ValueTask pending)
    {
        try
        {
            await pending.ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is PhoneControlServiceException or ObjectDisposedException)
        {
        }
    }

    private void SetGuardState(bool applied, bool faulted)
    {
        lock (_stateGate) { _guardApplied = applied; _guardFaulted = faulted; }
        Publish(Snapshot);
    }

    private void Publish(PhoneControlSnapshot snapshot)
    {
        lock (_stateGate)
        {
            snapshot = snapshot with { ScreenGuardEnabled = _guardApplied, ScreenGuardFaulted = _guardFaulted };
            Snapshot = snapshot;
            if (snapshot.SentCommands > 0 ||
                snapshot.ReplacedPointerMoves > 0 ||
                snapshot.LastQueueDelayMilliseconds > 0)
            {
                _diagnosticSnapshot = snapshot;
            }
        }

        StateChanged?.Invoke(this, new PhoneControlChangedEventArgs(snapshot));
    }

    private void WriteLog(
        string eventName,
        string reasonCode,
        string message,
        string? deviceKey)
    {
        _log.TryWrite(new AndroidConnectionLogEntry(
            DateTimeOffset.UtcNow,
            eventName,
            reasonCode,
            message,
            deviceKey));
    }

    private static string CommandName(PhoneInputCommandKind kind) => kind switch
    {
        PhoneInputCommandKind.PointerDown => "按下",
        PhoneInputCommandKind.PointerUp => "抬起",
        PhoneInputCommandKind.PointerCancel => "取消触控",
        PhoneInputCommandKind.Back => "返回",
        PhoneInputCommandKind.Home => "桌面",
        PhoneInputCommandKind.RecentApps => "最近任务",
        PhoneInputCommandKind.OpenControlPanel => "控制栏",
        PhoneInputCommandKind.Screenshot => "截屏",
        PhoneInputCommandKind.Scroll => "滚动",
        PhoneInputCommandKind.WakeScreen => "唤醒屏幕",
        PhoneInputCommandKind.UserActivity => "刷新屏幕活动",
        PhoneInputCommandKind.UnlockDigit => "安全数字输入",
        PhoneInputCommandKind.UnlockBackspace => "安全输入删除",
        PhoneInputCommandKind.UnlockConfirm => "安全输入确认",
        _ => kind.ToString(),
    };
}
