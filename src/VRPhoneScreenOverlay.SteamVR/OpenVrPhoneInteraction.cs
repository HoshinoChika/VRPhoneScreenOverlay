using System.Runtime.InteropServices;
using System.Text;
using Valve.VR;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.SteamVR;

public readonly record struct OpenVrPhoneInputCommand(
    PhoneInputCommandKind Kind,
    float NormalizedX = 0,
    float NormalizedY = 0,
    float ScrollDelta = 0,
    int UnlockDigit = -1);

public delegate void OpenVrPhoneInputSink(OpenVrPhoneInputCommand command);

public sealed record OpenVrPhoneInteractionSnapshot(
    bool InputReady,
    bool WorldAnchored,
    bool Hovered,
    bool Grabbed,
    string ReasonCode,
    string Message,
    bool ControllerPoseValid = false,
    bool UnlockKeypadExpanded = false,
    string? DiagnosticExceptionType = null,
    string? DiagnosticExceptionMessage = null,
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
    public static OpenVrPhoneInteractionSnapshot Stopped { get; } = new(
        false,
        false,
        false,
        false,
        OpenVrReasonCodes.InputStopped,
        "SteamVR 手柄输入未运行");
}

public sealed record OpenVrBindingResult(
    bool Succeeded,
    string ReasonCode,
    string Message);

internal sealed class OpenVrPhoneInteraction : IDisposable
{
    private const string _leftSpaceDragPath = "/actions/main/in/LeftHandSpaceDrag";
    private const string _rightSpaceDragPath = "/actions/main/in/RightHandSpaceDrag";
    private const string _resetOffsetsPath = "/actions/main/in/ResetOffsets";
    private const string _phoneBackPath = "/actions/main/in/PhoneBack";
    private const string _phoneHomePath = "/actions/main/in/PhoneHome";
    private const string _phoneRecentsPath = "/actions/main/in/PhoneRecents";
    private const string _phoneControlPanelPath = "/actions/main/in/PhoneControlPanel";
    private const string _phoneScreenshotPath = "/actions/main/in/PhoneScreenshot";
    private const string _phoneGrabPath = "/actions/main/in/PhoneOverlayGrab";
    private const string _phoneScalePath = "/actions/main/in/PhoneOverlayScale";
    private const string _phoneTouchPath = "/actions/main/in/PhoneOverlayTouch";
    private const string _phonePointerPosePath = "/actions/pointer/in/PhonePointerPose";
    private const string _menuDismissPath = "/actions/pointer/in/MenuDismiss";
    private const string _phoneButtonStatePath =
        "/actions/phonebuttonstate/in/AnyPhoneInputPressed";
    private const int _pollIntervalMilliseconds = 8;
    private const int _maximumCommandsPerPoll = 8;
    private readonly object _openVrGate;
    private OpenVrControllerHand _controllerHand;
    // Capacity one, reject concurrent requests; the input worker owns application.
    private HandChange? _pendingHandChange;
    private long _handSwitchRequests;
    private long _handSwitchCompleted;
    private long _handSwitchRejected;

    internal (long Requested, long Completed, long Rejected) HandSwitchMetrics
    {
        get { lock (_openVrGate) { return (_handSwitchRequests, _handSwitchCompleted, _handSwitchRejected); } }
    }
    private string _controllerInputPath;
    private string _playspaceInputPath;
    private readonly object _stateGate = new();
    private readonly OpenVrBindingHealthMonitor _bindingHealth = new();
    private readonly OpenVrPhoneActionSetGate _phoneActionSetGate = new();
    private readonly OpenVrGrabAwakePolicy _grabAwakePolicy = new();
    private OpenVrPointerPoseResolver _pointerPoseResolver = new();
    private readonly OpenVrPointerSmoother _pointerSmoother = new();
    private readonly OpenVrPointerOverlayRenderer _pointerRenderer;
    private readonly OpenVrPhoneBackface _backface;
    private readonly OpenVrPhoneMenuState _menu = new();
    private readonly OpenVrPhoneMenuView _menuView;
    private bool _presentationStopped;
    private volatile bool _phonePresented;
    private int _appliedInputRevision = -1;
    private readonly OpenVrPlacementStore _placementStore;
    private readonly OpenVrPhoneTouchState _touchState = new();
    private readonly CVRSystem _system;
    private readonly CVROverlay _overlay;
    private readonly ulong _overlayHandle;
    private readonly float _initialDistanceMeters;
    private readonly Action _submitVideoTexture;
    private readonly ManualResetEventSlim _stopSignal = new(false);
    private readonly Thread _worker;
    private readonly SmoothScrollGesture _smoothScroll = new();
    private readonly VRActiveActionSet_t[] _activeActionSets = new VRActiveActionSet_t[6];
    private bool _inputOverridesAvailable;
    private readonly TrackedDevicePose_t[] _trackedPoses =
        new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
    private HmdMatrix34_t _overlayTransform;
    private HmdMatrix34_t _grabRelativeTransform;
    private OpenVrPhoneSurface _grabSurface;
    private bool _grabHidden;
    private HmdMatrix34_t GrabRoot => _grabHidden ? _recalledHead : _overlayTransform;
    private ulong _actionSet;
    private ulong _pointerActionSet;
    private ulong _phoneButtonStateActionSet;
    private ulong _leftSpaceDragAction;
    private ulong _rightSpaceDragAction;
    private ulong _resetOffsetsAction;
    private ulong _phoneBackAction;
    private ulong _phoneHomeAction;
    private ulong _phoneRecentsAction;
    private ulong _phoneControlPanelAction;
    private ulong _phoneScreenshotAction;
    private ulong _phoneGrabAction;
    private ulong _phoneScaleAction;
    private ulong _phoneTouchAction;
    private ulong _phonePointerPoseAction;
    private ulong _menuDismissAction;
    private ulong _menuRecallAction;
    private readonly OpenVrRecallInputLifetime _recallLifetime = new();
    private volatile int _phoneLockState;
    private HmdMatrix34_t _recalledHead;
    private ulong _phoneButtonStateAction;
    private ulong _controllerInputSource = OpenVR.k_ulInvalidInputValueHandle;
    private ulong _playspaceInputSource = OpenVR.k_ulInvalidInputValueHandle;
    private IDisposable? _actionUpdateLease;
    private bool _previousBack;
    private bool _previousHome;
    private bool _previousRecents;
    private bool _previousControlPanel;
    private bool _previousScreenshot;
    private bool _previousGrab;
    private long _bindingRevision = -1;
    private bool _waitForBindingRelease;
    private bool _phoneActionsEnabled;
    private bool _grabbed;
    private bool _worldAnchored;
    private bool _bindingHealthStarted;
    private float _baseWidthMeters = 0.65f;
    private float _appliedWidthMeters = float.NaN;
    private float _frameAspectRatio = 1f;
    private float _scaleFactor = 1f;
    private float _grabUvX = 0.5f;
    private float _grabUvY = 0.5f;
    private long _lastGrabTimestamp;
    private volatile bool _keepAwakeWhileGrabbed;
    private volatile bool _unlockKeypadEnabled;
    private volatile bool _screenGuardEnabled;
    private volatile bool _screenGuardFaulted;

    public void SetScreenGuard(bool enabled, bool faulted)
    {
        _screenGuardEnabled = enabled;
        _screenGuardFaulted = faulted;
    }
    private volatile bool _frameReady;
    private string? _headsetModel;
    private string? _controllerType;
    private uint _controllerDeviceIndex = OpenVR.k_unTrackedDeviceIndexInvalid;
    private bool _pointerPoseBound;
    private bool _pointerPoseActive;
    private bool _touchBound;
    private bool _touchActive;
    private bool _grabBound;
    private bool _grabActive;
    private bool _scaleBound;
    private bool _scaleActive;
    private volatile bool _stopping;
    private bool _disposed;

