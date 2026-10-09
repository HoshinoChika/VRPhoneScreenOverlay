using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

public enum OpenVrPlayspaceDragState
{
    Stopped,
    Starting,
    Ready,
    Faulted,
    WaitingForSteamVr,
}

public sealed record OpenVrPlayspaceDragSnapshot(
    OpenVrPlayspaceDragState State,
    bool Enabled,
    float Multiplier,
    string ReasonCode,
    string Message,
    float OffsetX = 0,
    float OffsetY = 0,
    float OffsetZ = 0,
    bool FlingEnabled = false,
    OpenVrPlayspaceMotionOptions? MotionOptions = null, long MotionSaveRevision = 0);

public sealed class OpenVrPlayspaceDragChangedEventArgs(
    OpenVrPlayspaceDragSnapshot snapshot) : EventArgs
{
    public OpenVrPlayspaceDragSnapshot Snapshot { get; } = snapshot;
}

public sealed record OpenVrPlayspaceDragDiagnostic(
    string ReasonCode,
    string Message);

public sealed class OpenVrPlayspaceDragDiagnosticEventArgs(
    OpenVrPlayspaceDragDiagnostic diagnostic) : EventArgs
{
    public OpenVrPlayspaceDragDiagnostic Diagnostic { get; } = diagnostic;
}

public interface IOpenVrPlayspaceDragService : IDisposable
{
    public OpenVrPlayspaceDragSnapshot Snapshot { get; }

    public event EventHandler<OpenVrPlayspaceDragChangedEventArgs>? StateChanged;

    public event EventHandler<OpenVrPlayspaceDragDiagnosticEventArgs>? DiagnosticRecorded;

    public void Start();

    public void StopService();

    public void SetEnabled(bool enabled);

    public void SetMultiplier(float multiplier);

    public void ConfigureMotion(bool enabled, OpenVrPlayspaceMotionOptions options);

    public void ReportMotionSaveResult(bool succeeded) { }

    public void SetPhoneControllerHand(OpenVrControllerHand hand);
}

