namespace VRPhoneScreenOverlay.SteamVR;

/// <summary>
/// Stable reason codes raised by this module. Values are protocol: they are only ever
/// added, never re-spelled, and never localised.
/// </summary>
public static class OpenVrReasonCodes
{
    public const string PlayspaceWaitingForSteamVr = "OPENVR_PLAYSPACE_WAITING_FOR_STEAMVR";
    public const string StartupConfigured = "OPENVR_STARTUP_CONFIGURED";
    public const string StartupRegistrationFailed = "OPENVR_STARTUP_REGISTRATION_FAILED";
    public const string StartupRegistrationTimeout = "OPENVR_STARTUP_REGISTRATION_TIMEOUT";
    /// <summary><c>OPENVR_BINDING_DEFAULT_RESTORE_FAILED</c></summary>
    public const string BindingDefaultRestoreFailed = "OPENVR_BINDING_DEFAULT_RESTORE_FAILED";

    /// <summary><c>OPENVR_BINDING_LOAD_FAILED</c></summary>
    public const string BindingLoadFailed = "OPENVR_BINDING_LOAD_FAILED";

    /// <summary><c>OPENVR_BINDING_LOADING</c></summary>
    public const string BindingLoading = "OPENVR_BINDING_LOADING";

    /// <summary><c>OPENVR_BINDING_OVERLAY_ACTIVE</c></summary>
    public const string BindingOverlayActive = "OPENVR_BINDING_OVERLAY_ACTIVE";

    /// <summary><c>OPENVR_BINDING_READY</c></summary>
    public const string BindingReady = "OPENVR_BINDING_READY";

    /// <summary><c>OPENVR_BINDING_STOPPED</c></summary>
    public const string BindingStopped = "OPENVR_BINDING_STOPPED";

    /// <summary><c>OPENVR_BINDING_UI_FAILED</c></summary>
    public const string BindingUiFailed = "OPENVR_BINDING_UI_FAILED";

    /// <summary><c>OPENVR_BINDING_UI_OPENED</c></summary>
    public const string BindingUiOpened = "OPENVR_BINDING_UI_OPENED";

    /// <summary><c>D3D11_STEAMVR_ADAPTER_MISSING</c></summary>
    public const string D3d11SteamvrAdapterMissing = "D3D11_STEAMVR_ADAPTER_MISSING";

    /// <summary><c>D3D11_VIDEO_FRAME_TRUNCATED</c></summary>
    public const string D3d11VideoFrameTruncated = "D3D11_VIDEO_FRAME_TRUNCATED";

    /// <summary><c>D3D11_VIDEO_INITIALIZATION_FAILED</c></summary>
    public const string D3d11VideoInitializationFailed = "D3D11_VIDEO_INITIALIZATION_FAILED";

    /// <summary><c>D3D11_VIDEO_NV12_ODD_DIMENSIONS</c></summary>
    public const string D3d11VideoNv12OddDimensions = "D3D11_VIDEO_NV12_ODD_DIMENSIONS";

    /// <summary><c>D3D11_VIDEO_PIXEL_FORMAT_UNSUPPORTED</c></summary>
    public const string D3d11VideoPixelFormatUnsupported = "D3D11_VIDEO_PIXEL_FORMAT_UNSUPPORTED";

    /// <summary><c>D3D11_VIDEO_PRESENT_FAILED</c></summary>
    public const string D3d11VideoPresentFailed = "D3D11_VIDEO_PRESENT_FAILED";

    /// <summary><c>OPENVR_INITIALIZATION_FAILED</c></summary>
    public const string InitializationFailed = "OPENVR_INITIALIZATION_FAILED";

    /// <summary><c>OPENVR_INITIALIZATION_UNEXPECTED</c></summary>
    public const string InitializationUnexpected = "OPENVR_INITIALIZATION_UNEXPECTED";

    /// <summary><c>OPENVR_INPUT_NOT_READY</c></summary>
    public const string InputNotReady = "OPENVR_INPUT_NOT_READY";

    /// <summary><c>OPENVR_INPUT_READY</c></summary>
    public const string InputReady = "OPENVR_INPUT_READY";

    /// <summary><c>OPENVR_INPUT_RECONNECTING</c></summary>
    public const string InputReconnecting = "OPENVR_INPUT_RECONNECTING";

    /// <summary><c>OPENVR_INPUT_STARTING</c></summary>
    public const string InputStarting = "OPENVR_INPUT_STARTING";

    /// <summary><c>OPENVR_INPUT_STOP_TIMEOUT</c></summary>
    public const string InputStopTimeout = "OPENVR_INPUT_STOP_TIMEOUT";

    /// <summary><c>OPENVR_INPUT_STOPPED</c></summary>
    public const string InputStopped = "OPENVR_INPUT_STOPPED";

    /// <summary><c>OPENVR_INPUT_STOPPING</c></summary>
    public const string InputStopping = "OPENVR_INPUT_STOPPING";

    /// <summary><c>OPENVR_LOCAL_BINDING_ACTIVATED</c></summary>
    public const string LocalBindingActivated = "OPENVR_LOCAL_BINDING_ACTIVATED";

    /// <summary><c>OPENVR_LOCAL_BINDING_ACTIVATION_FAILED</c></summary>
    public const string LocalBindingActivationFailed = "OPENVR_LOCAL_BINDING_ACTIVATION_FAILED";

    /// <summary><c>OPENVR_LOCAL_BINDING_PREPARE_FAILED</c></summary>
    public const string LocalBindingPrepareFailed = "OPENVR_LOCAL_BINDING_PREPARE_FAILED";

    /// <summary><c>OPENVR_LOCAL_BINDING_PREPARED</c></summary>
    public const string LocalBindingPrepared = "OPENVR_LOCAL_BINDING_PREPARED";

