using System.Runtime.ExceptionServices;
using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.Session;

public enum PhoneMediaSessionState
{
    Stopped,
    Starting,
    Running,
    WaitingForDevice,
    PausingForScreenRestart,
    ScreenRestartPaused,
    ResumingAfterScreenRestart,
    Stopping,
    Faulted,
}

public sealed record PhoneMediaSessionSnapshot(
    PhoneMediaSessionState State,
    string ReasonCode,
    string Message);

public sealed class PhoneMediaSessionChangedEventArgs(PhoneMediaSessionSnapshot snapshot) : EventArgs
{
    public PhoneMediaSessionSnapshot Snapshot { get; } = snapshot;
}

public interface IPhoneMediaSessionCoordinator : IAsyncDisposable
{
    public bool CanStart(string? deviceKey);
    public PhoneMediaSessionSnapshot Snapshot { get; }

    public event EventHandler<PhoneMediaSessionChangedEventArgs>? StateChanged;

    public void ConfigureAutoStart(bool enabled, AndroidVideoOptions videoOptions);

    public void RequestAutomaticStart() { }

    public ValueTask StartAsync(
        string? deviceKey,
        AndroidVideoOptions videoOptions,
        CancellationToken cancellationToken);

    public ValueTask RestartScreenAsync(
        string? deviceKey,
        AndroidVideoOptions videoOptions,
        Func<CancellationToken, ValueTask>? whilePaused,
        CancellationToken cancellationToken);

    public ValueTask StopAsync(CancellationToken cancellationToken);

    public ValueTask StopForConnectionChangeAsync(CancellationToken cancellationToken) => StopAsync(cancellationToken);

    public ValueTask SwitchDeviceAsync(string deviceKey, Func<AndroidVideoOptions> videoOptions,
        CancellationToken cancellationToken);
}

