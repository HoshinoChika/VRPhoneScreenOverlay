namespace VRPhoneScreenOverlay.Android;

/// <summary>
/// Stable reason codes raised by this module. Values are protocol: they are only ever
/// added, never re-spelled, and never localised.
/// </summary>
public static class AndroidReasonCodes
{
    /// <summary><c>ANDROID_ADB_SERVER_STOP_FAILED</c></summary>
    public const string AdbServerStopFailed = "ANDROID_ADB_SERVER_STOP_FAILED";

    /// <summary><c>ANDROID_ADB_SERVER_STOPPED</c></summary>
    public const string AdbServerStopped = "ANDROID_ADB_SERVER_STOPPED";

    /// <summary><c>ANDROID_AUDIO</c></summary>
    public const string Audio = "ANDROID_AUDIO";

    /// <summary><c>ANDROID_AUDIO_CAPABILITY_AVAILABLE</c></summary>
    public const string AudioCapabilityAvailable = "ANDROID_AUDIO_CAPABILITY_AVAILABLE";

    /// <summary><c>ANDROID_AUDIO_CAPABILITY_UNKNOWN</c></summary>
    public const string AudioCapabilityUnknown = "ANDROID_AUDIO_CAPABILITY_UNKNOWN";

    /// <summary><c>ANDROID_AUDIO_CODEC_UNKNOWN</c></summary>
    public const string AudioCodecUnknown = "ANDROID_AUDIO_CODEC_UNKNOWN";

    /// <summary><c>ANDROID_AUDIO_CODEC_UNSUPPORTED</c></summary>
    public const string AudioCodecUnsupported = "ANDROID_AUDIO_CODEC_UNSUPPORTED";

    /// <summary><c>ANDROID_AUDIO_CONFIGURATION_FAILED</c></summary>
    public const string AudioConfigurationFailed = "ANDROID_AUDIO_CONFIGURATION_FAILED";

    /// <summary><c>ANDROID_AUDIO_CONNECTED</c></summary>
    public const string AudioConnected = "ANDROID_AUDIO_CONNECTED";

    /// <summary><c>ANDROID_AUDIO_DEVICE_NOT_READY</c></summary>
    public const string AudioDeviceNotReady = "ANDROID_AUDIO_DEVICE_NOT_READY";

    /// <summary><c>ANDROID_AUDIO_DISABLED</c></summary>
    public const string AudioDisabled = "ANDROID_AUDIO_DISABLED";

    /// <summary><c>ANDROID_AUDIO_HANDSHAKE_TIMEOUT</c></summary>
    public const string AudioHandshakeTimeout = "ANDROID_AUDIO_HANDSHAKE_TIMEOUT";

    /// <summary><c>ANDROID_AUDIO_PACKET_SIZE_INVALID</c></summary>
    public const string AudioPacketSizeInvalid = "ANDROID_AUDIO_PACKET_SIZE_INVALID";

    /// <summary><c>ANDROID_AUDIO_REQUIRES_API_30</c></summary>
    public const string AudioRequiresApi30 = "ANDROID_AUDIO_REQUIRES_API_30";

    /// <summary><c>ANDROID_AUDIO_STOPPED</c></summary>
    public const string AudioStopped = "ANDROID_AUDIO_STOPPED";

    /// <summary><c>ANDROID_AUTHORIZATION_REQUIRED</c></summary>
    public const string AuthorizationRequired = "ANDROID_AUTHORIZATION_REQUIRED";

    /// <summary><c>ANDROID_CONTROL</c></summary>
    public const string Control = "ANDROID_CONTROL";

    /// <summary><c>ANDROID_CONTROL_CAPABILITY_AVAILABLE</c></summary>
    public const string ControlCapabilityAvailable = "ANDROID_CONTROL_CAPABILITY_AVAILABLE";

    /// <summary><c>ANDROID_CONTROL_CAPABILITY_UNKNOWN</c></summary>
    public const string ControlCapabilityUnknown = "ANDROID_CONTROL_CAPABILITY_UNKNOWN";

    /// <summary><c>ANDROID_CONTROL_COMMAND_SENT</c></summary>
    public const string ControlCommandSent = "ANDROID_CONTROL_COMMAND_SENT";

    /// <summary><c>ANDROID_CONTROL_CONNECTED</c></summary>
    public const string ControlConnected = "ANDROID_CONTROL_CONNECTED";

    /// <summary><c>ANDROID_CONTROL_DEVICE_NOT_READY</c></summary>
    public const string ControlDeviceNotReady = "ANDROID_CONTROL_DEVICE_NOT_READY";

