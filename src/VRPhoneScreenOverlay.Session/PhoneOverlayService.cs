using System.Diagnostics;
using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.Media;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Session;

public enum PhoneOverlayState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Faulted,
}

public sealed record PhoneOverlaySnapshot(
    PhoneOverlayState State,
    string ReasonCode,
    string Message,
    int Width = 0,
    int Height = 0,
    long SubmittedFrames = 0,
    long DroppedFrames = 0,
    double FramesPerSecond = 0,
    bool SteamVrInputReady = false,
    bool WorldAnchored = false,
    bool ControllerHovered = false,
    bool OverlayGrabbed = false,
    bool UnlockKeypadExpanded = false,
    string InputReasonCode = OpenVrReasonCodes.InputStopped,
    OpenVrBindingHealthState BindingState = OpenVrBindingHealthState.Stopped,
    bool ShowBindingNotice = false,
    string BindingReasonCode = OpenVrReasonCodes.BindingStopped,
    string BindingMessage = "SteamVR 手柄绑定未运行",
    int RequestedResolutionPercent = 100,
    int RequestedBitrateMbps = 16,
    int RequestedMaximumFramesPerSecond = 60,
    double AverageBitrateMbps = 0,
    double PeakBitrateMbps = 0,
    double AverageLatencyMilliseconds = 0,
    string? VideoDecoderBackend = null,
    string? GraphicsAdapterBackend = null,
    long OpenVrGraphicsAdapterLuid = 0,
    string? HeadsetModel = null,
    string? ControllerType = null,
    bool PointerPoseBound = false,
    bool PointerPoseActive = false,
    bool TouchBound = false,
    bool TouchActive = false,
    bool GrabBound = false,
    bool GrabActive = false,
    bool ScaleBound = false,
    bool ScaleActive = false);

public sealed class PhoneOverlayChangedEventArgs(PhoneOverlaySnapshot snapshot) : EventArgs
{
    public PhoneOverlaySnapshot Snapshot { get; } = snapshot;
}

public interface IPhoneOverlayService : IAsyncDisposable
{
    public ValueTask SwitchControllerHandAsync(OpenVrControllerHand hand, CancellationToken cancellationToken);
    public OpenVrBindingResult ConfigureSteamVrAutoLaunch(bool enabled);
    public PhoneOverlaySnapshot Snapshot { get; }

    public PhoneOverlaySnapshot DiagnosticSnapshot { get; }

    public event EventHandler<PhoneOverlayChangedEventArgs>? StateChanged;

    public void ConfigureLockScreenFeatures(
        bool keepAwakeWhileGrabbed,
        bool unlockKeypadEnabled);

    public ValueTask StartAsync(
        string? deviceKey,
        AndroidVideoOptions options,
        CancellationToken cancellationToken);

    public OpenVrBindingResult OpenBindingUi();
    public ValueTask<OpenVrBindingResult> OpenBindingUiAsync(CancellationToken token) => ValueTask.FromResult(OpenBindingUi());

    public bool HasSavedBindingChanged();

    public OpenVrBindingResult ReloadLocalBinding();

    public ValueTask StopAsync(CancellationToken cancellationToken);
}

