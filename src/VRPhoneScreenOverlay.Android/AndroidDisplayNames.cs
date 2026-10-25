namespace VRPhoneScreenOverlay.Android;

/// <summary>
/// Display names and user-facing failure descriptions for Android connections.
/// </summary>
/// <remarks>
/// These live next to the enums rather than in the UI because the connection service puts the
/// same words into its status messages. Keeping two copies let the fallback wording drift apart:
/// one said "未知连接" while the other said "未知" for the same case.
/// </remarks>
public static class AndroidDisplayNames
{
    /// <summary>Human readable name for a transport.</summary>
    public static string Transport(AndroidTransport transport) => transport switch
    {
        AndroidTransport.Usb => "USB",
        AndroidTransport.Network => "无线",
        AndroidTransport.Emulator => "模拟器",
        _ => "未知连接",
    };

    /// <summary>Human readable name for a device status.</summary>
    public static string DeviceStatus(AndroidDeviceStatus status) => status switch
    {
        AndroidDeviceStatus.Ready => "已授权",
        AndroidDeviceStatus.Unauthorized => "等待手机授权",
        AndroidDeviceStatus.Offline => "离线",
        AndroidDeviceStatus.NoPermissions => "驱动无权限",
        _ => "未知状态",
    };

    /// <summary>Human readable name for a video codec.</summary>
    public static string Codec(AndroidVideoCodec codec) => codec switch
    {
        AndroidVideoCodec.H264 => "H.264",
        AndroidVideoCodec.H265 => "H.265",
        AndroidVideoCodec.Av1 => "AV1",
        AndroidVideoCodec.Vp8 => "VP8",
        AndroidVideoCodec.Vp9 => "VP9",
        _ => codec.ToString(),
    };

    /// <summary>Actionable display text for known connection failures, without changing diagnostic messages.</summary>
    public static string? FailureMessage(string reasonCode) => reasonCode switch
    {
        "ANDROID_RESOURCE_HASH_MISMATCH" or "ANDROID_RESOURCE_MISSING" or "ANDROID_SCRCPY_MANIFEST_INVALID" or
        "ANDROID_SCRCPY_MANIFEST_MISSING" or "ANDROID_SCRCPY_SERVER_HASH_MISMATCH" or "ANDROID_SCRCPY_SERVER_MISSING" or
        "ANDROID_RECOVERY_SOURCE_INVALID" or "ANDROID_RECOVERY_VERIFY_FAILED" or "ANDROID_RECOVERY_JOURNAL_INVALID" =>
            "手机连接所需文件缺失或损坏，请从官网下载完整安装包。",
        "ANDROID_SERVER_VERSION_CONFLICT" or "ANDROID_RECOVERY_COMPONENT_IN_USE" =>
            "手机连接被其他软件占用，请退出相关软件后重试。",
        "ANDROID_SERVER_ENDPOINT_INVALID" => "手机连接设置不可用，请重启软件后重试。",
        "ANDROID_RUNTIME_PREPARE_FAILED" or "ANDROID_TOOL_START_FAILED" or "ANDROID_RECOVERY_ACCESS_DENIED" or
        "ANDROID_RECOVERY_DISK_FULL" or "ANDROID_RECOVERY_EVIDENCE_FAILED" =>
            "无法准备手机连接，请检查磁盘空间和写入权限后重试。",
        "ANDROID_TOOL_IO_FAILED" or "ANDROID_TOOL_OUTPUT_LIMIT" or "ANDROID_TOOL_COMMAND_FAILED" or "ANDROID_RECOVERY_FAILED" =>
            "手机连接失败，请重新连接手机或重启软件后重试。",
        "ANDROID_TOOL_TIMEOUT" or "ANDROID_RECOVERY_TIMEOUT" => "手机连接超时，请检查手机授权后重试。",
        "ANDROID_RECOVERY_CANCELLED" => "手机连接操作已取消。",
        _ => null,
    };
}