public sealed class OpenVrPlayspaceDragService(
    OpenVrControllerHand phoneControllerHand,
    float multiplier,
    Func<bool>? runtimeAvailable = null) : IOpenVrPlayspaceDragService
{
    private readonly Func<bool> _runtimeAvailable = runtimeAvailable ?? OpenVrRuntimeAvailability.IsRunning;
    private const string _leftDragPath = "/actions/main/in/LeftHandSpaceDrag";
    private const string _rightDragPath = "/actions/main/in/RightHandSpaceDrag";
    private const string _resetOffsetsPath = "/actions/main/in/ResetOffsets";
    private readonly object _stateGate = new();
    private readonly ManualResetEventSlim _stopSignal = new(false);
    private readonly TrackedDevicePose_t[] _poses =
        new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
    private OpenVrPlayspaceDragSnapshot _snapshot = new(
        OpenVrPlayspaceDragState.Stopped,
        false,
        ValidateMultiplier(multiplier),
        OpenVrReasonCodes.PlayspaceDragNotStarted,
        "独立空间拖拽服务未运行");
    private Thread? _worker;
    private int _phoneControllerHand = (int)phoneControllerHand;
    private int _handVersion;
    private float _multiplier = ValidateMultiplier(multiplier);
    private long _motionSaveRevision;
    private OpenVrPlayspaceMotionConfiguration _motionConfiguration = new(false, OpenVrPlayspaceMotionOptions.Default);
    private volatile bool _enabled;
    private volatile bool _disposed;

    // The worker refreshes runtime availability at most once per second. UI
    // availability queries must not enumerate OS processes on every state event.
    public bool IsRuntimeReady => !_disposed && Snapshot.State == OpenVrPlayspaceDragState.Ready;

    public OpenVrPlayspaceDragSnapshot Snapshot
    {
        get
        {
            lock (_stateGate)
            {
                return _snapshot;
            }
        }
    }

    public event EventHandler<OpenVrPlayspaceDragChangedEventArgs>? StateChanged;

    public event EventHandler<OpenVrPlayspaceDragDiagnosticEventArgs>? DiagnosticRecorded;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_stateGate)
        {
            if (_worker is { IsAlive: true })
            {
                return;
            }

            _stopSignal.Reset();
            OpenVrRuntimeHost.ActionUpdates.SetPlayspaceEnabled(_enabled);
            OpenVrRuntimeHost.ActionUpdates.SetPlayspaceMultiplier(_multiplier);
            PublishLocked(new OpenVrPlayspaceDragSnapshot(
                OpenVrPlayspaceDragState.Starting,
                _enabled,
                Volatile.Read(ref _multiplier),
                OpenVrReasonCodes.PlayspaceDragStarting,
                "正在连接 SteamVR 独立空间拖拽"));
            _worker = new Thread(Run)
            {
                IsBackground = true,
                Name = "VRPhoneScreen playspace drag",
            };
            _worker.Start();
        }
    }

    public void StopService()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        StopWorker(resetStopSignal: true);
    }

    public void SetEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_stateGate)
        {
            _enabled = enabled;
            OpenVrRuntimeHost.ActionUpdates.SetPlayspaceEnabled(enabled);
            PublishDiagnostic(
                OpenVrReasonCodes.PlayspaceDragEnabledChanged,
                $"enabled={enabled}");
            Publish(Snapshot with
            {
                Enabled = enabled,
                ReasonCode = enabled ? OpenVrReasonCodes.PlayspaceDragEnabled : OpenVrReasonCodes.PlayspaceDragDisabled,
                Message = enabled ? "独立空间拖拽已开启" : "独立空间拖拽已关闭",
            });
        }
    }

    public void SetMultiplier(float multiplier)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        float validated = ValidateMultiplier(multiplier);
        lock (_stateGate)
        {
            float previous = Interlocked.Exchange(ref _multiplier, validated);
            OpenVrRuntimeHost.ActionUpdates.SetPlayspaceMultiplier(validated);
            PublishDiagnostic(
                OpenVrReasonCodes.PlayspaceDragMultiplierApplied,
                FormattableString.Invariant(
                    $"previous={previous:0}; applied={validated:0}"));
            Publish(Snapshot with
            {
                Multiplier = validated,
                ReasonCode = OpenVrReasonCodes.PlayspaceDragMultiplierChanged,
                Message = $"空间拖拽倍率已设为 {validated:0}x",
            });
        }
    }

    public void ReportMotionSaveResult(bool succeeded) => OpenVrRuntimeHost.ActionUpdates.SetMotionSaveState(
        succeeded ? OpenVrMotionSaveState.Saved : OpenVrMotionSaveState.Failed);

    public void ConfigureMotion(bool enabled, OpenVrPlayspaceMotionOptions options)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        Volatile.Write(ref _motionConfiguration, new(enabled, options));
        OpenVrRuntimeHost.ActionUpdates.SetMotion(enabled, options);
        Publish(Snapshot with { FlingEnabled = enabled, MotionOptions = options });
    }

    public void SetPhoneControllerHand(OpenVrControllerHand hand)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Interlocked.Exchange(ref _phoneControllerHand, (int)hand);
        _ = Interlocked.Increment(ref _handVersion);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopWorker(resetStopSignal: false);
        _stopSignal.Dispose();
    }

    private void Run()
    {
        while (!_stopSignal.IsSet)
        {
            if (!_runtimeAvailable())
            {
                PublishWaiting();
                _stopSignal.Wait(TimeSpan.FromSeconds(3));
                continue;
            }
            try
            {
                RunSession();
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or DllNotFoundException or
                    EntryPointNotFoundException or BadImageFormatException)
            {
                if (!_enabled || !_runtimeAvailable())
                {
                    PublishWaiting();
                    _stopSignal.Wait(TimeSpan.FromSeconds(3));
                    continue;
                }
                Publish(new OpenVrPlayspaceDragSnapshot(
                    OpenVrPlayspaceDragState.Faulted,
                    _enabled,
                    Volatile.Read(ref _multiplier),
                    OpenVrReasonCodes.PlayspaceDragSteamvrUnavailable,
                    "SteamVR 独立空间拖拽暂不可用，正在重试"));
                _stopSignal.Wait(TimeSpan.FromSeconds(3));
            }
        }
    }

    private void PublishWaiting() => Publish(new OpenVrPlayspaceDragSnapshot(
        OpenVrPlayspaceDragState.WaitingForSteamVr,
        _enabled,
        Volatile.Read(ref _multiplier),
        OpenVrReasonCodes.PlayspaceWaitingForSteamVr,
        "等待 SteamVR 就绪"));

    private void RunSession()
    {
        CVRSystem system;
        ulong actionSet;
        ulong leftDrag = OpenVR.k_ulInvalidActionHandle;
        ulong rightDrag = OpenVR.k_ulInvalidActionHandle;
        ulong resetOffsets = OpenVR.k_ulInvalidActionHandle;
        lock (OpenVrRuntimeHost.ApiGate)
        {
            EVRInitError initializationError = EVRInitError.None;
            system = OpenVrRuntimeHost.GetOrStart(ref initializationError);
            if (initializationError != EVRInitError.None)
            {
                throw new InvalidOperationException(
                    $"SteamVR initialization failed: {initializationError}");
            }

            EVRInputError manifestError = OpenVrRuntimeHost.EnsureActionManifestSubmitted();
            if (manifestError is not (EVRInputError.None or EVRInputError.IPCError))
            {
                throw new InvalidOperationException($"SteamVR action manifest failed: {manifestError}");
            }

            actionSet = OpenVrInputManifest.GetActionSet();
            Check(OpenVR.Input.GetActionHandle(_leftDragPath, ref leftDrag));
            Check(OpenVR.Input.GetActionHandle(_rightDragPath, ref rightDrag));
            Check(OpenVR.Input.GetActionHandle(_resetOffsetsPath, ref resetOffsets));
        }

        OpenVrPlayspaceDragController? controller = null;
        try
        {
            int appliedHandVersion = -1;
            ulong inputSource = OpenVR.k_ulInvalidInputValueHandle;
            VRActiveActionSet_t[] activeSet = new VRActiveActionSet_t[1];
            OpenVrDashboardInputGate dashboardInput = new();
            long lastPhoneInputRevision = -1;
            Publish(new OpenVrPlayspaceDragSnapshot(
                OpenVrPlayspaceDragState.Ready,
                _enabled,
                Volatile.Read(ref _multiplier),
                OpenVrReasonCodes.PlayspaceDragReady,
                "SteamVR 独立空间拖拽已就绪"));

            PublishDiagnostic(
                OpenVrReasonCodes.PlayspaceDragSessionReady,
                FormattableString.Invariant(
                    $"multiplier={Volatile.Read(ref _multiplier):0}; enabled={_enabled}"));
            bool previousDragPressed = false;
            int appliedFrameCount = 0;
            float sourcePathLength = 0;
            float appliedPathLength = 0;
            float finalOffsetX = 0;
            float finalOffsetY = 0;
            float finalOffsetZ = 0;
            float sourceDeltaX = 0;
            float sourceDeltaY = 0;
            float sourceDeltaZ = 0;
            float appliedDeltaX = 0;
            float appliedDeltaY = 0;
            float appliedDeltaZ = 0;
            float publishedOffsetX = 0;
            float publishedOffsetY = 0;
            float publishedOffsetZ = 0;
            long nextCoordinatePublishAt = 0;
            long nextAvailabilityCheckAt = 0;
            long originRevision = -1;
            long bindingRevision = -1;
            OpenVrPlayspaceFrameClock frameClock = new();
            long lastFreshInput = Stopwatch.GetTimestamp();

            while (!_stopSignal.IsSet)
            {
                long availabilityNow = Environment.TickCount64;
                if (availabilityNow >= nextAvailabilityCheckAt)
                {
                    nextAvailabilityCheckAt = availabilityNow + 1000;
                    if (!_runtimeAvailable()) { return; }
                }
                lock (_stateGate)
                {
                    OpenVrPlayspaceControlRequest? controlRequest = OpenVrRuntimeHost.ActionUpdates.TakeControls();
                    if (controlRequest is not null)
                    {
                        if (controlRequest.Enabled != _enabled) { SetEnabled(controlRequest.Enabled); }
                        if (controlRequest.Multiplier != Volatile.Read(ref _multiplier)) { SetMultiplier(controlRequest.Multiplier); }
                        if (controlRequest.FlingEnabled is { } fling)
                        {
                            ConfigureMotion(fling, controlRequest.MotionOptions ?? Volatile.Read(ref _motionConfiguration).Options);
                            if (controlRequest.MotionOptions is not null)
                            {
                                OpenVrRuntimeHost.ActionUpdates.SetMotionSaveState(OpenVrMotionSaveState.Pending);
                                Publish(Snapshot with { MotionSaveRevision = ++_motionSaveRevision, ReasonCode = "PLAYSPACE_MOTION_SAVE_REQUEST", Message = "正在保存浮窗移动参数" });
                            }
                        }
                    }
                }
                lock (OpenVrRuntimeHost.ApiGate)
                {
                    int currentHandVersion = Volatile.Read(ref _handVersion);
                    if (controller is null || currentHandVersion != appliedHandVersion)
                    {
                        controller?.Restore();
                        OpenVrControllerHand phoneHand = (OpenVrControllerHand)Volatile.Read(
                            ref _phoneControllerHand);
                        Check(OpenVrRuntimeHost.SetInputHand(phoneHand));
                        OpenVrControllerHand playspaceHand =
                            OpenVrControllerHandRouting.Opposite(phoneHand);
                        string inputPath = OpenVrControllerHandRouting.InputSourcePath(playspaceHand);
                        inputSource = OpenVR.k_ulInvalidInputValueHandle;
                        Check(OpenVR.Input.GetInputSourceHandle(inputPath, ref inputSource));
                        controller = new OpenVrPlayspaceDragController(
                            system,
                            OpenVR.ChaperoneSetup,
                            playspaceHand);
                        PublishCoordinates(0, 0, 0);
                        publishedOffsetX = 0;
                        publishedOffsetY = 0;
                        publishedOffsetZ = 0;
                        activeSet[0].ulActionSet = actionSet;
                        activeSet[0].ulRestrictedToDevice = inputSource;
                        appliedHandVersion = currentHandVersion;
                    }

                    OpenVrSharedPlayspaceInput sharedInput =
                        OpenVrRuntimeHost.ActionUpdates.Read();
                    OpenVrRuntimeEvents events = OpenVrRuntimeHost.Events;
                    if (bindingRevision != events.BindingRevision)
                    {
                        _ = OpenVrRuntimeHost.SetInputHand((OpenVrControllerHand)Volatile.Read(ref _phoneControllerHand));
                        controller.SuspendForBindingChange();
                        bindingRevision = events.BindingRevision;
                    }
                    if (!sharedInput.PhonePollerActive) { events.Poll(system); }
                    if (originRevision != events.OriginRevision)
                    {
                        controller.Restore();
                        originRevision = events.OriginRevision;
                    }
                    OpenVrPlayspaceInputSample inputSample;
                    bool sampleIsFresh;
                    bool dashboard = OpenVR.Overlay.IsDashboardVisible();
                    if (sharedInput.PhonePollerActive)
                    {
                        inputSample = sharedInput.InputSample;
                        sampleIsFresh = sharedInput.InputRevision != lastPhoneInputRevision;
                        lastPhoneInputRevision = sharedInput.InputRevision;
                    }
                    else
                    {
                        // Match the phone poller: drag/reset never override scene input.
                        activeSet[0].nPriority = 0;
                        CVRInput input = OpenVR.Input ?? throw new InvalidOperationException(
                            "SteamVR input interface became unavailable.");
                        Check(input.UpdateActionState(
                            activeSet,
                            (uint)Marshal.SizeOf<VRActiveActionSet_t>()));
                        inputSample = OpenVrPlayspaceInputSample.Read(
                            input, leftDrag, rightDrag, resetOffsets, inputSource, dashboard);
                        inputSample = inputSample with
                        {
                            DashboardVisible = dashboard || OpenVR.Overlay.IsDashboardVisible(),
                        };
                        sampleIsFresh = true;
                    }
                    dashboardInput.Apply(dashboard, inputSample, sampleIsFresh,
                        out bool dragPressed, out bool resetPressed);
                    dragPressed &= _enabled;
                    float appliedMultiplier = Volatile.Read(ref _multiplier);
                    if (dragPressed && !previousDragPressed)
                    {
                        appliedFrameCount = 0;
                        sourcePathLength = 0;
                        appliedPathLength = 0;
                        finalOffsetX = 0;
                        finalOffsetY = 0;
                        finalOffsetZ = 0;
                        sourceDeltaX = 0;
                        sourceDeltaY = 0;
                        sourceDeltaZ = 0;
                        appliedDeltaX = 0;
                        appliedDeltaY = 0;
                        appliedDeltaZ = 0;
                        PublishDiagnostic(
                            OpenVrReasonCodes.PlayspaceDragGestureStarted,
                            FormattableString.Invariant(
                                $"multiplier={appliedMultiplier:0}"));
                    }

                    long tick = Stopwatch.GetTimestamp();
                    float sinceVsync = 0;
                    ulong frame = 0;
                    bool hasFrame = system.GetTimeSinceLastVsync(ref sinceVsync, ref frame);
                    bool advance = frameClock.TryAdvance(hasFrame ? frame : null,
                        tick / (double)Stopwatch.Frequency, out float seconds);
                    if (advance)
                    {
                        system.GetDeviceToAbsoluteTrackingPose(
                            ETrackingUniverseOrigin.TrackingUniverseStanding, 0, _poses);
                    }
                    if (sampleIsFresh) { lastFreshInput = tick; }
                    bool inputCurrent = Stopwatch.GetElapsedTime(lastFreshInput, tick).TotalSeconds <= 0.1;
                    bool hmdTracked = _poses[0].bPoseIsValid && _poses[0].bDeviceIsConnected &&
                        _poses[0].eTrackingResult == ETrackingResult.Running_OK;
                    OpenVrPlayspaceMotionConfiguration motion = Volatile.Read(ref _motionConfiguration);
                    bool motionAllowed = !events.BindingChanging && !events.RoomSetupActive && !dashboardInput.Suppressed && inputCurrent && hmdTracked;
                    if (!motionAllowed) { controller.Suspend(); }
                    OpenVrPlayspaceDragUpdate update = !advance ? default : controller.Update(
                        dragPressed,
                        resetPressed,
                        appliedMultiplier,
                        _poses,
                        motion.Options,
                        motion.Enabled && _enabled,
                        seconds,
                        motionAllowed);
                    if (update.HeightResetRequested)
                    {
                        string reason = update.HeightResetSucceeded
                            ? motion.Options.ResetAllOffsets ? OpenVrReasonCodes.PlayspaceOffsetsResetApplied
                                : OpenVrReasonCodes.PlayspaceHeightResetApplied
                            : OpenVrReasonCodes.PlayspaceHeightResetUnavailable;
                        string message = update.HeightResetSucceeded
                            ? motion.Options.ResetAllOffsets ? "已清除全部空间偏移和弹射速度" : "已复位高度，保留当前水平位置" : "当前地面坐标不可用，请等待 SteamVR 定位恢复后重试";
                        Publish(Snapshot with { ReasonCode = reason, Message = message });
                        PublishDiagnostic(reason, message);
                    }
                    float currentOffsetX = controller.OffsetX;
                    float currentOffsetY = controller.OffsetY;
                    float currentOffsetZ = controller.OffsetZ;
                    if (update.OffsetApplied && dragPressed)
                    {
                        appliedFrameCount++;
                        sourcePathLength += update.SourceDistance;
                        appliedPathLength += update.AppliedDistance;
                        finalOffsetX = update.OffsetX;
                        finalOffsetY = update.OffsetY;
                        finalOffsetZ = update.OffsetZ;
                        sourceDeltaX += update.SourceDeltaX;
                        sourceDeltaY += update.SourceDeltaY;
                        sourceDeltaZ += update.SourceDeltaZ;
                        appliedDeltaX += update.AppliedDeltaX;
                        appliedDeltaY += update.AppliedDeltaY;
                        appliedDeltaZ += update.AppliedDeltaZ;
                    }

                    long now = Environment.TickCount64;
                    bool returnedToOrigin = currentOffsetX == 0 &&
                        currentOffsetY == 0 &&
                        currentOffsetZ == 0 &&
                        (publishedOffsetX != 0 || publishedOffsetY != 0 ||
                            publishedOffsetZ != 0);
                    bool gestureCompleted = !dragPressed && previousDragPressed;
                    if (returnedToOrigin || gestureCompleted ||
                        (update.OffsetApplied && now >= nextCoordinatePublishAt))
                    {
                        PublishCoordinates(currentOffsetX, currentOffsetY, currentOffsetZ);
                        publishedOffsetX = currentOffsetX;
                        publishedOffsetY = currentOffsetY;
                        publishedOffsetZ = currentOffsetZ;
                        nextCoordinatePublishAt = now + 100;
                    }

                    if (gestureCompleted)
                    {
                        PublishDiagnostic(
                            OpenVrReasonCodes.PlayspaceDragGestureCompleted,
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "multiplier={0:0}; frames={1}; sourcePathMeters={2:0.0000}; " +
                                "appliedPathMeters={3:0.0000}; sourceDeltaMeters={4:0.0000}," +
                                "{5:0.0000},{6:0.0000}; appliedDeltaMeters={7:0.0000}," +
                                "{8:0.0000},{9:0.0000}; offsetMeters={10:0.0000}," +
                                "{11:0.0000},{12:0.0000}",
                                appliedMultiplier,
                                appliedFrameCount,
                                sourcePathLength,
                                appliedPathLength,
                                sourceDeltaX,
                                sourceDeltaY,
                                sourceDeltaZ,
                                appliedDeltaX,
                                appliedDeltaY,
                                appliedDeltaZ,
                                finalOffsetX,
                                finalOffsetY,
                                finalOffsetZ));
                    }

                    previousDragPressed = dragPressed;
                }

                _stopSignal.Wait(TimeSpan.FromMilliseconds(4));
            }
        }
        finally
        {
            lock (OpenVrRuntimeHost.ApiGate)
            {
                controller?.Restore();
            }

            PublishCoordinates(0, 0, 0);
        }
    }

    private void StopWorker(bool resetStopSignal)
    {
        _stopSignal.Set();
        Thread? worker;
        lock (_stateGate)
        {
            worker = _worker;
        }

        if (worker is { IsAlive: true } && !worker.Join(TimeSpan.FromSeconds(5)))
        {
            throw new InvalidOperationException(
                "SteamVR playspace drag worker did not stop within five seconds.");
        }

        lock (_stateGate)
        {
            if (ReferenceEquals(_worker, worker))
            {
                _worker = null;
            }
        }

        Publish(new OpenVrPlayspaceDragSnapshot(
            OpenVrPlayspaceDragState.Stopped,
            _enabled,
            Volatile.Read(ref _multiplier),
            OpenVrReasonCodes.PlayspaceDragStopped,
            "独立空间拖拽服务已停止并恢复空间"));
        if (resetStopSignal)
        {
            _stopSignal.Reset();
        }
    }

    private static float ValidateMultiplier(float multiplier)
    {
        if (!float.IsFinite(multiplier) || multiplier <= 0f || multiplier > 40f)
        {
            throw new ArgumentOutOfRangeException(nameof(multiplier));
        }

        return multiplier;
    }

    private static void Check(EVRInputError error)
    {
        if (error != EVRInputError.None)
        {
            throw new InvalidOperationException($"SteamVR input failed: {error}");
        }
    }

    private void Publish(OpenVrPlayspaceDragSnapshot snapshot)
    {
        lock (_stateGate)
        {
            PublishLocked(snapshot);
        }
    }

    private void PublishLocked(OpenVrPlayspaceDragSnapshot snapshot)
    {
        // Coordinates/runtime status may have been sampled before a UI toggle.
        // They cannot overwrite the current control values with their old copy.
        snapshot = snapshot with { MotionSaveRevision = _motionSaveRevision, Enabled = _enabled, Multiplier = _multiplier, FlingEnabled = Volatile.Read(ref _motionConfiguration).Enabled, MotionOptions = Volatile.Read(ref _motionConfiguration).Options };
        _snapshot = snapshot;
        StateChanged?.Invoke(this, new OpenVrPlayspaceDragChangedEventArgs(snapshot));
    }

    private void PublishCoordinates(float x, float y, float z) =>
        Publish(Snapshot with
        {
            OffsetX = x,
            OffsetY = y,
            OffsetZ = z,
        });

    private void PublishDiagnostic(string reasonCode, string message) =>
        DiagnosticRecorded?.Invoke(
            this,
            new OpenVrPlayspaceDragDiagnosticEventArgs(
                new OpenVrPlayspaceDragDiagnostic(reasonCode, message)));
}