public sealed class PhoneOverlayService(
    IAndroidVideoSessionFactory videoSessions,
    IPhoneControlService phoneControl,
    IAndroidKeyguardStateService keyguard,
    IAndroidConnectionLogSink log,
    IPhoneVideoPipelineFactory? pipelineFactory = null) : IPhoneOverlayService
{
    private readonly IPhoneVideoPipelineFactory _pipelineFactory = pipelineFactory ?? new PhoneVideoPipelineFactory();
    private readonly object _gate = new();
    private readonly IAndroidVideoSessionFactory _videoSessions = videoSessions;
    private readonly IPhoneControlService _phoneControl = phoneControl;
    private readonly PhoneKeyguardMonitor _keyguardMonitor = new(keyguard, log);
    private readonly IAndroidConnectionLogSink _log = log;
    private PhoneOverlaySnapshot _snapshot = new(
        PhoneOverlayState.Stopped,
        PhoneReasonCodes.VideoOverlayNotStarted,
        "手机浮窗未运行");
    private PhoneOverlaySnapshot _diagnosticSnapshot = new(
        PhoneOverlayState.Stopped,
        PhoneReasonCodes.VideoOverlayNotStarted,
        "手机浮窗未运行");
    private CancellationTokenSource? _runLifetime;
    private Task? _runTask;
    private IOpenVrSceneOverlay? _activeOverlay;
    private bool _keepAwakeWhileGrabbed;
    private volatile bool _unlockKeypadEnabled;

    public async ValueTask SwitchControllerHandAsync(OpenVrControllerHand hand, CancellationToken cancellationToken)
    {
        IOpenVrSceneOverlay? overlay;
        lock (_gate)
        {
            overlay = _activeOverlay;
            if (overlay is null && _snapshot.State == PhoneOverlayState.Starting)
            {
                throw new PhoneOverlayServiceException(PhoneReasonCodes.HandSwitchStarting, "浮窗正在启动，请就绪后再切换惯用手");
            }
        }
        if (overlay is null) { return; }
        try { await overlay.SwitchControllerHandAsync(hand, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException)
        {
            throw new PhoneOverlayServiceException(PhoneReasonCodes.HandSwitchFailed, "惯用手切换未完成，已尝试恢复原设置，请重试", exception);
        }
    }

    public OpenVrBindingResult ConfigureSteamVrAutoLaunch(bool enabled) =>
        OpenVrStartupRegistration.Configure(enabled);

    public PhoneOverlaySnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public PhoneOverlaySnapshot DiagnosticSnapshot
    {
        get
        {
            lock (_gate)
            {
                return _diagnosticSnapshot;
            }
        }
    }

    public event EventHandler<PhoneOverlayChangedEventArgs>? StateChanged;

    public void ConfigureLockScreenFeatures(
        bool keepAwakeWhileGrabbed,
        bool unlockKeypadEnabled)
    {
        IOpenVrSceneOverlay? overlay;
        lock (_gate)
        {
            _keepAwakeWhileGrabbed = keepAwakeWhileGrabbed;
            _unlockKeypadEnabled = unlockKeypadEnabled;
            overlay = _activeOverlay;
        }

        try
        {
            overlay?.ConfigureLockScreenFeatures(
                keepAwakeWhileGrabbed,
                unlockKeypadEnabled);
        }
        catch (ObjectDisposedException)
        {
        }

    }

    public bool HasSavedBindingChanged() => OpenVrBindingRecovery.HasSavedBindingChanged();

    public OpenVrBindingResult OpenBindingUi()
    {
        IOpenVrSceneOverlay? overlay;
        lock (_gate)
        {
            overlay = _activeOverlay;
        }

        if (overlay is null)
        {
            return OpenVrBindingUiLauncher.Open();
        }

        try
        {
            return overlay.OpenBindingUi();
        }
        catch (ObjectDisposedException)
        {
            return new OpenVrBindingResult(
                false,
                OpenVrReasonCodes.InputStopping,
                "SteamVR 手机浮窗正在关闭，请重新打开后再进入手柄绑定页面");
        }
    }

    public async ValueTask<OpenVrBindingResult> OpenBindingUiAsync(CancellationToken token)
    {
        OpenVrBindingResult prepared = await Task.Run(OpenVrBindingUiLauncher.Prepare, token).ConfigureAwait(false);
        if (!prepared.Succeeded) { return prepared; }
        return await OpenVrBindingEditorNavigation.NavigateAsync(token).ConfigureAwait(false);
    }

    public OpenVrBindingResult ReloadLocalBinding()
    {
        bool overlayActive;
        lock (_gate)
        {
            overlayActive = _activeOverlay is not null ||
                _runTask is { IsCompleted: false };
        }

        if (overlayActive)
        {
            return new OpenVrBindingResult(
                false,
                OpenVrReasonCodes.BindingOverlayActive,
                "请先完整关闭 SteamVR 手机浮窗，再重新加载本地绑定");
        }

        OpenVrBindingResult result = OpenVrBindingRecovery.PrepareLocalBinding();
        _log.TryWrite(new AndroidConnectionLogEntry(
            DateTimeOffset.UtcNow,
            "steamvr_binding_recovery",
            result.ReasonCode,
            result.Message));
        return result;
    }

    public async ValueTask StartAsync(
        string? deviceKey,
        AndroidVideoOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_runTask is { IsCompleted: false })
            {
                if (_snapshot.State == PhoneOverlayState.Running)
                {
                    return;
                }

                throw new PhoneOverlayServiceException(
                    PhoneReasonCodes.VideoOverlayAlreadyStarting,
                    "手机浮窗正在启动");
            }

            _runLifetime?.Dispose();
            CancellationTokenSource lifetime = new();
            _runLifetime = lifetime;
            _runTask = Task.Run(
                () => RunAsync(deviceKey, options, started, lifetime.Token),
                CancellationToken.None);
        }

        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(35), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await StopAsync(CancellationToken.None);
            throw;
        }
        catch (TimeoutException exception)
        {
            await StopAsync(CancellationToken.None);
            throw new PhoneOverlayServiceException(
                PhoneReasonCodes.VideoOverlayStartTimeout,
                "35 秒内没有在 SteamVR 中显示手机画面",
                exception);
        }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CancellationTokenSource? lifetime;
        Task? runTask;
        lock (_gate)
        {
            lifetime = _runLifetime;
            runTask = _runTask;
        }

        if (runTask is null)
        {
            Publish(new PhoneOverlaySnapshot(
                PhoneOverlayState.Stopped,
                PhoneReasonCodes.VideoOverlayNotStarted,
                "手机浮窗未运行"));
            return;
        }

        Publish(Snapshot with
        {
            State = PhoneOverlayState.Stopping,
            ReasonCode = PhoneReasonCodes.VideoOverlayStopping,
            Message = "正在关闭手机浮窗",
        });
        if (lifetime is not null)
        {
            await lifetime.CancelAsync();
        }
        try
        {
            // Once shutdown has been signaled, ownership cannot be released until the worker has
            // actually disposed the video, decoder, GPU and OpenVR resources. Detaching here on a
            // caller cancellation would allow a second pipeline to start beside the first one.
            await runTask;
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_runTask, runTask))
                {
                    _runTask = null;
                    _runLifetime?.Dispose();
                    _runLifetime = null;
                }
            }

            Publish(new PhoneOverlaySnapshot(
                PhoneOverlayState.Stopped,
                PhoneReasonCodes.VideoOverlayStopped,
                "手机浮窗已关闭"));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        OpenVrBindingUiLauncher.Shutdown();
    }

    private async Task RunAsync(
        string? deviceKey,
        AndroidVideoOptions options,
        TaskCompletionSource started,
        CancellationToken cancellationToken)
    {
        Publish(new PhoneOverlaySnapshot(
            PhoneOverlayState.Starting,
            PhoneReasonCodes.VideoOverlayStarting,
            "正在连接手机视频并创建 SteamVR 浮窗"));
        _log.TryWrite(new AndroidConnectionLogEntry(
            DateTimeOffset.UtcNow,
            "video_overlay",
            PhoneReasonCodes.VideoOverlayStarting,
            "正在连接手机视频并创建 SteamVR 浮窗",
            deviceKey));

        try
        {
            await using IAndroidVideoSession session = await _videoSessions.OpenAsync(
                deviceKey,
                options,
                cancellationToken);
            if (session.Codec != AndroidVideoCodec.H264)
            {
                throw new PhoneOverlayServiceException(
                    PhoneReasonCodes.VideoOverlayCodecUnsupported,
                    $"当前视频编码 {session.Codec} 尚未接入浮窗播放");
            }

            int width = 0;
            int height = 0;
            int generation = -1;
            PhoneOverlayMetrics metrics = new();
            PhoneOverlayLogGate logGate = new();
            Stopwatch rateClock = Stopwatch.StartNew();
            Stopwatch sessionClock = Stopwatch.StartNew();
            IH264VideoDecoder? decoder = null;
            IGpuVideoFramePresenter? presenter = null;
            IOpenVrSceneOverlay? overlay = null;
            using CancellationTokenSource keyguardLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task keyguardTask = Task.CompletedTask;
            try
            {
                overlay = _pipelineFactory.CreateOverlay();
                overlay.PhoneInputReceived += OnPhoneInputReceived;
                _phoneControl.StateChanged += OnScreenGuardChanged;
                lock (_gate)
                {
                    _activeOverlay = overlay;
                    overlay.ConfigureLockScreenFeatures(
                        _keepAwakeWhileGrabbed,
                        _unlockKeypadEnabled);
                }

                await using LatestAndroidVideoFrameReader latestFrames = new(
                    session,
                    cancellationToken);
                keyguardTask = _keyguardMonitor.RunAsync(deviceKey, () => _unlockKeypadEnabled,
                    overlay.SetPhoneLocked, keyguardLifetime.Token);
                while (await latestFrames.ReadLatestAsync(cancellationToken) is { } frame)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using (frame)
                    {
                        if (generation != frame.Generation)
                        {
                            bool dimensionsChanged = width > 0 && height > 0 &&
                                (width != frame.Width || height != frame.Height);
                            generation = frame.Generation;
                            width = frame.Width;
                            height = frame.Height;
                            decoder?.Dispose();
                            decoder = _pipelineFactory.CreateDecoder(width, height, frame.Configuration.Span);
                            if (dimensionsChanged)
                            {
                                Publish(Snapshot with
                                {
                                    State = PhoneOverlayState.Starting,
                                    ReasonCode = PhoneReasonCodes.VideoOverlayReconfiguring,
                                    Message = $"手机方向变化，正在切换到 {width}×{height}",
                                });
                                _log.TryWrite(new AndroidConnectionLogEntry(
                                    DateTimeOffset.UtcNow,
                                    "video_overlay",
                                    PhoneReasonCodes.VideoOverlayReconfiguring,
                                    "检测到手机画面尺寸变化，正在重建解码阶段",
                                    session.DeviceKey,
                                    VideoWidth: width,
                                    VideoHeight: height,
                                    PacketCount: metrics.SubmittedFrames));
                            }
                        }

                        IH264VideoDecoder activeDecoder = decoder ?? throw new InvalidOperationException(
                            "Video decoder was not initialized for the current stream generation.");
                        using DecodedVideoFrame? decoded = activeDecoder.DecodePacket(
                            frame.Payload.Span,
                            frame.PresentationTimeMicroseconds,
                            frame.IsKeyFrame);
                        if (decoded is null)
                        {
                            continue;
                        }

                        presenter ??= _pipelineFactory.CreatePresenter(
                            overlay.GraphicsAdapterLuid);
                        GpuVideoFrame gpuFrame = presenter.Present(decoded);
                        overlay.Submit(gpuFrame);
                        metrics.RecordSubmittedFrame(
                            frame.EstimatedCaptureTimestamp is long captureTimestamp
                                ? Stopwatch.GetElapsedTime(captureTimestamp, Stopwatch.GetTimestamp())
                                    .TotalMilliseconds
                                : null);
                        if (metrics.TrySampleThroughput(
                            rateClock.Elapsed,
                            sessionClock.Elapsed,
                            latestFrames.EncodedPayloadBytes))
                        {
                            rateClock.Restart();
                        }

                        OpenVrPhoneInteractionSnapshot interaction = overlay.Interaction;
                        OpenVrBindingHealthSnapshot bindingHealth = overlay.BindingHealth;
                        if (logGate.ShouldLogInteraction(interaction))
                        {
                            _log.TryWrite(new AndroidConnectionLogEntry(
                                DateTimeOffset.UtcNow,
                                "steamvr_phone_input",
                                interaction.ReasonCode,
                                $"{interaction.Message}；世界固定={interaction.WorldAnchored}；" +
                                $"抓取={interaction.Grabbed}；命中={interaction.Hovered}；" +
                                $"活动手柄姿态有效={interaction.ControllerPoseValid}；" +
                                "射线来源=程序有限矩形求交",
                            session.DeviceKey,
                            VideoWidth: gpuFrame.Width,
                            VideoHeight: gpuFrame.Height,
                            ExceptionType: interaction.DiagnosticExceptionType,
                            ExceptionMessage: interaction.DiagnosticExceptionMessage));
                        }

                        if (logGate.ShouldLogBindingHealth(bindingHealth.ReasonCode))
                        {
                            _log.TryWrite(new AndroidConnectionLogEntry(
                                DateTimeOffset.UtcNow,
                                "steamvr_binding",
                                bindingHealth.ReasonCode,
                                bindingHealth.Message,
                                session.DeviceKey));
                        }

                        PhoneOverlaySnapshot running = new(
                            PhoneOverlayState.Running,
                            PhoneReasonCodes.VideoOverlayRunning,
                            "手机画面正在 SteamVR 中播放",
                            gpuFrame.Width,
                            gpuFrame.Height,
                            metrics.SubmittedFrames,
                            latestFrames.DroppedFrames,
                            metrics.FramesPerSecond,
                            interaction.InputReady,
                            interaction.WorldAnchored,
                            interaction.Hovered,
                            interaction.Grabbed,
                            interaction.UnlockKeypadExpanded,
                            interaction.ReasonCode,
                            bindingHealth.State,
                            bindingHealth.ShowNotice,
                            bindingHealth.ReasonCode,
                            bindingHealth.Message,
                            options.ResolutionPercent,
                            options.VideoBitrateBitsPerSecond / 1_000_000,
                            options.MaximumFramesPerSecond,
                            metrics.AverageBitrateMbps,
                            metrics.PeakBitrateMbps,
                            metrics.AverageLatencyMilliseconds,
                            activeDecoder.BackendName,
                            presenter.AdapterBackend,
                            overlay.GraphicsAdapterLuid,
                            interaction.HeadsetModel,
                            interaction.ControllerType,
                            interaction.PointerPoseBound,
                            interaction.PointerPoseActive,
                            interaction.TouchBound,
                            interaction.TouchActive,
                            interaction.GrabBound,
                            interaction.GrabActive,
                            interaction.ScaleBound,
                            interaction.ScaleActive);
                        Publish(running);
                        if (logGate.ShouldLogSubmittedSize(gpuFrame.Width, gpuFrame.Height))
                        {
                            _log.TryWrite(new AndroidConnectionLogEntry(
                                DateTimeOffset.UtcNow,
                                "video_overlay",
                                PhoneReasonCodes.VideoOverlayRunning,
                                $"手机视频已提交 SteamVR；可见 {gpuFrame.Width}x{gpuFrame.Height}；" +
                                $"解码缓冲 {decoded.Width}x{decoded.Height}；步幅 {decoded.Stride}",
                                session.DeviceKey,
                                VideoWidth: gpuFrame.Width,
                                VideoHeight: gpuFrame.Height,
                                PacketCount: metrics.SubmittedFrames));
                        }

                        if (logGate.ShouldLogQualitySample(
                            sessionClock.Elapsed,
                            TimeSpan.FromSeconds(5)))
                        {
                            _log.TryWrite(new AndroidConnectionLogEntry(
                                DateTimeOffset.UtcNow,
                                "video_quality",
                                PhoneReasonCodes.VideoQualitySample,
                                $"请求 {options.ResolutionPercent}% / " +
                                $"{options.VideoBitrateBitsPerSecond / 1_000_000} Mbps / " +
                                $"{options.MaximumFramesPerSecond} FPS；实际平均 " +
                                $"{metrics.AverageBitrateMbps:F2} Mbps，峰值 " +
                                $"{metrics.PeakBitrateMbps:F2} Mbps，" +
                                $"提交 {metrics.FramesPerSecond:F1} FPS，端到端平均延迟 " +
                                (metrics.AverageLatencyMilliseconds > 0
                                    ? $"{metrics.AverageLatencyMilliseconds:F0} ms"
                                    : "不可用"),
                                session.DeviceKey,
                                VideoWidth: gpuFrame.Width,
                                VideoHeight: gpuFrame.Height,
                                PacketCount: metrics.SubmittedFrames,
                                PayloadBytes: metrics.TotalEncodedBytes));
                        }
                    }

                    started.TrySetResult();
                }
            }
            finally
            {
                await keyguardLifetime.CancelAsync().ConfigureAwait(false);
                try { await keyguardTask.ConfigureAwait(false); }
                catch (OperationCanceledException) when (keyguardLifetime.IsCancellationRequested) { }
                if (overlay is not null)
                {
                    _phoneControl.StateChanged -= OnScreenGuardChanged;
                    overlay.PhoneInputReceived -= OnPhoneInputReceived;
                    lock (_gate)
                    {
                        if (ReferenceEquals(_activeOverlay, overlay))
                        {
                            _activeOverlay = null;
                        }
                    }

                    overlay.Dispose();
                }

                presenter?.Dispose();
                decoder?.Dispose();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            started.TrySetCanceled(cancellationToken);
        }
        catch (Exception exception)
        {
            PhoneOverlayServiceException failure = MapFailure(exception);
            Publish(new PhoneOverlaySnapshot(
                PhoneOverlayState.Faulted,
                failure.ReasonCode,
                failure.Message));
            started.TrySetException(failure);
            _log.TryWrite(new AndroidConnectionLogEntry(
                DateTimeOffset.UtcNow,
                "video_overlay",
                failure.ReasonCode,
                failure.Message,
                deviceKey,
                ExceptionType: exception.GetType().FullName,
                ExceptionMessage: exception.Message));
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Publish(new PhoneOverlaySnapshot(
                    PhoneOverlayState.Stopped,
                    PhoneReasonCodes.VideoOverlayStopped,
                    "手机浮窗已关闭"));
                _log.TryWrite(new AndroidConnectionLogEntry(
                    DateTimeOffset.UtcNow,
                    "video_overlay",
                    PhoneReasonCodes.VideoOverlayStopped,
                    "手机浮窗已关闭",
                    deviceKey));
            }
        }
    }

    private void Publish(PhoneOverlaySnapshot snapshot)
    {
        bool meaningfulChange;
        lock (_gate)
        {
            meaningfulChange = _snapshot.State != snapshot.State ||
                _snapshot.ReasonCode != snapshot.ReasonCode ||
                _snapshot.Width != snapshot.Width ||
                _snapshot.Height != snapshot.Height ||
                _snapshot.SteamVrInputReady != snapshot.SteamVrInputReady ||
                _snapshot.WorldAnchored != snapshot.WorldAnchored ||
                _snapshot.ControllerHovered != snapshot.ControllerHovered ||
                _snapshot.OverlayGrabbed != snapshot.OverlayGrabbed ||
                _snapshot.UnlockKeypadExpanded != snapshot.UnlockKeypadExpanded ||
                _snapshot.InputReasonCode != snapshot.InputReasonCode ||
                _snapshot.BindingState != snapshot.BindingState ||
                _snapshot.ShowBindingNotice != snapshot.ShowBindingNotice ||
                _snapshot.BindingReasonCode != snapshot.BindingReasonCode ||
                _snapshot.RequestedResolutionPercent != snapshot.RequestedResolutionPercent ||
                _snapshot.RequestedBitrateMbps != snapshot.RequestedBitrateMbps ||
                _snapshot.RequestedMaximumFramesPerSecond !=
                    snapshot.RequestedMaximumFramesPerSecond ||
                snapshot.SubmittedFrames == 1 ||
                snapshot.SubmittedFrames / 60 != _snapshot.SubmittedFrames / 60;
            _snapshot = snapshot;
            if (snapshot.SubmittedFrames > 0 ||
                !string.IsNullOrWhiteSpace(snapshot.VideoDecoderBackend) ||
                !string.IsNullOrWhiteSpace(snapshot.ControllerType))
            {
                _diagnosticSnapshot = snapshot;
            }
        }

        if (meaningfulChange)
        {
            StateChanged?.Invoke(this, new PhoneOverlayChangedEventArgs(snapshot));
        }
    }

    private void OnScreenGuardChanged(object? sender, PhoneControlChangedEventArgs args)
    {
        IOpenVrSceneOverlay? overlay;
        lock (_gate) { overlay = _activeOverlay; }
        // Input callbacks acquire the session gate from the OpenVR thread. Never
        // hold that gate while acquiring the OpenVR API gate in the other direction.
        overlay?.SetScreenGuard(args.Snapshot.ScreenGuardEnabled, args.Snapshot.ScreenGuardFaulted);
    }

    private void OnPhoneInputReceived(OpenVrPhoneInputCommand command)
    {
        PhoneOverlaySnapshot snapshot = Snapshot;
        PhoneControlState controlState = _phoneControl.Snapshot.State;
        if (controlState != PhoneControlState.Ready &&
            !(controlState == PhoneControlState.Faulted && command.Kind == PhoneInputCommandKind.WakeScreen))
        {
            return;
        }

        bool positioned = command.Kind is PhoneInputCommandKind.PointerDown or
            PhoneInputCommandKind.PointerMove or
            PhoneInputCommandKind.PointerUp or
            PhoneInputCommandKind.PointerCancel or
            PhoneInputCommandKind.Scroll;
        if (positioned && (snapshot.Width < 1 || snapshot.Height < 1))
        {
            return;
        }

        _phoneControl.QueueFromSteamVr(
            command.Kind,
            command.NormalizedX,
            command.NormalizedY,
            positioned ? snapshot.Width : 0,
            positioned ? snapshot.Height : 0,
            command.ScrollDelta,
            command.UnlockDigit);
    }

    private static PhoneOverlayServiceException MapFailure(Exception exception) => exception switch
    {
        PhoneOverlayServiceException failure => failure,
        AndroidConnectionException failure => new(
            failure.ReasonCode,
            failure.Message,
            failure),
        MediaDecoderException failure => new(
            failure.ReasonCode,
            failure.Message,
            failure),
        GpuVideoPresenterException failure => new(
            failure.ReasonCode,
            failure.Message,
            failure),
        OpenVrOverlayException failure => new(
            failure.ReasonCode,
            failure.Message,
            failure),
        _ => new PhoneOverlayServiceException(
            PhoneReasonCodes.VideoOverlayUnexpectedFailure,
            "手机浮窗遇到未预期错误",
            exception),
    };
}

public sealed class PhoneOverlayServiceException(
    string reasonCode,
    string message,
    Exception? inner = null) : Exception(message, inner)
{
    public string ReasonCode { get; } = reasonCode;
}