public sealed class PhoneMediaSessionCoordinator : IPhoneMediaSessionCoordinator
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _stateGate = new();
    private readonly object _reconnectGate = new();
    private readonly CancellationTokenSource _disposeLifetime = new();
    private readonly IPhoneOverlayService _phoneOverlay;
    private readonly IPhoneAudioService _phoneAudio;
    private readonly IPhoneControlService _phoneControl;
    private readonly IAndroidMediaPlaybackControl _androidMediaPlayback;
    private readonly IAndroidConnectionService? _phoneConnection;
    private AndroidConnectionSnapshot? _lastConnectionSnapshot;
    private PhoneMediaSessionSnapshot _snapshot = new(
        PhoneMediaSessionState.Stopped,
        PhoneReasonCodes.MediaStopped,
        "手机媒体会话未运行");
    private AndroidMediaPauseLease? _pendingPhonePlaybackPause;
    private string? _activeDeviceKey;
    private string? _desiredDeviceKey;
    private AndroidVideoOptions? _desiredVideoOptions;
    private CancellationTokenSource? _reconnectLifetime;
    private Task _reconnectTask = Task.CompletedTask;
    private bool _desiredRunning;
    private bool _sessionActive;
    private bool _disposed;
    private readonly PhoneAutoStartPolicy _autoStart = new();
    private readonly Func<bool> _runtimeReady;
    private readonly Task _autoStartTask;
    private bool _autoStartEnabled;
    private AndroidVideoOptions _autoStartOptions = new();
    private readonly Func<string?, bool> _startReady;
    private CancellationTokenSource? _autoStartAttempt;

    public PhoneMediaSessionCoordinator(
        IPhoneOverlayService phoneOverlay,
        IPhoneAudioService phoneAudio,
        IPhoneControlService phoneControl,
        IAndroidMediaPlaybackControl androidMediaPlayback,
        IAndroidConnectionService? phoneConnection = null,
        Func<bool>? runtimeReady = null,
        Func<string?, bool>? startReady = null)
    {
        _phoneOverlay = phoneOverlay ?? throw new ArgumentNullException(nameof(phoneOverlay));
        _phoneAudio = phoneAudio ?? throw new ArgumentNullException(nameof(phoneAudio));
        _phoneControl = phoneControl ?? throw new ArgumentNullException(nameof(phoneControl));
        _androidMediaPlayback = androidMediaPlayback ??
            throw new ArgumentNullException(nameof(androidMediaPlayback));
        _phoneConnection = phoneConnection;
        _runtimeReady = runtimeReady ?? (() => false);
        _startReady = startReady ?? (deviceKey => PhoneStartReadiness.CanStart(
            _phoneConnection?.Snapshot, _runtimeReady(), deviceKey));
        if (_phoneConnection is not null)
        {
            _lastConnectionSnapshot = _phoneConnection.Snapshot;
            _phoneConnection.StateChanged += OnPhoneConnectionStateChanged;
        }
        _autoStartTask = phoneConnection is null ? Task.CompletedTask : AutoStartAsync(_disposeLifetime.Token);
    }

    public PhoneMediaSessionSnapshot Snapshot
    {
        get
        {
            lock (_stateGate)
            {
                return _snapshot;
            }
        }
    }

    public event EventHandler<PhoneMediaSessionChangedEventArgs>? StateChanged;

    public bool CanStart(string? deviceKey) => !_disposed && !_disposeLifetime.IsCancellationRequested && _startReady(deviceKey);

    private void EnsureCanStart(string? deviceKey)
    {
        if (!CanStart(deviceKey))
        {
            throw new PhoneOverlayServiceException(PhoneReasonCodes.MediaStartNotReady,
                "请等待 ADB、已授权手机和 SteamVR 全部就绪后再打开浮窗");
        }
    }

    public void ConfigureAutoStart(bool enabled, AndroidVideoOptions videoOptions)
    {
        CancellationTokenSource? cancel;
        lock (_stateGate)
        {
            _autoStartEnabled = enabled;
            _autoStartOptions = videoOptions;
            cancel = enabled ? null : _autoStartAttempt;
        }
        try { cancel?.Cancel(); }
        catch (ObjectDisposedException) { } // The owning attempt may have just completed.
    }

    public void RequestAutomaticStart()
    {
        // Explicit method selection ends a manual-close hold. Scans and saves do not.
        lock (_stateGate)
        {
            if (_snapshot.State is not (PhoneMediaSessionState.Running or PhoneMediaSessionState.Starting))
            { _autoStart.Reset(); }
        }
    }

    private async Task AutoStartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using PeriodicTimer timer = new(TimeSpan.FromSeconds(3));
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
                CancellationTokenSource? attempt = null;
                try
                {
                    AndroidConnectionSnapshot? connection = _phoneConnection?.Snapshot;
                    AndroidVideoOptions options;
                    lock (_stateGate)
                    {
                        if (!_autoStartEnabled || _snapshot.State is not (PhoneMediaSessionState.Stopped or PhoneMediaSessionState.Faulted)) { continue; }
                        if (!_autoStart.TryBegin(_autoStartEnabled, connection?.IsReady == true,
                            CanStart(connection?.SelectedDevice?.DeviceKey), true))
                        {
                            continue;
                        }
                        options = _autoStartOptions;
                        attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        _autoStartAttempt = attempt;
                    }

                    string? deviceKey = connection?.SelectedDevice?.DeviceKey;
                    await PrepareDeviceAsync(deviceKey).ConfigureAwait(false);
                    await StartOwnedAsync(deviceKey, options, attempt.Token).ConfigureAwait(false);
                    await ResumePendingPhoneMediaAsync(deviceKey, attempt.Token).ConfigureAwait(false);
                    EnsureCanStart(deviceKey);
                    lock (_stateGate)
                    {
                        attempt.Token.ThrowIfCancellationRequested();
                        if (!_autoStartEnabled) { throw new OperationCanceledException(); }
                        // Only a successfully opened session owns reconnect intent.
                        _desiredRunning = true;
                        _desiredDeviceKey = deviceKey;
                        _desiredVideoOptions = options;
                        _autoStart.Suppress();
                    }
                    _activeDeviceKey = deviceKey;
                    _sessionActive = true;
                    PublishRunning();
                }
                catch (Exception exception)
                {
                    bool cancelled = exception is OperationCanceledException &&
                        (cancellationToken.IsCancellationRequested || attempt?.IsCancellationRequested == true);
                    ClearDesiredRunning();
                    try
                    {
                        await StopOwnedChannelsAsync(CancellationToken.None).ConfigureAwait(false);
                        Publish(new PhoneMediaSessionSnapshot(cancelled ? PhoneMediaSessionState.Stopped : PhoneMediaSessionState.Faulted,
                            cancelled ? PhoneReasonCodes.MediaStopped : PhoneReasonCodes.MediaAutoStartFailed,
                            cancelled ? "自动打开已取消" : "自动打开浮窗失败；最多尝试三次，也可手动打开"));
                    }
                    catch (Exception)
                    {
                        lock (_stateGate) { _autoStart.Suppress(); }
                        Publish(new PhoneMediaSessionSnapshot(PhoneMediaSessionState.Faulted,
                            PhoneReasonCodes.MediaAutoStartCleanupFailed, "自动打开失败且清理未完成，请重启软件后重试"));
                    }
                }
                finally
                {
                    lock (_stateGate) { if (ReferenceEquals(_autoStartAttempt, attempt)) { _autoStartAttempt = null; } }
                    attempt?.Dispose();
                    _lifecycle.Release();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    public async ValueTask StartAsync(
        string? deviceKey,
        AndroidVideoOptions videoOptions,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(videoOptions);
        EnsureCanStart(deviceKey);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_stateGate) { _autoStart.Suppress(); }
            if (_sessionActive && _activeDeviceKey == deviceKey && Snapshot.State == PhoneMediaSessionState.Running) { return; }
            SetDesiredRunning(deviceKey, videoOptions);
            await PrepareDeviceAsync(deviceKey).ConfigureAwait(false);
            await StartOwnedAsync(deviceKey, videoOptions, cancellationToken).ConfigureAwait(false);
            await ResumePendingPhoneMediaAsync(deviceKey, cancellationToken).ConfigureAwait(false);
            _activeDeviceKey = deviceKey;
            _sessionActive = true;
            PublishRunning();
        }
        catch (Exception)
        {
            try
            {
                await StopOwnedChannelsAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    ClearDesiredRunning();
                }

                Publish(new PhoneMediaSessionSnapshot(
                    PhoneMediaSessionState.Faulted,
                    PhoneReasonCodes.MediaStartFailed,
                    "手机媒体会话启动失败"));
            }

            throw;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async ValueTask RestartScreenAsync(
        string? deviceKey,
        AndroidVideoOptions videoOptions,
        Func<CancellationToken, ValueTask>? whilePaused,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(videoOptions);
        EnsureCanStart(deviceKey);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        bool restartedScreen = false;
        bool changingDevice = false;
        try
        {
            SetDesiredRunning(deviceKey, videoOptions);
            changingDevice = _activeDeviceKey is not null &&
                !string.Equals(_activeDeviceKey, deviceKey, StringComparison.Ordinal);
            await PrepareDeviceAsync(deviceKey).ConfigureAwait(false);
            Publish(new PhoneMediaSessionSnapshot(
                PhoneMediaSessionState.PausingForScreenRestart,
                PhoneReasonCodes.MediaPausingForScreenRestart,
                "正在暂停手机媒体并准备重启手机屏幕"));
            AndroidMediaPauseLease mediaPause = await EnsurePhonePlaybackPausedAsync(
                deviceKey,
                cancellationToken).ConfigureAwait(false);
            await _phoneOverlay.StopAsync(cancellationToken).ConfigureAwait(false);
            await _phoneControl.StopAsync(cancellationToken).ConfigureAwait(false);
            Publish(new PhoneMediaSessionSnapshot(
                PhoneMediaSessionState.ScreenRestartPaused,
                PhoneReasonCodes.MediaScreenRestartPaused,
                "手机媒体已暂停，正在重启手机屏幕"));

            if (whilePaused is not null)
            {
                await whilePaused(cancellationToken).ConfigureAwait(false);
                await WaitForStartReadyAsync(deviceKey, cancellationToken).ConfigureAwait(false);
            }

            EnsureCanStart(deviceKey);
            Publish(new PhoneMediaSessionSnapshot(
                PhoneMediaSessionState.ResumingAfterScreenRestart,
                PhoneReasonCodes.MediaResumingAfterScreenRestart,
                "手机屏幕已重启，正在恢复手机媒体"));
            await _phoneOverlay.StartAsync(deviceKey, videoOptions, cancellationToken)
                .ConfigureAwait(false);
            restartedScreen = true;
            if (changingDevice)
            {
                await _phoneAudio.StartAsync(deviceKey, cancellationToken).ConfigureAwait(false);
            }

            await _phoneControl.StartAsync(deviceKey, cancellationToken).ConfigureAwait(false);
            await _androidMediaPlayback.ResumeAfterVrReadyAsync(
                mediaPause,
                cancellationToken).ConfigureAwait(false);
            _pendingPhonePlaybackPause = null;
            _activeDeviceKey = deviceKey;
            _sessionActive = true;
            PublishRunning();
        }
        catch (Exception)
        {
            try
            {
                if (changingDevice)
                {
                    await StopOwnedChannelsAsync(CancellationToken.None).ConfigureAwait(false);
                }
                else if (restartedScreen)
                {
                    try
                    {
                        await _phoneOverlay.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    finally
                    {
                        await _phoneControl.StopAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    ClearDesiredRunning();
                }

                Publish(new PhoneMediaSessionSnapshot(
                    PhoneMediaSessionState.Faulted,
                    PhoneReasonCodes.MediaScreenRestartFailed,
                    "手机屏幕重启失败，媒体播放保持暂停"));
            }

            throw;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async ValueTask SwitchDeviceAsync(string deviceKey, Func<AndroidVideoOptions> videoOptions,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceKey);
        ArgumentNullException.ThrowIfNull(videoOptions);
        if (_phoneConnection is null) { throw new InvalidOperationException("Device discovery is unavailable."); }
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (deviceKey == _phoneConnection.Snapshot.SelectedDevice?.DeviceKey) { return; }
            if (!_phoneConnection.Snapshot.Devices.Any(device => device.DeviceKey == deviceKey &&
                device.Status == AndroidDeviceStatus.Ready))
            {
                throw new PhoneOverlayServiceException(PhoneReasonCodes.MediaSwitchDeviceNotReady,
                    "所选 ADB 连接未就绪，请刷新后重新选择");
            }
            bool reopen = _sessionActive || IsDesiredRunning();
            ClearDesiredRunning();
            if (reopen) { lock (_stateGate) { _autoStart.Suppress(); } }
            Publish(new(PhoneMediaSessionState.Stopping, "MEDIA_DEVICE_SWITCH_STOPPING", "正在安全关闭旧连接"));
            try
            {
                if (_sessionActive)
                {
                    try { _ = await EnsurePhonePlaybackPausedAsync(_activeDeviceKey, cancellationToken).ConfigureAwait(false); }
                    finally { await StopOwnedChannelsAsync(CancellationToken.None).ConfigureAwait(false); }
                }
                else { await StopOwnedChannelsAsync(CancellationToken.None).ConfigureAwait(false); }
                await _phoneConnection.SelectDeviceAsync(deviceKey, cancellationToken).ConfigureAwait(false);
                if (!_phoneConnection.Snapshot.IsReady || _phoneConnection.Snapshot.SelectedDevice?.DeviceKey != deviceKey)
                {
                    throw new PhoneOverlayServiceException(PhoneReasonCodes.MediaSwitchDeviceUnavailable,
                        "所选 ADB 连接已不可用；旧浮窗已安全关闭，请重新选择连接");
                }
                DiscardOtherDevicePause(deviceKey);
                if (reopen)
                {
                    await WaitForStartReadyAsync(deviceKey, cancellationToken).ConfigureAwait(false);
                    AndroidVideoOptions options = videoOptions();
                    await StartOwnedAsync(deviceKey, options, cancellationToken).ConfigureAwait(false);
                    EnsureCanStart(deviceKey);
                    await ResumePendingPhoneMediaAsync(deviceKey, cancellationToken).ConfigureAwait(false);
                    _activeDeviceKey = deviceKey;
                    _sessionActive = true;
                    SetDesiredRunning(deviceKey, options);
                    PublishRunning();
                }
                else { Publish(new(PhoneMediaSessionState.Stopped, PhoneReasonCodes.MediaDeviceSelected, "已选择新的 ADB 连接")); }
            }
            catch
            {
                try { await StopOwnedChannelsAsync(CancellationToken.None).ConfigureAwait(false); }
                finally
                {
                    ClearDesiredRunning();
                    Publish(new(PhoneMediaSessionState.Faulted, "MEDIA_DEVICE_SWITCH_FAILED",
                        "切换 ADB 连接失败，浮窗已关闭，请刷新并重新打开"));
                }
                throw;
            }
        }
        finally { _lifecycle.Release(); }
    }

    public ValueTask StopAsync(CancellationToken cancellationToken) => StopCoreAsync(suppressAutoStart: true, cancellationToken);

    public ValueTask StopForConnectionChangeAsync(CancellationToken cancellationToken) => StopCoreAsync(suppressAutoStart: false, cancellationToken);

    private async ValueTask StopCoreAsync(bool suppressAutoStart, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (suppressAutoStart) { lock (_stateGate) { _autoStart.Suppress(); } }
        CancellationTokenSource? automaticAttempt;
        lock (_stateGate) { automaticAttempt = _autoStartAttempt; }
        try { if (automaticAttempt is not null) { await automaticAttempt.CancelAsync().ConfigureAwait(false); } }
        catch (ObjectDisposedException) { }
        ClearDesiredRunning();
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ClearDesiredRunning();
            Publish(new PhoneMediaSessionSnapshot(
                PhoneMediaSessionState.Stopping,
                PhoneReasonCodes.MediaStopping,
                "正在停止手机媒体会话"));
            Exception? phonePauseFailure = null;
            if (_sessionActive)
            {
                try
                {
                    _ = await EnsurePhonePlaybackPausedAsync(
                        _activeDeviceKey,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    phonePauseFailure = exception;
                }
            }

            // Once stop owns the lifecycle, cancellation must not abandon
            // already-running channels or their phone/device ownership.
            await StopOwnedChannelsAsync(CancellationToken.None).ConfigureAwait(false);
            Publish(new PhoneMediaSessionSnapshot(
                PhoneMediaSessionState.Stopped,
                PhoneReasonCodes.MediaStopped,
                phonePauseFailure is null
                    ? "手机媒体会话已停止，手机播放保持暂停"
                    : "手机媒体会话已停止，但暂停手机播放失败"));
            if (phonePauseFailure is not null)
            {
                ExceptionDispatchInfo.Capture(phonePauseFailure).Throw();
            }
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

        if (_phoneConnection is not null)
        {
            _phoneConnection.StateChanged -= OnPhoneConnectionStateChanged;
        }

        await _disposeLifetime.CancelAsync().ConfigureAwait(false);
        Task pendingReconnect = CancelReconnect();
        try
        {
            try
            {
                await Task.WhenAll(_autoStartTask, pendingReconnect).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // A faulted background task must never skip channel shutdown.
            }

            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (AndroidConnectionException)
        {
            // Shutdown has already released the owned audio, video, input and OpenVR resources.
            // A disconnected phone cannot be paused here and must not block the remaining cleanup.
        }
        finally
        {
            _disposed = true;
            await _disposeLifetime.CancelAsync().ConfigureAwait(false);
            _disposeLifetime.Dispose();
            _lifecycle.Dispose();
        }
    }

    private async ValueTask StartOwnedAsync(
        string? deviceKey,
        AndroidVideoOptions videoOptions,
        CancellationToken cancellationToken)
    {
        EnsureCanStart(deviceKey);
        Publish(new PhoneMediaSessionSnapshot(
            PhoneMediaSessionState.Starting,
            PhoneReasonCodes.MediaStarting,
            "正在启动手机媒体会话"));
        await _phoneOverlay.StartAsync(deviceKey, videoOptions, cancellationToken)
            .ConfigureAwait(false);
        await _phoneAudio.StartAsync(deviceKey, cancellationToken).ConfigureAwait(false);
        await _phoneControl.StartAsync(deviceKey, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WaitForStartReadyAsync(string? deviceKey, CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            while (!CanStart(deviceKey)) { await Task.Delay(100, deadline.Token).ConfigureAwait(false); }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PhoneOverlayServiceException(PhoneReasonCodes.MediaStartNotReady,
                "请等待 ADB、已授权手机和 SteamVR 全部就绪后再打开浮窗");
        }
    }

    private void PublishRunning() => Publish(new PhoneMediaSessionSnapshot(
        PhoneMediaSessionState.Running,
        PhoneReasonCodes.MediaRunning,
        "手机画面和音频正在播放"));

    private async ValueTask PrepareDeviceAsync(string? deviceKey)
    {
        DiscardOtherDevicePause(deviceKey);
        if (_activeDeviceKey is not null &&
            !string.Equals(_activeDeviceKey, deviceKey, StringComparison.Ordinal))
        {
            await StopOwnedChannelsAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private void DiscardOtherDevicePause(string? deviceKey)
    {
        if (_pendingPhonePlaybackPause is { } pending &&
            !string.Equals(pending.DeviceKey, deviceKey, StringComparison.Ordinal))
        {
            // A single session only owns one phone's pending playback. Changing
            // phones leaves the old phone paused and never sends it a later key.
            _pendingPhonePlaybackPause = null;
        }
    }

    private async ValueTask ResumePendingPhoneMediaAsync(
        string? deviceKey,
        CancellationToken cancellationToken)
    {
        DiscardOtherDevicePause(deviceKey);
        if (_pendingPhonePlaybackPause is not { } mediaPause)
        {
            return;
        }

        await _androidMediaPlayback.ResumeAfterVrReadyAsync(mediaPause, cancellationToken)
            .ConfigureAwait(false);
        _pendingPhonePlaybackPause = null;
    }

    private async ValueTask<AndroidMediaPauseLease> EnsurePhonePlaybackPausedAsync(
        string? deviceKey,
        CancellationToken cancellationToken)
    {
        DiscardOtherDevicePause(deviceKey);
        if (_pendingPhonePlaybackPause is { } pendingPause)
        {
            return pendingPause;
        }

        AndroidMediaPauseLease mediaPause = await _androidMediaPlayback
            .PauseForVrInterruptionAsync(deviceKey, cancellationToken)
            .ConfigureAwait(false);
        _pendingPhonePlaybackPause = mediaPause;
        return mediaPause;
    }

    private void OnPhoneConnectionStateChanged(
        object? sender,
        AndroidConnectionChangedEventArgs eventArgs)
    {
        if (_disposed)
        {
            return;
        }

        lock (_reconnectGate)
        {
            AndroidConnectionSnapshot connection = eventArgs.Snapshot;
            // Compare with accepted notifications, not the separately readable
            // live snapshot: it may already be Ready after an unseen disconnect.
            if (connection.Revision <= (_lastConnectionSnapshot?.Revision ?? -1))
            {
                return;
            }

            bool wasReadyForSameDevice = _lastConnectionSnapshot is { IsReady: true } previous &&
                string.Equals(previous.SelectedDevice?.DeviceKey,
                    connection.SelectedDevice?.DeviceKey, StringComparison.Ordinal);
            _lastConnectionSnapshot = connection;
            if (!connection.IsReady)
            {
                _ = CancelReconnect();
                if (IsDesiredRunning())
                {
                    Publish(new PhoneMediaSessionSnapshot(
                        PhoneMediaSessionState.WaitingForDevice,
                        PhoneReasonCodes.MediaWaitingForDevice,
                        "手机已断开，等待当前连接方式下的自动连接设备"));
                    (_, _, AndroidVideoOptions? waitingOptions) = ReadDesiredRunning();
                    if (waitingOptions is not null) { ScheduleReconnect(null, waitingOptions); }
                }

                return;
            }

            (bool desired, _, AndroidVideoOptions? options) = ReadDesiredRunning();
            if (!desired || options is null || connection.SelectedDevice?.DeviceKey is not { } selectedKey)
            {
                _ = CancelReconnect();
                return;
            }

            if (!wasReadyForSameDevice)
            {
                // Discovery has enforced transport and automatic eligibility.
                lock (_stateGate) { _desiredDeviceKey = selectedKey; }
                ScheduleReconnect(selectedKey, options);
            }
        }
    }

    private void ScheduleReconnect(string? deviceKey, AndroidVideoOptions videoOptions)
    {
        lock (_reconnectGate)
        {
            if (_reconnectLifetime is not null)
            {
                _ = CancelAndDisposeAsync(_reconnectLifetime);
            }

            _reconnectLifetime = CancellationTokenSource.CreateLinkedTokenSource(
                _disposeLifetime.Token);
            _reconnectTask = RecoverAfterReconnectAsync(
                deviceKey,
                videoOptions,
                _reconnectLifetime.Token);
        }
    }

    private async Task RecoverAfterReconnectAsync(
        string? deviceKey,
        AndroidVideoOptions videoOptions,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken)
                .ConfigureAwait(false);
            if (deviceKey is null)
            {
                await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (IsDesiredRunning() && _phoneConnection?.Snapshot.IsReady != true)
                    { await StopOwnedChannelsAsync(CancellationToken.None).ConfigureAwait(false); }
                }
                finally { _lifecycle.Release(); }
                return;
            }
            const int maximumAttempts = 3;
            for (int attempt = 1; attempt <= maximumAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!CanRecover(deviceKey))
                {
                    return;
                }

                try
                {
                    await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        if (!CanRecover(deviceKey))
                        {
                            return;
                        }

                        await WaitForStartReadyAsync(deviceKey, cancellationToken).ConfigureAwait(false);
                        Publish(new PhoneMediaSessionSnapshot(
                            PhoneMediaSessionState.Starting,
                            PhoneReasonCodes.MediaReconnecting,
                            attempt == 1
                                ? "手机已重新连接，正在自动恢复浮窗、音频和控制"
                                : $"正在重试自动恢复（{attempt}/{maximumAttempts}）"));
                        await StopOwnedChannelsAsync(cancellationToken)
                            .ConfigureAwait(false);
                        await StartOwnedAsync(deviceKey, videoOptions, cancellationToken)
                            .ConfigureAwait(false);
                        await ResumePendingPhoneMediaAsync(deviceKey, cancellationToken).ConfigureAwait(false);
                        _activeDeviceKey = deviceKey;
                        _sessionActive = true;
                        PublishRunning();
                        return;
                    }
                    catch
                    {
                        await StopOwnedChannelsAsync(CancellationToken.None).ConfigureAwait(false);
                        throw;
                    }
                    finally
                    {
                        _lifecycle.Release();
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception) when (
                    exception is AndroidConnectionException or PhoneOverlayServiceException or
                        PhoneControlServiceException or IOException or InvalidOperationException)
                {
                    if (attempt == maximumAttempts)
                    {
                        Publish(new PhoneMediaSessionSnapshot(
                            PhoneMediaSessionState.Faulted,
                            PhoneReasonCodes.MediaReconnectFailed,
                            "手机重新连接后自动恢复失败，请手动重新打开浮窗"));
                        return;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            Publish(new PhoneMediaSessionSnapshot(
                PhoneMediaSessionState.Faulted,
                PhoneReasonCodes.MediaReconnectUnexpected,
                "手机重连恢复遇到未预期错误，请手动重新打开浮窗"));
        }
    }

    private async ValueTask StopOwnedChannelsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _phoneAudio.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await _phoneOverlay.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    await _phoneControl.StopAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _sessionActive = false;
                    _activeDeviceKey = null;
                }
            }
        }
    }

    private bool CanRecover(string deviceKey)
    {
        (bool desired, string? desiredDeviceKey, _) = ReadDesiredRunning();
        return desired &&
            string.Equals(desiredDeviceKey, deviceKey, StringComparison.Ordinal) &&
            _phoneConnection?.Snapshot is { IsReady: true } connection &&
            string.Equals(
                connection.SelectedDevice?.DeviceKey,
                deviceKey,
                StringComparison.Ordinal);
    }

    private void SetDesiredRunning(string? deviceKey, AndroidVideoOptions videoOptions)
    {
        _ = CancelReconnect();
        lock (_stateGate)
        {
            _desiredRunning = true;
            _desiredDeviceKey = deviceKey;
            _desiredVideoOptions = videoOptions;
        }
    }

    private void ClearDesiredRunning()
    {
        lock (_stateGate)
        {
            _desiredRunning = false;
            _desiredDeviceKey = null;
            _desiredVideoOptions = null;
        }

        _ = CancelReconnect();
    }

    private bool IsDesiredRunning()
    {
        lock (_stateGate)
        {
            return _desiredRunning;
        }
    }

    private (bool Desired, string? DeviceKey, AndroidVideoOptions? Options) ReadDesiredRunning()
    {
        lock (_stateGate)
        {
            return (_desiredRunning, _desiredDeviceKey, _desiredVideoOptions);
        }
    }

    private Task CancelReconnect()
    {
        lock (_reconnectGate)
        {
            if (_reconnectLifetime is not null)
            {
                _ = CancelAndDisposeAsync(_reconnectLifetime);
            }

            _reconnectLifetime = null;
            Task pending = _reconnectTask;
            _reconnectTask = Task.CompletedTask;
            return pending;
        }
    }

    private static async Task CancelAndDisposeAsync(CancellationTokenSource source)
    {
        await source.CancelAsync().ConfigureAwait(false);
        source.Dispose();
    }

    private void Publish(PhoneMediaSessionSnapshot snapshot)
    {
        lock (_stateGate)
        {
            _snapshot = snapshot;
        }

        StateChanged?.Invoke(this, new PhoneMediaSessionChangedEventArgs(snapshot));
    }
}