    public const string LocalBindingDriverEnriched = "OPENVR_LOCAL_BINDING_DRIVER_ENRICHED";

    /// <summary><c>OPENVR_LOCAL_BINDING_UNCHANGED</c></summary>
    public const string LocalBindingUnchanged = "OPENVR_LOCAL_BINDING_UNCHANGED";

    /// <summary><c>OPENVR_NATIVE_LIBRARY_FAILED</c></summary>
    public const string NativeLibraryFailed = "OPENVR_NATIVE_LIBRARY_FAILED";

    /// <summary><c>OPENVR_OVERLAY_ALPHA_FAILED</c></summary>
    public const string OverlayAlphaFailed = "OPENVR_OVERLAY_ALPHA_FAILED";

    /// <summary><c>OPENVR_OVERLAY_CREATE_FAILED</c></summary>
    public const string OverlayCreateFailed = "OPENVR_OVERLAY_CREATE_FAILED";

    /// <summary><c>OPENVR_OVERLAY_HIDE_FAILED</c></summary>
    public const string OverlayHideFailed = "OPENVR_OVERLAY_HIDE_FAILED";

    /// <summary><c>OPENVR_OVERLAY_INPUT_FAILED</c></summary>
    public const string OverlayInputFailed = "OPENVR_OVERLAY_INPUT_FAILED";

    /// <summary><c>OPENVR_OVERLAY_INTERFACE_MISSING</c></summary>
    public const string OverlayInterfaceMissing = "OPENVR_OVERLAY_INTERFACE_MISSING";

    /// <summary><c>OPENVR_OVERLAY_SHOW_FAILED</c></summary>
    public const string OverlayShowFailed = "OPENVR_OVERLAY_SHOW_FAILED";

    /// <summary><c>OPENVR_OVERLAY_TEXTURE_FAILED</c></summary>
    public const string OverlayTextureFailed = "OPENVR_OVERLAY_TEXTURE_FAILED";

    /// <summary><c>OPENVR_OVERLAY_WIDTH_FAILED</c></summary>
    public const string OverlayWidthFailed = "OPENVR_OVERLAY_WIDTH_FAILED";

    /// <summary><c>PLAYSPACE_DRAG_DISABLED</c></summary>
    public const string PlayspaceDragDisabled = "PLAYSPACE_DRAG_DISABLED";

    /// <summary><c>PLAYSPACE_DRAG_ENABLED</c></summary>
    public const string PlayspaceDragEnabled = "PLAYSPACE_DRAG_ENABLED";

    /// <summary><c>PLAYSPACE_DRAG_ENABLED_CHANGED</c></summary>
    public const string PlayspaceDragEnabledChanged = "PLAYSPACE_DRAG_ENABLED_CHANGED";

    /// <summary><c>PLAYSPACE_DRAG_GESTURE_COMPLETED</c></summary>
    public const string PlayspaceDragGestureCompleted = "PLAYSPACE_DRAG_GESTURE_COMPLETED";

    /// <summary><c>PLAYSPACE_DRAG_GESTURE_STARTED</c></summary>
    public const string PlayspaceDragGestureStarted = "PLAYSPACE_DRAG_GESTURE_STARTED";

    /// <summary><c>PLAYSPACE_DRAG_MULTIPLIER_APPLIED</c></summary>
    public const string PlayspaceDragMultiplierApplied = "PLAYSPACE_DRAG_MULTIPLIER_APPLIED";

    /// <summary><c>PLAYSPACE_DRAG_MULTIPLIER_CHANGED</c></summary>
    public const string PlayspaceDragMultiplierChanged = "PLAYSPACE_DRAG_MULTIPLIER_CHANGED";

    /// <summary><c>PLAYSPACE_DRAG_READY</c></summary>
    public const string PlayspaceDragReady = "PLAYSPACE_DRAG_READY";

    /// <summary><c>PLAYSPACE_DRAG_SESSION_READY</c></summary>
    public const string PlayspaceDragSessionReady = "PLAYSPACE_DRAG_SESSION_READY";

    /// <summary><c>PLAYSPACE_DRAG_STARTING</c></summary>
    public const string PlayspaceDragStarting = "PLAYSPACE_DRAG_STARTING";

    /// <summary><c>PLAYSPACE_DRAG_STEAMVR_UNAVAILABLE</c></summary>
    public const string PlayspaceDragSteamvrUnavailable = "PLAYSPACE_DRAG_STEAMVR_UNAVAILABLE";

    /// <summary><c>PLAYSPACE_DRAG_STOPPED</c></summary>
    public const string PlayspaceDragStopped = "PLAYSPACE_DRAG_STOPPED";

    /// <summary><c>OPENVR_POINTER_POSE_WAITING</c></summary>
    public const string PointerPoseWaiting = "OPENVR_POINTER_POSE_WAITING";

    /// <summary><c>OPENVR_WAITING_FOR_HMD_POSE</c></summary>
    public const string WaitingForHmdPose = "OPENVR_WAITING_FOR_HMD_POSE";

    /// <summary><c>PLAYSPACE_DRAG_NOT_STARTED</c> — 空间拖拽从未启动，与已停止区分。</summary>
    public const string PlayspaceDragNotStarted = "PLAYSPACE_DRAG_NOT_STARTED";
    public const string PlayspaceOffsetsResetApplied = "PLAYSPACE_OFFSETS_RESET_APPLIED";
    public const string PlayspaceHeightResetApplied = "PLAYSPACE_HEIGHT_RESET_APPLIED";
    public const string PlayspaceHeightResetUnavailable = "PLAYSPACE_HEIGHT_RESET_UNAVAILABLE";

}
