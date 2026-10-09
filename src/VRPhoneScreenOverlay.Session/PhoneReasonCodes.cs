namespace VRPhoneScreenOverlay.Session;

/// <summary>
/// Stable reason codes raised by this module. Values are protocol: they are only ever
/// added, never re-spelled, and never localised.
/// </summary>
public static class PhoneReasonCodes
{
    public const string HandSwitchFailed = "PHONE_HAND_SWITCH_FAILED";
    public const string HandSwitchStarting = "PHONE_HAND_SWITCH_STARTING";
    public const string MediaAutoStartFailed = "PHONE_MEDIA_AUTO_START_FAILED";
    public const string MediaAutoStartCleanupFailed = "PHONE_MEDIA_AUTO_START_CLEANUP_FAILED";
    public const string MediaStartNotReady = "PHONE_MEDIA_START_NOT_READY";
    public const string MediaSwitchDeviceNotReady = "PHONE_MEDIA_SWITCH_DEVICE_NOT_READY";
    public const string MediaSwitchDeviceUnavailable = "PHONE_MEDIA_SWITCH_DEVICE_UNAVAILABLE";
    public const string MediaDeviceSelected = "PHONE_MEDIA_DEVICE_SELECTED";
    /// <summary><c>PHONE_CONTROL_COMMAND_SENT</c></summary>
    public const string ControlCommandSent = "PHONE_CONTROL_COMMAND_SENT";

    /// <summary><c>PHONE_CONTROL_DEVICE_NOT_READY</c></summary>
    public const string ControlDeviceNotReady = "PHONE_CONTROL_DEVICE_NOT_READY";

    /// <summary><c>PHONE_CONTROL_METRICS</c></summary>
    public const string ControlMetrics = "PHONE_CONTROL_METRICS";

    /// <summary><c>PHONE_CONTROL_NOT_READY</c></summary>
    public const string ControlNotReady = "PHONE_CONTROL_NOT_READY";

    /// <summary><c>PHONE_CONTROL_READY</c></summary>
    public const string ControlReady = "PHONE_CONTROL_READY";

    /// <summary><c>PHONE_CONTROL_SEND_FAILED</c></summary>
    public const string ControlSendFailed = "PHONE_CONTROL_SEND_FAILED";

    /// <summary><c>PHONE_CONTROL_START_FAILED</c></summary>
    public const string ControlStartFailed = "PHONE_CONTROL_START_FAILED";

    /// <summary><c>PHONE_CONTROL_STARTING</c></summary>
    public const string ControlStarting = "PHONE_CONTROL_STARTING";

    /// <summary><c>PHONE_CONTROL_STOPPED</c></summary>
    public const string ControlStopped = "PHONE_CONTROL_STOPPED";

    /// <summary><c>PHONE_CONTROL_STOPPING</c></summary>
    public const string ControlStopping = "PHONE_CONTROL_STOPPING";

    /// <summary><c>PHONE_CONTROL_VIDEO_SIZE_MISSING</c></summary>
    public const string ControlVideoSizeMissing = "PHONE_CONTROL_VIDEO_SIZE_MISSING";

    /// <summary><c>PHONE_MEDIA_PAUSING_FOR_SCREEN_RESTART</c></summary>
    public const string MediaPausingForScreenRestart = "PHONE_MEDIA_PAUSING_FOR_SCREEN_RESTART";

    /// <summary><c>PHONE_MEDIA_RECONNECT_FAILED</c></summary>
    public const string MediaReconnectFailed = "PHONE_MEDIA_RECONNECT_FAILED";

    /// <summary><c>PHONE_MEDIA_RECONNECT_UNEXPECTED</c></summary>
    public const string MediaReconnectUnexpected = "PHONE_MEDIA_RECONNECT_UNEXPECTED";

    /// <summary><c>PHONE_MEDIA_RECONNECTING</c></summary>
    public const string MediaReconnecting = "PHONE_MEDIA_RECONNECTING";

    /// <summary><c>PHONE_MEDIA_RESUMING_AFTER_SCREEN_RESTART</c></summary>
    public const string MediaResumingAfterScreenRestart = "PHONE_MEDIA_RESUMING_AFTER_SCREEN_RESTART";

    /// <summary><c>PHONE_MEDIA_RUNNING</c></summary>
    public const string MediaRunning = "PHONE_MEDIA_RUNNING";