    /// <summary><c>ANDROID_CONTROL_DISPLAY_SIZE_FAILED</c></summary>
    public const string ControlDisplaySizeFailed = "ANDROID_CONTROL_DISPLAY_SIZE_FAILED";

    /// <summary><c>ANDROID_CONTROL_DISPLAY_SIZE_READY</c></summary>
    public const string ControlDisplaySizeReady = "ANDROID_CONTROL_DISPLAY_SIZE_READY";

    /// <summary><c>ANDROID_CONTROL_DISPLAY_SIZE_TIMEOUT</c></summary>
    public const string ControlDisplaySizeTimeout = "ANDROID_CONTROL_DISPLAY_SIZE_TIMEOUT";

    /// <summary><c>ANDROID_CONTROL_REQUIRES_API_21</c></summary>
    public const string ControlRequiresApi21 = "ANDROID_CONTROL_REQUIRES_API_21";

    /// <summary><c>ANDROID_CONTROL_SERVER_EXITED</c></summary>
    public const string ControlServerExited = "ANDROID_CONTROL_SERVER_EXITED";

    /// <summary><c>ANDROID_CONTROL_STOPPED</c></summary>
    public const string ControlStopped = "ANDROID_CONTROL_STOPPED";

    /// <summary><c>ANDROID_CONTROL_WRITE_FAILED</c></summary>
    public const string ControlWriteFailed = "ANDROID_CONTROL_WRITE_FAILED";

    /// <summary><c>ANDROID_CREATED</c></summary>
    public const string Created = "ANDROID_CREATED";

    /// <summary><c>ANDROID_DEVICE_OFFLINE</c></summary>
    public const string DeviceOffline = "ANDROID_DEVICE_OFFLINE";

    /// <summary><c>ANDROID_DISCOVERING</c></summary>
    public const string Discovering = "ANDROID_DISCOVERING";

    /// <summary><c>ANDROID_DRIVER_PERMISSION_REQUIRED</c></summary>
    public const string DriverPermissionRequired = "ANDROID_DRIVER_PERMISSION_REQUIRED";

    /// <summary><c>ANDROID_KEYGUARD_DEVICE_NOT_READY</c></summary>
    public const string KeyguardDeviceNotReady = "ANDROID_KEYGUARD_DEVICE_NOT_READY";

    /// <summary><c>ANDROID_KEYGUARD_LOCKED</c></summary>
    public const string KeyguardLocked = "ANDROID_KEYGUARD_LOCKED";

    /// <summary><c>ANDROID_KEYGUARD_MONITOR_FAILED</c></summary>
    public const string KeyguardMonitorFailed = "ANDROID_KEYGUARD_MONITOR_FAILED";

    /// <summary><c>ANDROID_KEYGUARD_PROBE_FAILED</c></summary>
    public const string KeyguardProbeFailed = "ANDROID_KEYGUARD_PROBE_FAILED";

    /// <summary><c>ANDROID_KEYGUARD_PROBE_TIMEOUT</c></summary>
    public const string KeyguardProbeTimeout = "ANDROID_KEYGUARD_PROBE_TIMEOUT";

    /// <summary><c>ANDROID_KEYGUARD_UNKNOWN</c></summary>
    public const string KeyguardUnknown = "ANDROID_KEYGUARD_UNKNOWN";

    /// <summary><c>ANDROID_KEYGUARD_UNLOCKED</c></summary>
    public const string KeyguardUnlocked = "ANDROID_KEYGUARD_UNLOCKED";

    /// <summary><c>ANDROID_MEDIA_ALREADY_PAUSED</c></summary>
    public const string MediaAlreadyPaused = "ANDROID_MEDIA_ALREADY_PAUSED";

    /// <summary><c>ANDROID_MEDIA_DEVICE_NOT_READY</c></summary>
    public const string MediaDeviceNotReady = "ANDROID_MEDIA_DEVICE_NOT_READY";

    /// <summary><c>ANDROID_MEDIA_PAUSE_FAILED</c></summary>
    public const string MediaPauseFailed = "ANDROID_MEDIA_PAUSE_FAILED";

    /// <summary><c>ANDROID_MEDIA_PAUSE_TIMEOUT</c></summary>
    public const string MediaPauseTimeout = "ANDROID_MEDIA_PAUSE_TIMEOUT";

    /// <summary><c>ANDROID_MEDIA_PAUSED_FOR_VR_INTERRUPTION</c></summary>
    public const string MediaPausedForVrInterruption = "ANDROID_MEDIA_PAUSED_FOR_VR_INTERRUPTION";

    /// <summary><c>ANDROID_MEDIA_RESUME_DEVICE_NOT_READY</c></summary>
    public const string MediaResumeDeviceNotReady = "ANDROID_MEDIA_RESUME_DEVICE_NOT_READY";