    public OpenVrPhoneInteraction(
        object openVrGate,
        CVRSystem system,
        CVROverlay overlay,
        ulong overlayHandle,
        float initialDistanceMeters,
        Action submitVideoTexture, OpenVrVideoSettingsChannel? videoSettings = null)
    {
        _menu.VideoSettings = videoSettings;
        _openVrGate = openVrGate;
        _system = system;
        _overlay = overlay;
        _overlayHandle = overlayHandle;
        _initialDistanceMeters = initialDistanceMeters;
        _submitVideoTexture = submitVideoTexture;
        _controllerHand = OpenVrControllerPreferences.Load();
        _controllerInputPath = OpenVrControllerHandRouting.InputSourcePath(_controllerHand);
        OpenVrControllerHand playspaceHand = OpenVrControllerHandRouting.Opposite(_controllerHand);
        _playspaceInputPath = OpenVrControllerHandRouting.InputSourcePath(playspaceHand);
        _pointerRenderer = new OpenVrPointerOverlayRenderer(overlay);
        _backface = new OpenVrPhoneBackface(overlay);
        _menuView = new OpenVrPhoneMenuView(overlay);
        using (CancellationTokenSource placementLoad = new(TimeSpan.FromSeconds(2)))
        {
            OpenVrSavedPlacement? saved = OpenVrPlacementStore.LoadAsync(OpenVrPlacementStore.DefaultPath, placementLoad.Token)
                .GetAwaiter().GetResult();
            _scaleFactor = saved?.Scale ?? 1f;
        }
        Snapshot = new OpenVrPhoneInteractionSnapshot(
            false,
            false,
            false,
            false,
            OpenVrReasonCodes.InputStarting,
            "正在准备 SteamVR 手柄输入");

        SetInitialTransform();
        _placementStore = new OpenVrPlacementStore(OpenVrPlacementStore.DefaultPath);
        _worker = new Thread(Run)
        {
            IsBackground = true,
            Name = "VRPhoneScreen SteamVR input",
        };
        _worker.Start();
    }

    public OpenVrPhoneInteractionSnapshot Snapshot { get; private set; }

    public OpenVrBindingHealthSnapshot BindingHealth => _bindingHealth.Snapshot;
    public bool PhonePresented => _phonePresented;

    public event OpenVrPhoneInputSink? PhoneInputReceived;

    public async ValueTask SwitchControllerHandAsync(OpenVrControllerHand hand, CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        HandChange request = new(hand, new(TaskCreationOptions.RunContinuationsAsynchronously), deadline.Token);
        if (!Monitor.TryEnter(_openVrGate, TimeSpan.FromMilliseconds(250)))
        {
            throw new InvalidOperationException("SteamVR input is busy.");
        }
        try
        {
            ObjectDisposedException.ThrowIf(_disposed || _stopping, this);
            _handSwitchRequests++;
            if (_pendingHandChange is not null) { _handSwitchRejected++; throw new InvalidOperationException("Hand switch already pending."); }
            _pendingHandChange = request;
        }
        finally { Monitor.Exit(_openVrGate); }
        try { await request.Completion.Task.WaitAsync(deadline.Token).ConfigureAwait(false); }
        finally
        {
            Interlocked.CompareExchange(ref _pendingHandChange, null, request);
        }
    }

    private sealed record HandChange(OpenVrControllerHand Hand, TaskCompletionSource Completion, CancellationToken Token);

    private void ApplyPendingHandChange()
    {
        HandChange? change = _pendingHandChange;
        if (change is null) { return; }
        _pendingHandChange = null;
        if (change.Token.IsCancellationRequested) { change.Completion.TrySetCanceled(change.Token); return; }
        OpenVrControllerHand previous = _controllerHand;
        try
        {
            CancelPointer();
            CancelSmoothScroll();
            _grabbed = false;
            _phoneActionsEnabled = false;
            _previousGrab = true;
            _phoneActionSetGate.Reset();
            _pointerSmoother.Reset();
            _pointerPoseResolver = new();
            _pointerRenderer.Hide();
            _menu.ResetInput();
            SetHandRouting(change.Hand);
            foreach (ref VRActiveActionSet_t set in _activeActionSets.AsSpan()) { set.nPriority = 0; }
            Check(OpenVR.Input.UpdateActionState(_activeActionSets, (uint)Marshal.SizeOf<VRActiveActionSet_t>()), "切换惯用手输入");
            ETrackedControllerRole applied = ETrackedControllerRole.Invalid;
            Check(OpenVR.Input.GetDominantHand(ref applied), "确认惯用手输入");
            if (applied != OpenVrRuntimeHost.InputHandRole(change.Hand)) { throw new InvalidOperationException("Hand switch was not applied."); }
            change.Token.ThrowIfCancellationRequested();
            OpenVrRuntimeHost.ActionUpdates.PublishPlayspaceInput(default);
            _handSwitchCompleted++;
            change.Completion.TrySetResult();
        }
        catch (Exception exception)
        {
            _controllerHand = previous;
            _controllerInputPath = OpenVrControllerHandRouting.InputSourcePath(previous);
            _playspaceInputPath = OpenVrControllerHandRouting.InputSourcePath(OpenVrControllerHandRouting.Opposite(previous));
            change.Completion.TrySetException(exception);
            SetHandRouting(previous);
            throw;
        }
    }

    private void SetHandRouting(OpenVrControllerHand hand)
    {
        Check(OpenVrRuntimeHost.SetInputHand(hand), "切换惯用手");
        string phonePath = OpenVrControllerHandRouting.InputSourcePath(hand);
        string spacePath = OpenVrControllerHandRouting.InputSourcePath(OpenVrControllerHandRouting.Opposite(hand));
        ulong phone = 0, space = 0;
        Check(OpenVR.Input.GetInputSourceHandle(phonePath, ref phone), "手机操作手");
        Check(OpenVR.Input.GetInputSourceHandle(spacePath, ref space), "空间拖拽手");
        _controllerHand = hand;
        _controllerInputPath = phonePath;
        _playspaceInputPath = spacePath;
        _controllerInputSource = phone;
        _playspaceInputSource = space;
        _controllerDeviceIndex = OpenVR.k_unTrackedDeviceIndexInvalid;
        _controllerType = null;
        for (int index = 0; index < _activeActionSets.Length; index++)
        {
            _activeActionSets[index].ulRestrictedToDevice = index == 0 ? space : phone;
        }
    }

    public void ConfigureLockScreenFeatures(
        bool keepAwakeWhileGrabbed,
        bool unlockKeypadEnabled)
    {
        _keepAwakeWhileGrabbed = keepAwakeWhileGrabbed;
        _unlockKeypadEnabled = unlockKeypadEnabled;
    }

    public OpenVrBindingResult OpenBindingUi()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        OpenVrPhoneInteractionSnapshot snapshot = GetSnapshot();
        if (!snapshot.InputReady || _actionSet == OpenVR.k_ulInvalidActionSetHandle)
        {
            return new OpenVrBindingResult(false, snapshot.ReasonCode, snapshot.Message);
        }