    /// <summary><c>PHONE_MEDIA_SCREEN_RESTART_FAILED</c></summary>
    public const string MediaScreenRestartFailed = "PHONE_MEDIA_SCREEN_RESTART_FAILED";

    /// <summary><c>PHONE_MEDIA_SCREEN_RESTART_PAUSED</c></summary>
    public const string MediaScreenRestartPaused = "PHONE_MEDIA_SCREEN_RESTART_PAUSED";

    /// <summary><c>PHONE_MEDIA_START_FAILED</c></summary>
    public const string MediaStartFailed = "PHONE_MEDIA_START_FAILED";

    /// <summary><c>PHONE_MEDIA_STARTING</c></summary>
    public const string MediaStarting = "PHONE_MEDIA_STARTING";

    /// <summary><c>PHONE_MEDIA_STOPPED</c></summary>
    public const string MediaStopped = "PHONE_MEDIA_STOPPED";

    /// <summary><c>PHONE_MEDIA_STOPPING</c></summary>
    public const string MediaStopping = "PHONE_MEDIA_STOPPING";

    /// <summary><c>PHONE_MEDIA_WAITING_FOR_DEVICE</c></summary>
    public const string MediaWaitingForDevice = "PHONE_MEDIA_WAITING_FOR_DEVICE";

    /// <summary><c>VIDEO_DECODE_CODEC_UNSUPPORTED</c></summary>
    public const string VideoDecodeCodecUnsupported = "VIDEO_DECODE_CODEC_UNSUPPORTED";

    /// <summary><c>VIDEO_DECODE_FRAME_LIMIT</c></summary>
    public const string VideoDecodeFrameLimit = "VIDEO_DECODE_FRAME_LIMIT";

    /// <summary><c>VIDEO_DECODE_PROBE_TIMEOUT</c></summary>
    public const string VideoDecodeProbeTimeout = "VIDEO_DECODE_PROBE_TIMEOUT";

    /// <summary><c>VIDEO_DECODE_READY</c></summary>
    public const string VideoDecodeReady = "VIDEO_DECODE_READY";

    /// <summary><c>VIDEO_OVERLAY_ALREADY_STARTING</c></summary>
    public const string VideoOverlayAlreadyStarting = "VIDEO_OVERLAY_ALREADY_STARTING";

    /// <summary><c>VIDEO_OVERLAY_CODEC_UNSUPPORTED</c></summary>
    public const string VideoOverlayCodecUnsupported = "VIDEO_OVERLAY_CODEC_UNSUPPORTED";

    /// <summary><c>VIDEO_OVERLAY_RECONFIGURING</c></summary>
    public const string VideoOverlayReconfiguring = "VIDEO_OVERLAY_RECONFIGURING";

    /// <summary><c>VIDEO_OVERLAY_RUNNING</c></summary>
    public const string VideoOverlayRunning = "VIDEO_OVERLAY_RUNNING";

    /// <summary><c>VIDEO_OVERLAY_START_TIMEOUT</c></summary>
    public const string VideoOverlayStartTimeout = "VIDEO_OVERLAY_START_TIMEOUT";

    /// <summary><c>VIDEO_OVERLAY_STARTING</c></summary>
    public const string VideoOverlayStarting = "VIDEO_OVERLAY_STARTING";

    /// <summary><c>VIDEO_OVERLAY_STOPPED</c></summary>
    public const string VideoOverlayStopped = "VIDEO_OVERLAY_STOPPED";

    /// <summary><c>VIDEO_OVERLAY_STOPPING</c></summary>
    public const string VideoOverlayStopping = "VIDEO_OVERLAY_STOPPING";

    /// <summary><c>VIDEO_OVERLAY_UNEXPECTED_FAILURE</c></summary>
    public const string VideoOverlayUnexpectedFailure = "VIDEO_OVERLAY_UNEXPECTED_FAILURE";

    /// <summary><c>VIDEO_QUALITY_SAMPLE</c></summary>
    public const string VideoQualitySample = "VIDEO_QUALITY_SAMPLE";

    /// <summary><c>VIDEO_OVERLAY_NOT_STARTED</c> — 手机浮窗从未启动，与已关闭区分。</summary>
    public const string VideoOverlayNotStarted = "VIDEO_OVERLAY_NOT_STARTED";

    /// <summary><c>PHONE_CONTROL_NOT_STARTED</c> — 手机控制从未启动，与已停止区分。</summary>
    public const string ControlNotStarted = "PHONE_CONTROL_NOT_STARTED";
}