    /// <summary><c>ANDROID_MEDIA_RESUME_FAILED</c></summary>
    public const string MediaResumeFailed = "ANDROID_MEDIA_RESUME_FAILED";

    /// <summary><c>ANDROID_MEDIA_RESUME_TIMEOUT</c></summary>
    public const string MediaResumeTimeout = "ANDROID_MEDIA_RESUME_TIMEOUT";

    /// <summary><c>ANDROID_MEDIA_RESUMED_AFTER_VR_READY</c></summary>
    public const string MediaResumedAfterVrReady = "ANDROID_MEDIA_RESUMED_AFTER_VR_READY";

    /// <summary><c>ANDROID_MEDIA_STATE_UNAVAILABLE</c></summary>
    public const string MediaStateUnavailable = "ANDROID_MEDIA_STATE_UNAVAILABLE";

    /// <summary><c>ANDROID_PROBE_FAILED</c></summary>
    public const string ProbeFailed = "ANDROID_PROBE_FAILED";

    /// <summary><c>ANDROID_PROBE_READY</c></summary>
    public const string ProbeReady = "ANDROID_PROBE_READY";

    /// <summary><c>ANDROID_PROBE_TIMEOUT</c></summary>
    public const string ProbeTimeout = "ANDROID_PROBE_TIMEOUT";

    /// <summary><c>ANDROID_PROBING</c></summary>
    public const string Probing = "ANDROID_PROBING";

    /// <summary><c>ANDROID_READY</c></summary>
    public const string Ready = "ANDROID_READY";

    /// <summary><c>ANDROID_READY_MULTIPLE</c></summary>
    public const string ReadyMultiple = "ANDROID_READY_MULTIPLE";

    /// <summary><c>ANDROID_REFRESHING</c></summary>
    public const string Refreshing = "ANDROID_REFRESHING";

    /// <summary><c>ANDROID_RESOURCE_HASH_MISMATCH</c></summary>
    public const string ResourceHashMismatch = "ANDROID_RESOURCE_HASH_MISMATCH";

    /// <summary><c>ANDROID_RESOURCE_MISSING</c></summary>
    public const string ResourceMissing = "ANDROID_RESOURCE_MISSING";

    /// <summary><c>ANDROID_RESOURCES_READY</c></summary>
    public const string ResourcesReady = "ANDROID_RESOURCES_READY";

    /// <summary><c>ANDROID_SCAN</c></summary>
    public const string Scan = "ANDROID_SCAN";

    /// <summary><c>ANDROID_SCAN_OK</c></summary>
    public const string ScanOk = "ANDROID_SCAN_OK";

    /// <summary><c>ANDROID_SCAN_UNEXPECTED</c></summary>
    public const string ScanUnexpected = "ANDROID_SCAN_UNEXPECTED";

    /// <summary><c>ANDROID_SCRCPY_MANIFEST_INVALID</c></summary>
    public const string ScrcpyManifestInvalid = "ANDROID_SCRCPY_MANIFEST_INVALID";

    /// <summary><c>ANDROID_SCRCPY_MANIFEST_MISSING</c></summary>
    public const string ScrcpyManifestMissing = "ANDROID_SCRCPY_MANIFEST_MISSING";

    /// <summary><c>ANDROID_SCRCPY_SERVER_HASH_MISMATCH</c></summary>
    public const string ScrcpyServerHashMismatch = "ANDROID_SCRCPY_SERVER_HASH_MISMATCH";

    /// <summary><c>ANDROID_SCRCPY_SERVER_MISSING</c></summary>
    public const string ScrcpyServerMissing = "ANDROID_SCRCPY_SERVER_MISSING";

    /// <summary><c>ANDROID_SCRCPY_SERVER_READY</c></summary>
    public const string ScrcpyServerReady = "ANDROID_SCRCPY_SERVER_READY";

    /// <summary><c>ANDROID_SERVER_START_FAILED</c></summary>
    public const string ServerStartFailed = "ANDROID_SERVER_START_FAILED";

    /// <summary><c>ANDROID_STOPPED</c></summary>
    public const string Stopped = "ANDROID_STOPPED";

    /// <summary><c>ANDROID_STOPPING</c></summary>
    public const string Stopping = "ANDROID_STOPPING";

    /// <summary><c>ANDROID_TOOL_START_FAILED</c></summary>
    public const string ToolStartFailed = "ANDROID_TOOL_START_FAILED";

    /// <summary><c>ANDROID_TOOL_VERSION</c></summary>
    public const string ToolVersion = "ANDROID_TOOL_VERSION";

