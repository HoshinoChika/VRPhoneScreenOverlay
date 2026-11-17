using System.Diagnostics;
using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.Media;
using VRPhoneScreenOverlay.Settings;
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
    bool ScaleActive = false)
{
    // The mean covers accepted samples over this session, not the latest frame.
    public long EncodedQueueBytes { get; init; }
    public long PeakEncodedQueueBytes { get; init; }
    public long EncodedQueueByteOverflows { get; init; }
    public VideoClockDiagnostic? VideoClock { get; init; }
    public VideoColorMetadata? VideoColor { get; init; }
    public VideoLatencyBreakdown? LatencyBreakdown { get; init; }
    public long LatencySampleCount { get; init; }
    public long UnavailableLatencySampleCount { get; init; }
    public long RejectedLatencySampleCount { get; init; }
    public bool HasLatencySamples => LatencySampleCount > 0 && double.IsFinite(AverageLatencyMilliseconds);
    public PhoneInputPathDiagnostic? InputPath { get; init; }
    public InputRoutingDiagnostic? InputRouting { get; init; }
    public long TotalEncodedBytes { get; init; }
    public VideoUploadDiagnostic? UploadDiagnostic { get; init; }
    public string PipelineStage { get; init; } = "unknown";
}

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
    IPhoneVideoPipelineFactory? pipelineFactory = null,
    IIncidentRecorder? incident = null, IVideoTrialStore? videoTrials = null,
    Func<VideoTrialContext>? videoTrialContext = null, VrMenuOverlayOwner? menuOwner = null) : IPhoneOverlayService
{
    private PhoneInputRoutingEvidence _inputRouting = new();
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
        if (menuOwner is not null)
        {
            await menuOwner.SwitchControllerHandAsync(hand, cancellationToken).ConfigureAwait(false);
            return;
        }
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

    private IGpuVideoFramePresenter? _diagnosticPresenter;
    private LatestAndroidVideoFrameReader? _diagnosticEncodedReader;
    private string _pipelinePhase = "idle";

    public PhoneOverlaySnapshot DiagnosticSnapshot
    {
        get
        {
            lock (_gate)
            {
                string phase = Volatile.Read(ref _pipelinePhase);
                return _diagnosticSnapshot with
                {
                    // Retain media evidence, but never replay its historical lifecycle state.
                    State = _snapshot.State,
                    ReasonCode = _snapshot.ReasonCode,
                    Message = _snapshot.Message,
                    UploadDiagnostic = Volatile.Read(ref _diagnosticPresenter)?.UploadDiagnostic ?? _diagnosticSnapshot.UploadDiagnostic,
                    PipelineStage = phase == "present" ? Volatile.Read(ref _diagnosticPresenter)?.DiagnosticStage ?? "present" : phase,
                    TotalEncodedBytes = Volatile.Read(ref _diagnosticEncodedReader)?.EncodedPayloadBytes ?? _diagnosticSnapshot.TotalEncodedBytes
                };
            }
        }
    }

    public event EventHandler<PhoneOverlayChangedEventArgs>? StateChanged;

    public void ConfigureLockScreenFeatures(
        bool keepAwakeWhileGrabbed,
        bool unlockKeypadEnabled)
    {
        menuOwner?.ConfigureLockScreenFeatures(keepAwakeWhileGrabbed, unlockKeypadEnabled);
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

        if (overlayActive || menuOwner?.IsReady == true)
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
        string attemptId = Guid.NewGuid().ToString("N");
        long attemptStarted = Stopwatch.GetTimestamp();
        Exception? primaryFailure = null;
        string? trialId = null;
        bool trialFailed = false;
        bool trialRequiresManualRetry = false;
        string trialEndReason = "VIDEO_STOPPED";
        long? firstSubmittedAt = null;
        bool stableRecorded = false;
        ValueTask RecordStageAsync(IncidentCheckpoint checkpoint, CancellationToken token) =>
            incident?.CheckpointAsync(checkpoint with
            {
                AttemptId = attemptId,
                AttemptElapsedMilliseconds = Stopwatch.GetElapsedTime(attemptStarted).TotalMilliseconds,
            }, token) ?? ValueTask.CompletedTask;
        ValueTask RecordFailureAsync(Exception exception, string kind) =>
            incident?.FailureAsync(exception, kind, attemptId, CancellationToken.None) ?? ValueTask.CompletedTask;

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
            if (videoTrials is not null)
            {
                VideoTrialContext context = videoTrialContext?.Invoke() ?? new(deviceKey ?? "unknown", 0, 0, "automatic");
                if (deviceKey is not null && deviceKey != context.DeviceKey) { context = context with { DeviceKey = deviceKey, NativeWidth = 0, NativeHeight = 0 }; }
                VideoParameters applied = new(options.ResolutionPercent, options.VideoBitrateBitsPerSecond / 1_000_000, options.MaximumFramesPerSecond);
                trialId = await videoTrials.BeginAsync(context, applied with
                { ResolutionPercent = options.RequestedResolutionPercent ?? options.ResolutionPercent }, applied, cancellationToken).ConfigureAwait(false);
                attemptId = trialId;
            }
            await RecordStageAsync(new("video-connect", "started",
                $"resolution={options.ResolutionPercent}; bitrate={options.VideoBitrateBitsPerSecond}; fps={options.MaximumFramesPerSecond}; maxSize={options.MaximumSize}"), cancellationToken).ConfigureAwait(false);
            await using IAndroidVideoSession session = await _videoSessions.OpenAsync(
                deviceKey,
                options,
                cancellationToken);
            await RecordStageAsync(new("video-connect", "succeeded"), cancellationToken).ConfigureAwait(false);
            if (session.Codec != AndroidVideoCodec.H264)
            {
                throw new PhoneOverlayServiceException(
                    PhoneReasonCodes.VideoOverlayCodecUnsupported,
                    $"当前视频编码 {session.Codec} 尚未接入浮窗播放");
            }

            int width = 0;
            int height = 0;
            int generation = -1;
            int submittedGeneration = -1;
            PhoneOverlayMetrics metrics = new();
            PhoneOverlayLogGate logGate = new();
            Stopwatch rateClock = Stopwatch.StartNew();
            Stopwatch sessionClock = Stopwatch.StartNew();
            LatestAndroidVideoFrameReader? latestFrames = null;
            LatestDecodedVideoFrameReader? decodedFrames = null;
            IGpuVideoFramePresenter? presenter = null;
            IOpenVrSceneOverlay? overlay = null;
            using CancellationTokenSource keyguardLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task keyguardTask = Task.CompletedTask;
            Task inputEvidenceTask = Task.CompletedTask;
            List<Exception> cleanupFailures = [];
            try
            {
                await RecordStageAsync(new("overlay-create", "started"), cancellationToken).ConfigureAwait(false);
                overlay = _pipelineFactory.CreateOverlay();
                await RecordStageAsync(new("overlay-create", "succeeded"), cancellationToken).ConfigureAwait(false);
                overlay.PhoneInputReceived += OnPhoneInputReceived;
                _phoneControl.StateChanged += OnScreenGuardChanged;
                lock (_gate)
                {
                    _activeOverlay = overlay;
                    _inputRouting = new();
                    overlay.ConfigureLockScreenFeatures(
                        _keepAwakeWhileGrabbed,
                        _unlockKeypadEnabled);
                }

                latestFrames = new LatestAndroidVideoFrameReader(session, cancellationToken);
                Volatile.Write(ref _diagnosticEncodedReader, latestFrames);
#pragma warning disable CA2000 // The outer finally awaits reader disposal, including when checkpoint recording fails; analyzer does not follow this callback-owning reader's async cleanup.
                decodedFrames = new LatestDecodedVideoFrameReader(latestFrames, packet =>
                {
                    RecordStageAsync(new("decoder-create", "started", Width: packet.Width, Height: packet.Height,
                        Generation: packet.Generation), cancellationToken).AsTask().GetAwaiter().GetResult();
                    IH264VideoDecoder created = _pipelineFactory.CreateDecoder(packet.Width, packet.Height, packet.Configuration.Span, detail =>
                    {
                        RecordStageAsync(new("decoder-" + detail.Kind, detail.Phase, detail.Backend, packet.Width,
                            packet.Height, Generation: packet.Generation), cancellationToken).AsTask().GetAwaiter().GetResult();
                        if (detail.Failure is not null)
                        { RecordFailureAsync(detail.Failure, "decoder-" + detail.Kind).AsTask().GetAwaiter().GetResult(); }
                    });
                    try
                    {
                        RecordStageAsync(new("decoder-create", "succeeded", created.BackendName, packet.Width, packet.Height,
                            Generation: packet.Generation), cancellationToken).AsTask().GetAwaiter().GetResult();
                        RecordStageAsync(new("first-decode", "started", Generation: packet.Generation), cancellationToken).AsTask().GetAwaiter().GetResult();
                        return created;
                    }
                    catch (Exception primary)
                    {
                        try { created.Dispose(); }
                        catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
                        throw;
                    }
                }, cancellationToken);
#pragma warning restore CA2000
                keyguardTask = _keyguardMonitor.RunAsync(deviceKey, () => _unlockKeypadEnabled,
                    overlay.SetPhoneLocked, keyguardLifetime.Token);
                inputEvidenceTask = PhoneInputEvidenceSampler.RunAsync(overlay, _inputRouting,
                    (interaction, routing) => CaptureInputEvidence(overlay, session.DeviceKey, interaction, routing),
                    keyguardLifetime.Token);
                while (await decodedFrames.ReadAsync(cancellationToken).ConfigureAwait(false) is { } frame)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using (frame)
                    {
                        DecodedVideoFrame decoded = frame.Frame;
                        if (generation != frame.Generation)
                        {
                            bool dimensionsChanged = width > 0 && height > 0 &&
                                (width != frame.SourceWidth || height != frame.SourceHeight);
                            generation = frame.Generation;
                            width = frame.SourceWidth;
                            height = frame.SourceHeight;
                            if (dimensionsChanged)
                            {
                                Publish(Snapshot with
                                {
                                    State = PhoneOverlayState.Starting,
                                    ReasonCode = PhoneReasonCodes.VideoOverlayReconfiguring,
                                    Message = $"手机方向变化，正在切换到 {width}×{height}"
                                });
                                _log.TryWrite(new(DateTimeOffset.UtcNow, "video_overlay", PhoneReasonCodes.VideoOverlayReconfiguring,
                                    "检测到手机画面尺寸变化，已重建解码阶段", session.DeviceKey, VideoWidth: width, VideoHeight: height,
                                    PacketCount: metrics.SubmittedFrames));
                            }
                        }

                        bool firstSubmission = submittedGeneration != generation;
                        if (firstSubmission)
                        {
                            await RecordStageAsync(new("first-decode", "succeeded", frame.Backend,
                                decoded.Width, decoded.Height, decoded.Stride, decoded.Pixels.Length, generation), cancellationToken).ConfigureAwait(false);
                            if (decoded.SourceGeometry is { } sourceGeometry)
                            {
                                await RecordStageAsync(new("decoder-buffer-layout", "succeeded", Generation: generation)
                                { Geometry = sourceGeometry }, cancellationToken).ConfigureAwait(false);
                            }
                            await RecordStageAsync(new("gpu-create", "started", Generation: generation), cancellationToken).ConfigureAwait(false);
                        }
                        presenter ??= _pipelineFactory.CreatePresenter(overlay.GraphicsAdapterLuid);
                        if (firstSubmission)
                        {
                            await RecordStageAsync(new("gpu-create", "succeeded", presenter.AdapterBackend, Generation: generation), cancellationToken).ConfigureAwait(false);
                            await RecordStageAsync(new("first-convert", "started", Width: decoded.VisibleWidth, Height: decoded.VisibleHeight, Generation: generation)
                            {
                                Geometry = new("gpu-convert", decoded.Width, decoded.Height, decoded.VisibleWidth, decoded.VisibleHeight,
                                decoded.Stride, decoded.Pixels.Length, Layout: "nv12-to-rgba")
                                { Upload = presenter.UploadDiagnostic }
                            }, cancellationToken).ConfigureAwait(false);
                        }
                        Volatile.Write(ref _diagnosticPresenter, presenter);
                        Volatile.Write(ref _pipelinePhase, "present");
                        long conversionStartedAt = Stopwatch.GetTimestamp();
                        GpuVideoFrame gpuFrame = presenter.Present(decoded);
                        long conversionCompletedAt = Stopwatch.GetTimestamp();
                        if (firstSubmission)
                        {
                            await RecordStageAsync(new("first-convert", "succeeded", Width: gpuFrame.Width, Height: gpuFrame.Height, Generation: generation)
                            { Geometry = gpuFrame.Geometry }, cancellationToken).ConfigureAwait(false);
                            await RecordStageAsync(new("first-submit", "started", Generation: generation), cancellationToken).ConfigureAwait(false);
                        }
                        Volatile.Write(ref _pipelinePhase, "openvr-submit");
                        long submissionStartedAt = Stopwatch.GetTimestamp();
                        overlay.Submit(gpuFrame);
                        long submittedAt = Stopwatch.GetTimestamp();
                        Volatile.Write(ref _pipelinePhase, "submitted");
                        if (firstSubmission)
                        {
                            await RecordStageAsync(new("first-submit", "succeeded", Generation: generation), cancellationToken).ConfigureAwait(false);
                            if (trialId is not null)
                            {
                                await videoTrials!.FirstFrameAsync(trialId, new(decoded.VisibleWidth, decoded.VisibleHeight,
                                    decoded.Width, decoded.Height, decoded.Stride, decoded.Pixels.Length, frame.Backend,
                                    presenter.AdapterBackend), cancellationToken).ConfigureAwait(false);
                            }
                            firstSubmittedAt ??= Stopwatch.GetTimestamp();
                            submittedGeneration = generation;
                        }
                        metrics.RecordSubmittedFrame(
                            frame.EstimatedCaptureTimestamp is long captureTimestamp
                                ? Stopwatch.GetElapsedTime(captureTimestamp, submittedAt)
                                    .TotalMilliseconds
                                : null,
                            new VideoFrameStageDurations(
                                ElapsedMilliseconds(frame.ReceivedTimestamp, frame.DecodeStartedTimestamp),
                                ElapsedMilliseconds(frame.DecodeStartedTimestamp, frame.OutputReadyTimestamp),
                                ElapsedMilliseconds(frame.OutputReadyTimestamp, submittedAt),
                                ElapsedMilliseconds(conversionStartedAt, conversionCompletedAt),
                                ElapsedMilliseconds(submissionStartedAt, submittedAt),
                                ElapsedMilliseconds(frame.EstimatedCaptureTimestamp, frame.ReceivedTimestamp)));
                        if (!stableRecorded && trialId is not null && firstSubmittedAt is long firstAt &&
                            metrics.SubmittedFrames >= 30 && Stopwatch.GetElapsedTime(firstAt) >= TimeSpan.FromSeconds(30))
                        {
                            await videoTrials!.StableAsync(trialId, metrics.SubmittedFrames, cancellationToken).ConfigureAwait(false);
                            stableRecorded = true;
                        }
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
                            latestFrames.DroppedFrames + decodedFrames.ReplacedFrames + decodedFrames.NativeReplacedFrames,
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
                            frame.Backend,
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
                            interaction.ScaleActive)
                        {
                            UploadDiagnostic = presenter.UploadDiagnostic,
                            InputPath = interaction.InputPath,
                            InputRouting = Snapshot.InputRouting,
                            TotalEncodedBytes = metrics.TotalEncodedBytes,
                            EncodedQueueBytes = latestFrames.QueuedPayloadBytes,
                            PeakEncodedQueueBytes = latestFrames.PeakQueuedPayloadBytes,
                            EncodedQueueByteOverflows = latestFrames.ByteBudgetOverflows,
                            VideoClock = frame.Clock,
                            VideoColor = decoded.Color,
                            LatencyBreakdown = metrics.LatencyBreakdown,
                            LatencySampleCount = metrics.LatencySampleCount,
                            UnavailableLatencySampleCount = metrics.UnavailableLatencySampleCount,
                            RejectedLatencySampleCount = metrics.RejectedLatencySampleCount
                        };
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


                    }

                    started.TrySetResult();
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                primaryFailure = exception;
                await RecordFailureAsync(exception, "video-primary").ConfigureAwait(false);
                throw;
            }
            finally
            {
                try
                {
                    if (decodedFrames is not null)
                    {
                        try
                        {
                            await RecordStageAsync(new("video-cleanup/decoder", "started"), CancellationToken.None).ConfigureAwait(false);
                            await RecordStageAsync(new("decoder-final-state", "sampled", decodedFrames.DiagnosticState +
                                $"; presentationReplaced={decodedFrames.ReplacedFrames}; omittedTimestamps={decodedFrames.OmittedTimestamps}"), CancellationToken.None).ConfigureAwait(false);
                        }
                        finally { await decodedFrames.DisposeAsync().ConfigureAwait(false); }
                        await RecordStageAsync(new("video-cleanup/decoder", "succeeded"), CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch (Exception error)
                {
                    cleanupFailures.Add(error);
                    await RecordFailureAsync(error, "video-cleanup/decoder").ConfigureAwait(false);
                    await RecordStageAsync(new("video-cleanup/decoder", "failed"), CancellationToken.None).ConfigureAwait(false);
                }
                await CleanupAsync("encoded-reader", async () =>
                {
                    try { if (latestFrames is not null) { await latestFrames.DisposeAsync().ConfigureAwait(false); } }
                    finally { Volatile.Write(ref _diagnosticEncodedReader, null); }
                }).ConfigureAwait(false);
                await CleanupAsync("keyguard", async () =>
                {
                    await keyguardLifetime.CancelAsync().ConfigureAwait(false);
                    try { await keyguardTask.ConfigureAwait(false); }
                    catch (OperationCanceledException) when (keyguardLifetime.IsCancellationRequested) { }
                }).ConfigureAwait(false);
                await CleanupAsync("input-evidence", async () => await inputEvidenceTask.ConfigureAwait(false)).ConfigureAwait(false);
                if (overlay is not null)
                {
                    await CleanupAsync("overlay", () =>
                    {
                        _phoneControl.StateChanged -= OnScreenGuardChanged;
                        overlay.PhoneInputReceived -= OnPhoneInputReceived;
                        lock (_gate)
                        {
                            if (ReferenceEquals(_activeOverlay, overlay)) { _activeOverlay = null; }
                        }
                        overlay.Dispose();
                        return ValueTask.CompletedTask;
                    }).ConfigureAwait(false);
                }
                await CleanupAsync("presenter", () => { presenter?.Dispose(); return ValueTask.CompletedTask; }).ConfigureAwait(false);

                async ValueTask CleanupAsync(string component, Func<ValueTask> release)
                {
                    await RecordStageAsync(new("video-cleanup/" + component, "started"), CancellationToken.None).ConfigureAwait(false);
                    try
                    {
                        await release().ConfigureAwait(false);
                        await RecordStageAsync(new("video-cleanup/" + component, "succeeded"), CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        cleanupFailures.Add(exception);
                        await RecordFailureAsync(exception, "video-cleanup/" + component).ConfigureAwait(false);
                        await RecordStageAsync(new("video-cleanup/" + component, "failed"), CancellationToken.None).ConfigureAwait(false);
                    }
                }
            }
            if (cleanupFailures.Count > 0) { throw new AggregateException("Video cleanup failed", cleanupFailures); }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            started.TrySetCanceled(cancellationToken);
        }
        catch (Exception exception)
        {
            PhoneOverlayServiceException failure = MapFailure(exception);
            trialFailed = true;
            trialRequiresManualRetry = RequiresVideoParameterReview(exception);
            trialEndReason = failure.ReasonCode;
            if (primaryFailure is null) { await RecordFailureAsync(exception, "video-primary").ConfigureAwait(false); }
            await RecordStageAsync(new("video-failure", "failed", $"{failure.ReasonCode}; {exception.GetType().Name}; HRESULT={exception.HResult:X8}"), CancellationToken.None).ConfigureAwait(false);
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
            if (trialId is not null)
            {
                try { await videoTrials!.EndAsync(trialId, trialFailed, trialEndReason, CancellationToken.None, trialRequiresManualRetry).ConfigureAwait(false); }
                catch (Exception error)
                {
                    await RecordFailureAsync(error, "video-trial-end").ConfigureAwait(false);
                    _log.TryWrite(new(DateTimeOffset.UtcNow, "video_trial", "VIDEO_TRIAL_END_WRITE_FAILED",
                        "画面恢复记录未能完成，自动打开已暂停", ExceptionType: error.GetType().FullName));
                }
            }
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
        overlay?.NotifyControlGeometryRevision(args.Snapshot.Geometry?.Revision ?? 0);
    }

    private void OnPhoneInputReceived(OpenVrPhoneInputCommand command)
    {
        PhoneOverlaySnapshot snapshot = Snapshot;
        int width = command.ScreenWidth > 0 ? command.ScreenWidth : snapshot.Width;
        int height = command.ScreenHeight > 0 ? command.ScreenHeight : snapshot.Height;
        PhoneControlState controlState = _phoneControl.Snapshot.State;
        if (!_inputRouting.TryRoute(command.Kind, controlState, width, height)) { return; }

        bool positioned = command.Kind is PhoneInputCommandKind.PointerDown or
            PhoneInputCommandKind.PointerMove or
            PhoneInputCommandKind.PointerUp or
            PhoneInputCommandKind.PointerCancel or
            PhoneInputCommandKind.Scroll;
        _phoneControl.QueueFromSteamVr(
            command.Kind,
            command.NormalizedX,
            command.NormalizedY,
            positioned ? width : 0,
            positioned ? height : 0,
            command.ScrollDelta,
            command.UnlockDigit, command.GeometryRevision);
    }

    private void CaptureInputEvidence(IOpenVrSceneOverlay owner, string deviceKey,
        OpenVrPhoneInteractionSnapshot interaction, InputRoutingDiagnostic routing)
    {
        PhoneOverlaySnapshot video;
        lock (_gate)
        {
            if (!ReferenceEquals(_activeOverlay, owner)) { return; }
            _snapshot = _snapshot with { InputPath = interaction.InputPath, InputRouting = routing };
            _diagnosticSnapshot = _diagnosticSnapshot with { InputPath = interaction.InputPath, InputRouting = routing };
            video = _snapshot;
        }
        _log.TryWrite(new(DateTimeOffset.UtcNow, "phone_input_path",
            interaction.InputPath?.LastActionReadError > 0 ? "PHONE_INPUT_ACTION_READ_FAILED" : "PHONE_INPUT_PATH_SAMPLE",
            "输入路径累计采样；动作边沿不是物理按键回执，转发不等于入队或手机执行", deviceKey)
        { InputPath = interaction.InputPath, InputRouting = routing, InputDispatch = _phoneControl.Snapshot.Dispatch });
        if (video.State == PhoneOverlayState.Running)
        {
            _log.TryWrite(new(DateTimeOffset.UtcNow, "video_quality", PhoneReasonCodes.VideoQualitySample,
                "视频质量定时采样；无新帧时仍保留观察", deviceKey, VideoWidth: video.Width, VideoHeight: video.Height,
                PacketCount: video.SubmittedFrames, PayloadBytes: video.TotalEncodedBytes)
            {
                VideoQuality = new(video.FramesPerSecond, video.AverageBitrateMbps, video.AverageLatencyMilliseconds, video.SubmittedFrames, video.DroppedFrames)
                {
                    EncodedQueueBytes = video.EncodedQueueBytes,
                    PeakEncodedQueueBytes = video.PeakEncodedQueueBytes,
                    EncodedQueueByteOverflows = video.EncodedQueueByteOverflows,
                    VideoClock = video.VideoClock,
                    VideoColor = video.VideoColor,
                    LatencyBreakdown = video.LatencyBreakdown,
                    LatencySampleCount = video.LatencySampleCount,
                    UnavailableLatencySampleCount = video.UnavailableLatencySampleCount,
                    RejectedLatencySampleCount = video.RejectedLatencySampleCount,
                    PeakBitrateMbps = video.PeakBitrateMbps,
                    RequestedResolutionPercent = video.RequestedResolutionPercent,
                    RequestedBitrateMbps = video.RequestedBitrateMbps,
                    RequestedMaximumFramesPerSecond = video.RequestedMaximumFramesPerSecond
                }
            });
        }
    }

    private static double? ElapsedMilliseconds(long? start, long? end) =>
        start is long first && end is long last ? Stopwatch.GetElapsedTime(first, last).TotalMilliseconds : null;

    private static bool RequiresVideoParameterReview(Exception error)
    {
        List<Exception> pending = [error];
        for (int index = 0; index < pending.Count && index < 8; index++)
        {
            Exception current = pending[index];
            if (current is MediaDecoderException or GpuVideoPresenterException or VideoTrialException) { return true; }
            if (current is AggregateException aggregate) { pending.AddRange(aggregate.InnerExceptions.Take(8 - pending.Count)); }
            else if (current.InnerException is { } inner && pending.Count < 8) { pending.Add(inner); }
        }
        return false;
    }

    private static PhoneOverlayServiceException MapFailure(Exception exception) => exception switch
    {
        BindingToolComponentException component => new(component.ReasonCode, component.Message, component),
        VideoTrialException trial => new(trial.ReasonCode, trial.Message, trial),
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