        lock (_openVrGate)
        {
            return OpenVrInputManifest.OpenBindingUi(_actionSet);
        }
    }

    public void SetPhoneLocked(bool? locked) => _phoneLockState = locked switch { true => 2, false => 1, _ => 0 };

    public void UpdateFrameGeometry(float baseWidthMeters, int frameWidth, int frameHeight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(baseWidthMeters, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(frameWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(frameHeight, 1);
        _baseWidthMeters = baseWidthMeters;
        _frameAspectRatio = frameWidth / (float)frameHeight;
        _frameReady = true;
        ApplyOverlayWidth();
    }

    // Both callers hold ApiGate. Visibility has one owner even though video and
    // input are independently scheduled. Video never unconditionally shows it.
    public void PrepareVideoSubmission()
    {
        // A new video frame can present the UI even while input bindings are
        // starting/reconnecting. Rendering never depends on a successful click.
        _system.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0, _trackedPoses);
        EnsureWorldAnchored();
        RenderPresentation(OpenVrRuntimeHost.ActionUpdates.Read());
        if (!_menu.ControlsVisible)
        {
            _menuView.Hide();
            _backface.Update(false, _overlayTransform, CurrentWidthMeters, _frameAspectRatio);
            _pointerRenderer.Hide();
        }
    }

    public void StopPresentation()
    {
        _presentationStopped = true;
        _menu.SetEnvironment(false, _menu.DashboardVisible, _unlockKeypadEnabled);
        _ = _overlay.HideOverlay(_overlayHandle);
        _phonePresented = false;
        _menuView.Hide();
        _backface.Update(false, _overlayTransform, CurrentWidthMeters, _frameAspectRatio);
        _pointerRenderer.Hide();
    }

    private void UpdatePresentationEnvironment()
    {
        bool headValid = _trackedPoses[OpenVR.k_unTrackedDeviceIndex_Hmd].bPoseIsValid;
        _menu.SetEnvironment(_frameReady && _worldAnchored && headValid && !_presentationStopped,
            _overlay.IsDashboardVisible(), _unlockKeypadEnabled);
        _menu.SetLocked(_phoneLockState switch { 2 => true, 1 => false, _ => null });
    }

    private void SynchronizePhoneVisibility()
    {
        if (_phonePresented == _menu.PhoneVisible) { return; }
        Check(_menu.PhoneVisible ? _overlay.ShowOverlay(_overlayHandle) : _overlay.HideOverlay(_overlayHandle),
            "同步手机浮窗显示状态");
        _phonePresented = _menu.PhoneVisible;
        if (_phonePresented) { _submitVideoTexture(); }
    }

    private void RenderPresentation(OpenVrSharedPlayspaceInput playspace)
    {
        UpdatePresentationEnvironment();
        _menu.SynchronizeSettings(playspace);
        if (!_phonePresented && _menu.PhoneVisible)
        {
            // Re-entry includes manual show, dashboard return and tracking
            // recovery. Scale survives, but position always uses the current head.
            _overlayTransform = OpenVrPhonePlacement.InFrontOf(
                _trackedPoses[OpenVR.k_unTrackedDeviceIndex_Hmd].mDeviceToAbsoluteTracking, _initialDistanceMeters);
            Check(_overlay.SetOverlayTransformAbsolute(_overlayHandle,
                ETrackingUniverseOrigin.TrackingUniverseStanding, ref _overlayTransform), "将手机浮窗放到当前头显前方");
        }
        _menuView.Render(_menu, _overlayTransform, CurrentWidthMeters, _frameAspectRatio,
            _trackedPoses[OpenVR.k_unTrackedDeviceIndex_Hmd].mDeviceToAbsoluteTracking, playspace);
        SynchronizePhoneVisibility();
        _backface.Update(_phonePresented, _overlayTransform, CurrentWidthMeters, _frameAspectRatio, _menu.OpacityPercent / 100f);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopping = true;
        lock (_openVrGate)
        {
            _pendingHandChange?.Completion.TrySetException(new ObjectDisposedException(nameof(OpenVrPhoneInteraction)));
            _pendingHandChange = null;
        }
        _stopSignal.Set();
        bool workerStopped = _worker.Join(TimeSpan.FromSeconds(2));
        if (!workerStopped)
        {
            Publish(Snapshot with
            {
                InputReady = false,
                ReasonCode = OpenVrReasonCodes.InputStopTimeout,
                Message = "SteamVR 手柄输入线程未在期限内停止",
            });
        }

        if (_touchState.TryCancel(out OpenVrPhoneInputCommand cancelPointer))
        {
            PhoneInputReceived?.Invoke(cancelPointer);
        }

        CancelSmoothScroll();

        lock (_openVrGate)
        {
            _pointerRenderer.Dispose();
            _backface.Dispose();
            _menuView.Dispose();
            QueuePlacement();
        }
        _placementStore.Dispose();

        Publish(OpenVrPhoneInteractionSnapshot.Stopped);
        if (workerStopped)
        {
            _stopSignal.Dispose();
        }
    }

    private void Run()
    {
        while (!_stopping)
        {
            bool inputInitialized = false;
            try
            {
                InitializeInput();
                inputInitialized = true;
                while (!_stopping)
                {
                    if (!PollInput())
                    {
                        break;
                    }

                    if (_stopSignal.Wait(_pollIntervalMilliseconds))
                    {
                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                ReleaseActionUpdateLease();
                long now = Environment.TickCount64;
                _bindingHealth.Advance(now);
                if (!inputInitialized)
                {
                    _bindingHealth.RecordInitializationFailure(now);
                }

                CancelPointer();
                CancelSmoothScroll();
                _phoneActionSetGate.Reset();
                _phoneActionsEnabled = false;
                _grabbed = false;
                lock (_openVrGate)
                {
                    _pointerRenderer.Hide();
                    _menu.ResetInput();
                }

                Publish(new OpenVrPhoneInteractionSnapshot(
                    false,
                    _worldAnchored,
                    false,
                    false,
                    OpenVrReasonCodes.InputReconnecting,
                    "SteamVR 手柄输入暂时不可用，正在重试",
                    DiagnosticExceptionType: exception.GetType().FullName,
                    DiagnosticExceptionMessage: exception.Message));
                if (_stopSignal.Wait(TimeSpan.FromSeconds(1)))
                {
                    break;
                }
            }
        }

        ReleaseActionUpdateLease();
    }

    private void CancelPointer()
    {
        if (!_touchState.TryCancel(out OpenVrPhoneInputCommand cancelPointer))
        {
            return;
        }

        PhoneInputReceived?.Invoke(cancelPointer);
    }

    private void ReleaseActionUpdateLease()
    {
        Interlocked.Exchange(ref _actionUpdateLease, null)?.Dispose();
    }

    private void InitializeInput()
    {
        lock (_openVrGate)
        {
            if (!_bindingHealthStarted)
            {
                _bindingHealth.Begin(Environment.TickCount64);
                _bindingHealthStarted = true;
            }

            EVRInputError manifestError = OpenVrRuntimeHost.EnsureActionManifestSubmitted();
            if (manifestError != EVRInputError.IPCError)
            {
                Check(manifestError, "动作清单");
            }

            _actionSet = OpenVrInputManifest.GetActionSet();
            Check(OpenVrRuntimeHost.SetInputHand(_controllerHand), "应用惯用手");
            _pointerActionSet = OpenVrInputManifest.GetPointerActionSet();
            _phoneButtonStateActionSet = OpenVrInputManifest.GetPhoneButtonStateActionSet();
            Check(OpenVR.Input.GetActionHandle(
                _leftSpaceDragPath,
                ref _leftSpaceDragAction), "左手空间拖拽");
            Check(OpenVR.Input.GetActionHandle(
                _rightSpaceDragPath,
                ref _rightSpaceDragAction), "右手空间拖拽");
            Check(OpenVR.Input.GetActionHandle(
                _resetOffsetsPath,
                ref _resetOffsetsAction), "重置空间偏移");
            Check(OpenVR.Input.GetActionHandle(_phoneBackPath, ref _phoneBackAction), "手机返回");
            Check(OpenVR.Input.GetActionHandle(_phoneHomePath, ref _phoneHomeAction), "手机桌面");
            Check(OpenVR.Input.GetActionHandle(_phoneRecentsPath, ref _phoneRecentsAction), "最近任务");
            Check(OpenVR.Input.GetActionHandle(
                _phoneControlPanelPath,
                ref _phoneControlPanelAction), "控制栏");
            Check(OpenVR.Input.GetActionHandle(
                _phoneScreenshotPath,
                ref _phoneScreenshotAction), "截屏");
            Check(OpenVR.Input.GetActionHandle(_phoneGrabPath, ref _phoneGrabAction), "抓取浮窗");
            Check(OpenVR.Input.GetActionHandle(_phoneScalePath, ref _phoneScaleAction), "调整远近");
            Check(OpenVR.Input.GetActionHandle(_phoneTouchPath, ref _phoneTouchAction), "触控手机");
            Check(OpenVR.Input.GetActionHandle(
                _phonePointerPosePath,
                ref _phonePointerPoseAction), "手机指针姿态");
            Check(OpenVR.Input.GetActionHandle(_menuDismissPath, ref _menuDismissAction), "菜单外点击");
            Check(OpenVR.Input.GetActionHandle("/actions/phonerecall/in/RecallMenu", ref _menuRecallAction), "截图键唤回菜单");
            Check(OpenVR.Input.GetActionHandle(
                _phoneButtonStatePath,
                ref _phoneButtonStateAction), "手机输入原始状态");
            Check(
                OpenVR.Input.GetInputSourceHandle(
                    _controllerInputPath,
                    ref _controllerInputSource),
                "手机操作手输入源");
            Check(
                OpenVR.Input.GetInputSourceHandle(
                    _playspaceInputPath,
                    ref _playspaceInputSource),
                "空间拖拽手输入源");
            _activeActionSets[0].ulActionSet = _actionSet;
            _activeActionSets[0].ulRestrictedToDevice = _playspaceInputSource;
            _activeActionSets[1].ulActionSet = _pointerActionSet;
            _activeActionSets[1].ulRestrictedToDevice = _controllerInputSource;
            _activeActionSets[2].ulActionSet = _actionSet;
            _activeActionSets[2].ulRestrictedToDevice = _controllerInputSource;
            _activeActionSets[3].ulActionSet = _phoneButtonStateActionSet;
            _activeActionSets[3].ulRestrictedToDevice = _controllerInputSource;

            ulong captureSet = 0;
            Check(OpenVR.Input.GetActionSetHandle(OpenVrInputCaptureBindings.ActionSet, ref captureSet), "手机输入阻断动作集");
            _activeActionSets[4].ulActionSet = captureSet;
            _activeActionSets[4].ulRestrictedToDevice = _controllerInputSource;
            ulong recallSet = 0;
            Check(OpenVR.Input.GetActionSetHandle("/actions/phonerecall", ref recallSet), "隐藏时唤回动作集");
            _activeActionSets[5].ulActionSet = recallSet;
            _activeActionSets[5].ulRestrictedToDevice = _controllerInputSource;

            EVRSettingsError settingsError = EVRSettingsError.None;
            OpenVR.Settings.SetBool(
                OpenVR.k_pch_SteamVR_Section,
                OpenVR.k_pch_SteamVR_AllowGlobalActionSetPriority,
                true,
                ref settingsError);
            _inputOverridesAvailable = settingsError == EVRSettingsError.None &&
                OpenVR.Settings.GetBool(OpenVR.k_pch_SteamVR_Section,
                    OpenVR.k_pch_SteamVR_AllowGlobalActionSetPriority, ref settingsError) &&
                settingsError == EVRSettingsError.None;

            _actionUpdateLease ??= OpenVrRuntimeHost.ActionUpdates.AcquirePhonePoller();
        }

        Publish(new OpenVrPhoneInteractionSnapshot(
            true,
            _worldAnchored,
            false,
            false,
            OpenVrReasonCodes.InputReady,
            "SteamVR 手柄输入已就绪"));
    }

    private bool PollInput()
    {
        Span<OpenVrPhoneInputCommand> commands =
            stackalloc OpenVrPhoneInputCommand[_maximumCommandsPerPoll];
        int commandCount = 0;
        bool hovered;
        bool grab;
        bool touch;
        InputAnalogActionData_t scale;
        HmdMatrix34_t controllerPose;
        bool validControllerPose;

        lock (_openVrGate)
        {
            ApplyPendingHandChange();
            EnsureWorldAnchored();
            OpenVrPhoneInteractionSnapshot previousSnapshot = GetSnapshot();
            validControllerPose = TryGetControllerPose(out controllerPose);
            OpenVrSharedPlayspaceInput sharedInput = OpenVrRuntimeHost.ActionUpdates.Read();
            RenderPresentation(sharedInput);
            if (_bindingRevision != OpenVrRuntimeHost.Events.BindingRevision)
            {
                _bindingRevision = OpenVrRuntimeHost.Events.BindingRevision;
                _ = OpenVrRuntimeHost.SetInputHand(_controllerHand);
                _waitForBindingRelease = true;
                _phoneActionsEnabled = false;
                _phoneActionSetGate.Reset();
                StopSmoothScroll(commands, ref commandCount);
                if (_touchState.TryCancel(out OpenVrPhoneInputCommand cancel)) { commands[commandCount++] = cancel; }
                if (_grabbed) { QueuePlacement(); }
                _grabbed = false;
                _menu.ResetInput();
            }
            if (!_menu.ControlsVisible || _appliedInputRevision != _menu.InputRevision ||
                (_grabbed && !_menu.IsSurfaceVisible(_grabSurface)))
            {
                _appliedInputRevision = _menu.InputRevision;
                _phoneActionsEnabled = false;
                _phoneActionSetGate.Reset();
                if (_grabbed) { QueuePlacement(); }
                _grabbed = false;
                _pointerRenderer.Hide();
                StopSmoothScroll(commands, ref commandCount);
                if (_touchState.TryCancel(out OpenVrPhoneInputCommand cancel)) { commands[commandCount++] = cancel; }
            }
            bool playspaceDashboard = _menu.DashboardVisible;
            // Playspace drag/reset observe input without overriding the scene app.
            _activeActionSets[0].nPriority = 0;
            _activeActionSets[0].ulRestrictedToDevice = _playspaceInputSource;
            // Ordinary local priority lets outside-click dismiss observe the bound
            // touch input without taking that input away from a scene application.
            _activeActionSets[1].nPriority = 1;
            _activeActionSets[1].ulRestrictedToDevice = _controllerInputSource;
            _activeActionSets[2].nPriority = _phoneActionsEnabled
                ? OpenVR.k_nActionSetOverlayGlobalPriorityMin + 2
                : 0;
            _activeActionSets[2].ulRestrictedToDevice = _controllerInputSource;
            _activeActionSets[3].nPriority = _recallLifetime.Finishing && !_menu.DashboardVisible ? 2 : _phoneActionsEnabled
                ? 0
                : previousSnapshot.Hovered && _menu.ControlsVisible
                    ? OpenVR.k_nActionSetOverlayGlobalPriorityMin + 1
                    : 1;
            _activeActionSets[3].ulRestrictedToDevice = _controllerInputSource;
            _activeActionSets[4].nPriority = CapturePriority(_inputOverridesAvailable && _menu.ControlsVisible,
                validControllerPose, previousSnapshot.Hovered, _grabbed, _recallLifetime.Finishing);
            if (_waitForBindingRelease || OpenVrRuntimeHost.Events.BindingChanging)
            { _activeActionSets[2].nPriority = _activeActionSets[4].nPriority = 0; }
            // The observer at local priority 1 shares this physical button.
            // Hidden recall must outrank it without capturing scene input; once
            // visible, wait for native release before yielding to phone actions.
            _activeActionSets[5].nPriority = RecallPriority(_menu.PhoneHidden, _menu.DashboardVisible, _recallLifetime.Finishing);
            Check(
                OpenVR.Input.UpdateActionState(
                    _activeActionSets,
                    (uint)Marshal.SizeOf<VRActiveActionSet_t>()),
                "更新动作状态");
            if (_waitForBindingRelease || OpenVrRuntimeHost.Events.BindingChanging)
            {
                InputAnalogActionData_t heldScale = ReadAnalog(_phoneScaleAction);
                bool held = ReadDigital(_phoneButtonStateAction) || ReadDigital(_phoneGrabAction) || ReadDigital(_phoneTouchAction) ||
                    ReadDigital(_menuDismissAction) || ReadDigital(_menuRecallAction) || Math.Abs(heldScale.x) > 0.15f || Math.Abs(heldScale.y) > 0.15f;
                _waitForBindingRelease = OpenVrRuntimeHost.Events.BindingChanging || _menu.DashboardVisible || !validControllerPose || held;
                _previousBack = _previousHome = _previousRecents = _previousControlPanel = _previousScreenshot = _previousGrab = false;
                _pointerRenderer.Hide();
                Publish(previousSnapshot with
                {
                    Hovered = false,
                    Grabbed = false,
                    ControllerPoseValid = validControllerPose,
                    ReasonCode = "OPENVR_BINDING_SWITCHING",
                    Message = "等待绑定切换或按键释放"
                });
                goto DispatchCommands;
            }
            // Read-only mirror of the user's screenshot binding while hidden,
            // without enabling off-screen Android actions or input capture.
            if (_menu.ProcessRecall(ReadDigital(_menuRecallAction)))
            {
                _recallLifetime.Recalled();
                if (_grabbed) { QueuePlacement(); }
                _grabbed = false;
                _phoneActionsEnabled = false;
                _phoneActionSetGate.Reset();
                _recalledHead = _trackedPoses[OpenVR.k_unTrackedDeviceIndex_Hmd].mDeviceToAbsoluteTracking;
                RenderPresentation(sharedInput);
            }
            bool rawPhoneInputActive = !_phoneActionsEnabled &&
                IsDigitalActionActive(_phoneButtonStateAction);
            bool rawPhoneInputPressed = !_phoneActionsEnabled &&
                ReadDigital(_phoneButtonStateAction);

            _recallLifetime.Observe(rawPhoneInputActive, rawPhoneInputPressed, _menu.DashboardVisible);

            OpenVrPlayspaceInputSample playspaceInput = OpenVrPlayspaceInputSample.Read(
                OpenVR.Input, _leftSpaceDragAction, _rightSpaceDragAction, _resetOffsetsAction,
                _playspaceInputSource, playspaceDashboard);
            // A Dashboard transition during this poll cannot turn a sample from
            // the suppressed action set into evidence of a physical release.
            playspaceInput = playspaceInput with
            {
                DashboardVisible = playspaceDashboard || _overlay.IsDashboardVisible(),
            };
            OpenVrRuntimeHost.ActionUpdates.PublishPlayspaceInput(playspaceInput);

            long now = Environment.TickCount64;
            bool bindingLoadFailed = DrainBindingEvents();
            if (bindingLoadFailed)
            {
                _bindingHealth.ObserveBindingLoadFailed(now);
            }

            _bindingHealth.Advance(now);
            InputPoseActionData_t pointerPose = ReadPose(_phonePointerPoseAction);
            if (_bindingHealth.ShouldProbe(now) && !_menu.DashboardVisible)
            {
                _pointerPoseBound = HasBinding(_phonePointerPoseAction);
                _pointerPoseActive = pointerPose.bActive;
                _touchBound = HasBinding(_phoneTouchAction);
                _touchActive = IsDigitalActionActive(_phoneTouchAction);
                _grabBound = HasBinding(_phoneGrabAction);
                _grabActive = IsDigitalActionActive(_phoneGrabAction);
                _scaleBound = HasBinding(_phoneScaleAction);
                _scaleActive = IsAnalogActionActive(_phoneScaleAction);
                _bindingHealth.RecordProbe(
                    now,
                    validControllerPose,
                    _pointerPoseBound && _pointerPoseActive &&
                    _touchBound && _grabBound &&
                    (!_phoneActionsEnabled || (_touchActive && _grabActive)));
            }

            OpenVrBindingHealthSnapshot bindingHealth = _bindingHealth.Snapshot;
            bool validPointerPose = pointerPose.bActive &&
                pointerPose.pose.bPoseIsValid &&
                pointerPose.pose.bDeviceIsConnected;
            bool validRayPose = _pointerPoseResolver.TryResolve(
                validPointerPose,
                pointerPose.pose.mDeviceToAbsoluteTracking,
                validControllerPose,
                controllerPose,
                out HmdMatrix34_t pointerTransform);
            VROverlayIntersectionResults_t rawHit = default;
            bool rawIntersection = validRayPose && _menu.PhoneVisible &&
                TryIntersect(pointerTransform, out rawHit);
            scale = ReadAnalog(_phoneScaleAction);
            grab = ReadDigital(_phoneGrabAction);
            if (OpenVrInteractionGate.ShouldEndGrab(
                grab,
                _grabbed,
                validControllerPose))
            {
                _grabbed = false;
                QueuePlacement();
            }

            VROverlayIntersectionResults_t hit = default;
            OpenVrPhoneMenuHit menuHit = default;
            bool geometricMenuHit = _menu.ControlsVisible && validRayPose && !_grabbed &&
                _menuView.TryPick(pointerTransform, _menu.KeypadEnabled, out menuHit, _menu.OpacityCaptured);
            bool directIntersection = rawIntersection && !_grabbed;
            if (directIntersection)
            {
                hit = _pointerSmoother.Update(rawHit, Environment.TickCount64);
            }
            else
            {
                _pointerSmoother.Reset();
            }

            bool geometricPhoneHit = OpenVrInteractionGate.IsPhoneHit(
                directIntersection && !geometricMenuHit,
                _grabbed);
            bool pointerVisible = false;
            if (geometricPhoneHit || geometricMenuHit)
            {
                GetViewerPosition(out float viewerX, out float viewerY, out float viewerZ);
                pointerVisible = _pointerRenderer.TryUpdate(
                    pointerTransform,
                    geometricMenuHit ? menuHit.Intersection : hit,
                    viewerX,
                    viewerY,
                    viewerZ);
            }
            else
            {
                _pointerRenderer.Hide();
            }

            hovered = OpenVrInteractionGate.IsVisiblePhoneTarget(
                geometricPhoneHit,
                pointerVisible,
                _grabbed);
            bool visibleMenuHit = geometricMenuHit && pointerVisible;
            bool beginGroupGrab = OpenVrInteractionGate.CanStartGrab(grab, _previousGrab, _grabbed,
                hovered || visibleMenuHit, validControllerPose) && !_menu.SuppressPhoneTouch;
            bool touchAction = ReadDigital(_phoneTouchAction);
            // The observer may dismiss a menu outside our ray, but never activate
            // a control or forward phone input before the regular neutral gate.
            bool menuPressed = touchAction || (!geometricMenuHit && ReadDigital(_menuDismissAction));
            if (beginGroupGrab) { _menu.ResetInput(); }
            _menu.SetScreenGuard(_screenGuardEnabled, _screenGuardFaulted);
            OpenVrMenuChange menuChange = beginGroupGrab ? default : _menu.ProcessInput(
                validRayPose && !_grabbed && (!geometricMenuHit || pointerVisible),
                visibleMenuHit, visibleMenuHit ? menuHit.Target : OpenVrMenuTarget.None,
                menuHit.SliderFraction, menuPressed, sharedInput);
            if (menuChange.Playspace is { } request)
            {
                OpenVrRuntimeHost.ActionUpdates.RequestControls(request.Enabled, request.Multiplier, request.FlingEnabled, request.MotionOptions);
            }
            if (menuChange.PhoneCommand is { } phoneCommand) { commands[commandCount++] = phoneCommand; }
            if (menuChange.OpacityChanged)
            {
                Check(_overlay.SetOverlayAlpha(_overlayHandle, _menu.OpacityPercent / 100f), "调整手机浮窗透明度");
            }
            if (menuChange.Consumed)
            {
                hovered = false;
                RenderPresentation(sharedInput);
            }
            if (menuChange.VisibilityChanged)
            {
                _grabbed = false;
                _phoneActionsEnabled = false;
                _phoneActionSetGate.Reset();
            }
            touch = hovered && touchAction && !_menu.SuppressPhoneTouch;

            if (hovered && !_menu.SuppressPhoneTouch)
            {
                float pointerX = Math.Clamp(hit.vUVs.v0, 0, 1);
                float pointerY = Math.Clamp(1f - hit.vUVs.v1, 0, 1);
                UpdateSmoothScroll(
                    scale,
                    !touch && !grab,
                    pointerX,
                    pointerY,
                    commands,
                    ref commandCount);
                if (_touchState.TryUpdate(
                    touch,
                    pointerX,
                    pointerY,
                    Environment.TickCount64,
                    out OpenVrPhoneInputCommand touchCommand))
                {
                    commands[commandCount++] = touchCommand;
                }
            }
            else
            {
                StopSmoothScroll(commands, ref commandCount);
                if (_touchState.TryRelease(out OpenVrPhoneInputCommand releasePointer))
                {
                    commands[commandCount++] = releasePointer;
                }
            }

            if (grab && !_previousGrab &&
                _touchState.TryCancel(out OpenVrPhoneInputCommand grabCancelPointer))
            {
                commands[commandCount++] = grabCancelPointer;
            }

            _ = UpdateGrab(
                controllerPose,
                validControllerPose,
                beginGroupGrab,
                visibleMenuHit ? menuHit.Intersection : hit,
                grab,
                scale,
                visibleMenuHit ? menuHit.Surface : OpenVrPhoneSurface.Phone);
            if (_grabbed) { RenderPresentation(sharedInput); }
            OpenVrGrabAwakeAction grabAwakeAction = _grabAwakePolicy.NextAction(
                _grabbed && _menu.PhoneVisible,
                _keepAwakeWhileGrabbed,
                now);
            if (grabAwakeAction != OpenVrGrabAwakeAction.None)
            {
                commands[commandCount++] = new OpenVrPhoneInputCommand(
                    grabAwakeAction == OpenVrGrabAwakeAction.Wake
                        ? PhoneInputCommandKind.WakeScreen
                        : PhoneInputCommandKind.UserActivity);
            }
            if (_grabbed)
            {
                _pointerSmoother.Reset();
                _pointerRenderer.Hide();
            }
            bool acceptsButtons = OpenVrInteractionGate.CanAcceptPhoneButtons(
                hovered && !_menu.SuppressPhoneTouch,
                _grabbed);
            AppendButtonCommand(
                _phoneBackAction,
                PhoneInputCommandKind.Back,
                acceptsButtons,
                ref _previousBack,
                commands,
                ref commandCount);
            AppendButtonCommand(
                _phoneHomeAction,
                PhoneInputCommandKind.Home,
                acceptsButtons,
                ref _previousHome,
                commands,
                ref commandCount);
            AppendButtonCommand(
                _phoneRecentsAction,
                PhoneInputCommandKind.RecentApps,
                acceptsButtons,
                ref _previousRecents,
                commands,
                ref commandCount);
            AppendButtonCommand(
                _phoneControlPanelAction,
                PhoneInputCommandKind.OpenControlPanel,
                acceptsButtons,
                ref _previousControlPanel,
                commands,
                ref commandCount);
            AppendButtonCommand(
                _phoneScreenshotAction,
                PhoneInputCommandKind.Screenshot,
                acceptsButtons,
                ref _previousScreenshot,
                commands,
                ref commandCount);
            _previousGrab = grab;
            _touchState.RecordTouchState(touch);
            bool anyHovered = hovered || visibleMenuHit;
            if (_recallLifetime.Finishing)
            {
                _phoneActionsEnabled = false;
                _phoneActionSetGate.Reset();
            }
            else if (_grabbed || _touchState.IsDown || (_menu.OpacityCaptured && validRayPose))
            {
                _phoneActionsEnabled = true;
            }
            else if (_phoneActionsEnabled)
            {
                if (!anyHovered)
                {
                    _phoneActionsEnabled = false;
                    _phoneActionSetGate.Reset();
                }
            }
            else if (_phoneActionSetGate.ShouldEnable(
                         anyHovered,
                         rawPhoneInputActive,
                         rawPhoneInputPressed))
            {
                _phoneActionsEnabled = true;
            }

            Publish(new OpenVrPhoneInteractionSnapshot(
                true,
                _worldAnchored,
                anyHovered,
                _grabbed,
                _menu.DashboardVisible ? "OPENVR_DASHBOARD_SUSPENDED" : !_inputOverridesAvailable
                    ? "OPENVR_INPUT_OVERRIDE_UNAVAILABLE"
                    : !_worldAnchored
                    ? OpenVrReasonCodes.WaitingForHmdPose
                    : bindingHealth.State == OpenVrBindingHealthState.Failed
                        ? bindingHealth.ReasonCode
                    : !validRayPose
                        ? OpenVrReasonCodes.PointerPoseWaiting
                    : OpenVrReasonCodes.InputReady,
                _menu.DashboardVisible ? "SteamVR 菜单已打开，浮窗暂时隐藏" : !_inputOverridesAvailable
                    ? "SteamVR 未允许覆盖游戏输入，请在开发者设置启用实验性浮窗输入覆盖"
                    : _worldAnchored
                    ? bindingHealth.State == OpenVrBindingHealthState.Failed
                        ? bindingHealth.Message
                        : !validRayPose
                        ? "正在等待已绑定的手柄射线姿态"
                        : _grabbed
                        ? "正在拖动手机浮窗"
                        : anyHovered
                            ? "手柄已指向手机控件"
                            : "SteamVR 手柄输入已就绪"
                    : "正在等待头显定位后固定手机浮窗",
                validControllerPose,
                _menu.KeypadVisible,
                HeadsetModel: _headsetModel,
                ControllerType: _controllerType,
                PointerPoseBound: _pointerPoseBound,
                PointerPoseActive: _pointerPoseActive,
                TouchBound: _touchBound,
                TouchActive: _touchActive,
                GrabBound: _grabBound,
                GrabActive: _grabActive,
                ScaleBound: _scaleBound,
                ScaleActive: _scaleActive));
        }

    DispatchCommands:
        for (int index = 0; index < commandCount; index++)
        {
            PhoneInputReceived?.Invoke(commands[index]);
        }

        return true;
    }

    internal static int RecallPriority(bool hidden, bool dashboard, bool finishing = false) =>
        (hidden || finishing) && !dashboard ? 2 : 0;

    internal static int CapturePriority(bool available, bool validController, bool visibleTarget, bool grabbed, bool finishingRecall = false) =>
        !finishingRecall && available && validController && (visibleTarget || grabbed) ? OpenVR.k_nActionSetOverlayGlobalPriorityMin : 0;

    private bool DrainBindingEvents()
    {
        OpenVrRuntimeHost.Events.Poll(_system);
        return OpenVrRuntimeHost.Events.TakeBindingFailure();
    }

    private static bool HasBinding(ulong action)
    {
        InputBindingInfo_t binding = default;
        uint bindingCount = 0;
        EVRInputError error = OpenVR.Input.GetActionBindingInfo(
            action,
            ref binding,
            (uint)Marshal.SizeOf<InputBindingInfo_t>(),
            1,
            ref bindingCount);
        return error == EVRInputError.None && bindingCount > 0;
    }

    private bool IsDigitalActionActive(ulong action)
    {
        InputDigitalActionData_t data = default;
        EVRInputError error = OpenVR.Input.GetDigitalActionData(
            action,
            ref data,
            (uint)Marshal.SizeOf<InputDigitalActionData_t>(),
            _controllerInputSource);
        return error == EVRInputError.None && data.bActive;
    }

    private bool IsAnalogActionActive(ulong action)
    {
        InputAnalogActionData_t data = default;
        EVRInputError error = OpenVR.Input.GetAnalogActionData(
            action,
            ref data,
            (uint)Marshal.SizeOf<InputAnalogActionData_t>(),
            _controllerInputSource);
        return error == EVRInputError.None && data.bActive;
    }

    private bool UpdateGrab(
        HmdMatrix34_t controllerPose,
        bool validControllerPose,
        bool hovered,
        VROverlayIntersectionResults_t hit,
        bool grab,
        InputAnalogActionData_t scale,
        OpenVrPhoneSurface surface)
    {
        bool started = false;
        if (OpenVrInteractionGate.CanStartGrab(
            grab,
            _previousGrab,
            _grabbed,
            hovered,
            validControllerPose))
        {
            _grabSurface = surface;
            _grabHidden = _menu.PhoneHidden;
            _grabRelativeTransform = OpenVrTransformMath.Multiply(
                OpenVrTransformMath.InverseRigid(controllerPose),
                GrabRoot);
            _grabUvX = Math.Clamp(hit.vUVs.v0, 0, 1);
            _grabUvY = Math.Clamp(hit.vUVs.v1, 0, 1);
            _grabbed = true;
            started = true;
            _lastGrabTimestamp = Environment.TickCount64;
        }
        if (!_grabbed)
        {
            return started;
        }

        long now = Environment.TickCount64;
        float elapsedSeconds = Math.Clamp((now - _lastGrabTimestamp) / 1000f, 0.001f, 0.05f);
        _lastGrabTimestamp = now;
        if (scale.bActive && MathF.Abs(scale.y) > 0.2f)
        {
            float previousWidth = CurrentWidthMeters;
            float scaleMultiplier = MathF.Exp(scale.y * elapsedSeconds * 1.25f);
            _scaleFactor = OpenVrPhoneScaleRange.Clamp(_scaleFactor * scaleMultiplier);
            float currentWidth = CurrentWidthMeters;
            if (MathF.Abs(previousWidth - currentWidth) > 0.0001f)
            {
                SetGrabRoot(OpenVrPhoneGroupLayout.AnchorScale(_grabSurface, GrabRoot,
                    previousWidth, currentWidth, _frameAspectRatio, _grabHidden, _grabUvX, _grabUvY));
                ApplyOverlayWidth();
                _grabRelativeTransform = OpenVrTransformMath.Multiply(
                    OpenVrTransformMath.InverseRigid(controllerPose),
                    GrabRoot);
            }
        }

        HmdMatrix34_t target = OpenVrTransformMath.Multiply(
            controllerPose,
            _grabRelativeTransform);
        SetGrabRoot(OpenVrTransformMath.Smooth(GrabRoot, target, elapsedSeconds));
        Check(
            _overlay.SetOverlayTransformAbsolute(
                _overlayHandle,
                ETrackingUniverseOrigin.TrackingUniverseStanding,
                ref _overlayTransform),
            "拖动手机浮窗");
        return started;
    }

    private void UpdateSmoothScroll(
        InputAnalogActionData_t analog,
        bool enabled,
        float pointerX,
        float pointerY,
        Span<OpenVrPhoneInputCommand> commands,
        ref int commandCount)
    {
        Span<SmoothScrollCommand> scrollCommands = stackalloc SmoothScrollCommand[2];
        int scrollCommandCount = _smoothScroll.Update(
            analog.bActive ? analog.y : 0,
            enabled && !_grabbed,
            pointerX,
            pointerY,
            Environment.TickCount64,
            scrollCommands);
        for (int index = 0; index < scrollCommandCount; index++)
        {
            SmoothScrollCommand command = scrollCommands[index];
            commands[commandCount++] = new OpenVrPhoneInputCommand(
                command.Kind,
                command.NormalizedX,
                command.NormalizedY);
        }
    }

    private void StopSmoothScroll(
        Span<OpenVrPhoneInputCommand> commands,
        ref int commandCount)
    {
        Span<SmoothScrollCommand> scrollCommands = stackalloc SmoothScrollCommand[1];
        int scrollCommandCount = _smoothScroll.Stop(scrollCommands);
        if (scrollCommandCount == 1)
        {
            SmoothScrollCommand command = scrollCommands[0];
            commands[commandCount++] = new OpenVrPhoneInputCommand(
                command.Kind,
                command.NormalizedX,
                command.NormalizedY);
        }
    }

    private float CurrentWidthMeters => _baseWidthMeters * _scaleFactor;

    private void ApplyOverlayWidth()
    {
        float widthMeters = CurrentWidthMeters;
        if (float.IsFinite(_appliedWidthMeters) &&
            MathF.Abs(widthMeters - _appliedWidthMeters) <= 0.0001f)
        {
            return;
        }

        Check(
            _overlay.SetOverlayWidthInMeters(_overlayHandle, widthMeters),
            "调整手机浮窗尺寸");
        _appliedWidthMeters = widthMeters;
    }

    private void SetGrabRoot(HmdMatrix34_t value)
    {
        if (_grabHidden)
        {
            HmdMatrix34_t delta = OpenVrTransformMath.Multiply(value, OpenVrTransformMath.InverseRigid(_recalledHead));
            _overlayTransform = OpenVrTransformMath.Multiply(delta, _overlayTransform);
            _recalledHead = value;
        }
        else { _overlayTransform = value; }
    }

    private void AppendButtonCommand(
        ulong action,
        PhoneInputCommandKind kind,
        bool acceptsButtons,
        ref bool previous,
        Span<OpenVrPhoneInputCommand> commands,
        ref int commandCount)
    {
        bool current = ReadDigital(action);
        if (OpenVrInteractionGate.ShouldEmitPhoneButton(acceptsButtons, current, previous))
        {
            commands[commandCount++] = new OpenVrPhoneInputCommand(kind);
        }

        previous = current;
    }

    private bool TryIntersect(HmdMatrix34_t pointer, out VROverlayIntersectionResults_t hit)
    {
        if (!_worldAnchored)
        {
            hit = default;
            return false;
        }

        bool intersects = OpenVrPhoneRaycaster.TryIntersect(
            pointer,
            _overlayTransform,
            CurrentWidthMeters,
            _frameAspectRatio,
            out hit);
        return intersects && OpenVrPhoneShellGeometry.ContainsScreenPoint(hit.vUVs.v0, hit.vUVs.v1, _frameAspectRatio);
    }

    private void SetInitialTransform()
    {
        HmdMatrix34_t relative = OpenVrTransformMath.Identity();
        relative.m11 = -_initialDistanceMeters;
        lock (_openVrGate)
        {
            _system.GetDeviceToAbsoluteTrackingPose(
                ETrackingUniverseOrigin.TrackingUniverseStanding,
                0,
                _trackedPoses);
            if (_trackedPoses[OpenVR.k_unTrackedDeviceIndex_Hmd].bPoseIsValid)
            {
                _overlayTransform = OpenVrTransformMath.Multiply(
                    _trackedPoses[OpenVR.k_unTrackedDeviceIndex_Hmd].mDeviceToAbsoluteTracking,
                    relative);
                Check(
                    _overlay.SetOverlayTransformAbsolute(
                        _overlayHandle,
                        ETrackingUniverseOrigin.TrackingUniverseStanding,
                        ref _overlayTransform),
                    "固定手机浮窗");
                _worldAnchored = true;
            }
            else
            {
                _overlayTransform = relative;
                Check(
                    _overlay.SetOverlayTransformTrackedDeviceRelative(
                        _overlayHandle,
                        OpenVR.k_unTrackedDeviceIndex_Hmd,
                        ref relative),
                    "设置手机浮窗初始位置");
            }
        }
    }

    private void EnsureWorldAnchored()
    {
        if (_worldAnchored)
        {
            return;
        }

        _system.GetDeviceToAbsoluteTrackingPose(
            ETrackingUniverseOrigin.TrackingUniverseStanding,
            0,
            _trackedPoses);
        if (!_trackedPoses[OpenVR.k_unTrackedDeviceIndex_Hmd].bPoseIsValid)
        {
            return;
        }

        _overlayTransform = OpenVrTransformMath.Multiply(
            _trackedPoses[OpenVR.k_unTrackedDeviceIndex_Hmd].mDeviceToAbsoluteTracking,
            _overlayTransform);
        Check(
            _overlay.SetOverlayTransformAbsolute(
                _overlayHandle,
                ETrackingUniverseOrigin.TrackingUniverseStanding,
                ref _overlayTransform),
            "固定手机浮窗");
        _worldAnchored = true;
    }

    private void QueuePlacement()
    {
        if (_frameReady)
        {
            _placementStore.Queue(new OpenVrSavedPlacement(_scaleFactor));
        }
    }

    private bool ReadDigital(ulong action)
    {
        return ReadDigital(action, _controllerInputSource);
    }

    private static bool ReadDigital(ulong action, ulong inputSource)
    {
        InputDigitalActionData_t data = default;
        EVRInputError error = OpenVR.Input.GetDigitalActionData(
            action,
            ref data,
            (uint)Marshal.SizeOf<InputDigitalActionData_t>(),
            inputSource);
        return error == EVRInputError.None && data.bActive && data.bState;
    }

    private InputAnalogActionData_t ReadAnalog(ulong action)
    {
        InputAnalogActionData_t data = default;
        _ = OpenVR.Input.GetAnalogActionData(
            action,
            ref data,
            (uint)Marshal.SizeOf<InputAnalogActionData_t>(),
            _controllerInputSource);
        return data;
    }

    private bool TryGetControllerPose(out HmdMatrix34_t pose)
    {
        pose = default;
        _system.GetDeviceToAbsoluteTrackingPose(
            ETrackingUniverseOrigin.TrackingUniverseStanding,
            0,
            _trackedPoses);
        _headsetModel ??= ReadTrackedDeviceProperty(
            OpenVR.k_unTrackedDeviceIndex_Hmd,
            ETrackedDeviceProperty.Prop_ModelNumber_String);
        ETrackedControllerRole role = OpenVrControllerHandRouting.TrackedRole(_controllerHand);
        uint controller = _system.GetTrackedDeviceIndexForControllerRole(role);
        if (controller == OpenVR.k_unTrackedDeviceIndexInvalid ||
            controller >= _trackedPoses.Length)
        {
            return false;
        }

        if (_controllerDeviceIndex != controller)
        {
            _controllerDeviceIndex = controller;
            _controllerType = ReadTrackedDeviceProperty(
                controller,
                ETrackedDeviceProperty.Prop_ControllerType_String);
        }

        TrackedDevicePose_t trackedPose = _trackedPoses[controller];
        if (!trackedPose.bPoseIsValid || !trackedPose.bDeviceIsConnected)
        {
            return false;
        }

        pose = trackedPose.mDeviceToAbsoluteTracking;
        return true;
    }

    private string? ReadTrackedDeviceProperty(
        uint deviceIndex,
        ETrackedDeviceProperty property)
    {
        ETrackedPropertyError error = ETrackedPropertyError.TrackedProp_Success;
        StringBuilder lengthProbe = new(1);
        uint length = _system.GetStringTrackedDeviceProperty(
            deviceIndex,
            property,
            lengthProbe,
            0,
            ref error);
        if (length <= 1 || error is not (
                ETrackedPropertyError.TrackedProp_Success or
                ETrackedPropertyError.TrackedProp_BufferTooSmall))
        {
            return null;
        }

        StringBuilder value = new((int)length);
        error = ETrackedPropertyError.TrackedProp_Success;
        _ = _system.GetStringTrackedDeviceProperty(
            deviceIndex,
            property,
            value,
            length,
            ref error);
        return error == ETrackedPropertyError.TrackedProp_Success && value.Length > 0
            ? value.ToString()
            : null;
    }

    private void GetViewerPosition(out float x, out float y, out float z)
    {
        TrackedDevicePose_t hmd = _trackedPoses[OpenVR.k_unTrackedDeviceIndex_Hmd];
        if (hmd.bPoseIsValid)
        {
            x = hmd.mDeviceToAbsoluteTracking.m3;
            y = hmd.mDeviceToAbsoluteTracking.m7;
            z = hmd.mDeviceToAbsoluteTracking.m11;
            return;
        }

        x = _overlayTransform.m3 + _overlayTransform.m2;
        y = _overlayTransform.m7 + _overlayTransform.m6;
        z = _overlayTransform.m11 + _overlayTransform.m10;
    }

    private void CancelSmoothScroll()
    {
        Span<SmoothScrollCommand> scrollCommands = stackalloc SmoothScrollCommand[1];
        if (_smoothScroll.Cancel(scrollCommands) == 1)
        {
            SmoothScrollCommand command = scrollCommands[0];
            PhoneInputReceived?.Invoke(new OpenVrPhoneInputCommand(
                command.Kind,
                command.NormalizedX,
                command.NormalizedY));
        }
    }

    private InputPoseActionData_t ReadPose(ulong action)
    {
        InputPoseActionData_t data = default;
        _ = OpenVR.Input.GetPoseActionDataForNextFrame(
            action,
            ETrackingUniverseOrigin.TrackingUniverseStanding,
            ref data,
            (uint)Marshal.SizeOf<InputPoseActionData_t>(),
            _controllerInputSource);
        return data;
    }

    private OpenVrPhoneInteractionSnapshot GetSnapshot()
    {
        lock (_stateGate)
        {
            return Snapshot;
        }
    }

    private void Publish(OpenVrPhoneInteractionSnapshot snapshot)
    {
        lock (_stateGate)
        {
            Snapshot = snapshot;
        }
    }

    private static void Check(EVRInputError error, string operation)
    {
        if (error != EVRInputError.None)
        {
            throw new InvalidOperationException($"{operation}失败：{error}");
        }
    }

    private static void Check(EVROverlayError error, string operation)
    {
        if (error != EVROverlayError.None)
        {
            throw new InvalidOperationException($"{operation}失败：{error}");
        }
    }
}