    /// <summary><c>ANDROID_VIDEO</c></summary>
    public const string Video = "ANDROID_VIDEO";

    /// <summary><c>ANDROID_VIDEO_CAPABILITY_AVAILABLE</c></summary>
    public const string VideoCapabilityAvailable = "ANDROID_VIDEO_CAPABILITY_AVAILABLE";

    /// <summary><c>ANDROID_VIDEO_CAPABILITY_UNKNOWN</c></summary>
    public const string VideoCapabilityUnknown = "ANDROID_VIDEO_CAPABILITY_UNKNOWN";

    /// <summary><c>ANDROID_VIDEO_CLOCK_SYNC_TIMEOUT</c></summary>
    public const string VideoClockSyncTimeout = "ANDROID_VIDEO_CLOCK_SYNC_TIMEOUT";

    /// <summary><c>ANDROID_VIDEO_CLOCK_SYNCHRONIZED</c></summary>
    public const string VideoClockSynchronized = "ANDROID_VIDEO_CLOCK_SYNCHRONIZED";

    /// <summary><c>ANDROID_VIDEO_CLOCK_UNAVAILABLE</c></summary>
    public const string VideoClockUnavailable = "ANDROID_VIDEO_CLOCK_UNAVAILABLE";

    /// <summary><c>ANDROID_VIDEO_CODEC_UNKNOWN</c></summary>
    public const string VideoCodecUnknown = "ANDROID_VIDEO_CODEC_UNKNOWN";

    /// <summary><c>ANDROID_VIDEO_CONFIGURATION_FAILED</c></summary>
    public const string VideoConfigurationFailed = "ANDROID_VIDEO_CONFIGURATION_FAILED";

    /// <summary><c>ANDROID_VIDEO_CONNECTED</c></summary>
    public const string VideoConnected = "ANDROID_VIDEO_CONNECTED";

    /// <summary><c>ANDROID_VIDEO_DEVICE_NOT_READY</c></summary>
    public const string VideoDeviceNotReady = "ANDROID_VIDEO_DEVICE_NOT_READY";

    /// <summary><c>ANDROID_VIDEO_DISABLED</c></summary>
    public const string VideoDisabled = "ANDROID_VIDEO_DISABLED";

    /// <summary><c>ANDROID_VIDEO_HANDSHAKE_TIMEOUT</c></summary>
    public const string VideoHandshakeTimeout = "ANDROID_VIDEO_HANDSHAKE_TIMEOUT";

    /// <summary><c>ANDROID_VIDEO_PACKET_SIZE_INVALID</c></summary>
    public const string VideoPacketSizeInvalid = "ANDROID_VIDEO_PACKET_SIZE_INVALID";

    /// <summary><c>ANDROID_VIDEO_PROBE_TIMEOUT</c></summary>
    public const string VideoProbeTimeout = "ANDROID_VIDEO_PROBE_TIMEOUT";

    /// <summary><c>ANDROID_VIDEO_REQUIRES_API_21</c></summary>
    public const string VideoRequiresApi21 = "ANDROID_VIDEO_REQUIRES_API_21";

    /// <summary><c>ANDROID_VIDEO_SESSION</c></summary>
    public const string VideoSession = "ANDROID_VIDEO_SESSION";

    /// <summary><c>ANDROID_VIDEO_SIZE_INVALID</c></summary>
    public const string VideoSizeInvalid = "ANDROID_VIDEO_SIZE_INVALID";

    /// <summary><c>ANDROID_VIDEO_STOPPED</c></summary>
    public const string VideoStopped = "ANDROID_VIDEO_STOPPED";

    /// <summary><c>ANDROID_VIDEO_WAKE_FAILED</c></summary>
    public const string VideoWakeFailed = "ANDROID_VIDEO_WAKE_FAILED";

    /// <summary><c>ANDROID_VIDEO_WAKE_SENT</c></summary>
    public const string VideoWakeSent = "ANDROID_VIDEO_WAKE_SENT";

    /// <summary><c>ANDROID_VIDEO_WAKE_TIMEOUT</c></summary>
    public const string VideoWakeTimeout = "ANDROID_VIDEO_WAKE_TIMEOUT";

    /// <summary><c>ANDROID_WAITING_FOR_DEVICE</c></summary>
    public const string WaitingForDevice = "ANDROID_WAITING_FOR_DEVICE";

    /// <summary><c>ANDROID_WAKE_KEY_FAILED</c></summary>
    public const string WakeKeyFailed = "ANDROID_WAKE_KEY_FAILED";

    /// <summary><c>ANDROID_WAKE_KEY_TIMEOUT</c></summary>
    public const string WakeKeyTimeout = "ANDROID_WAKE_KEY_TIMEOUT";
}
