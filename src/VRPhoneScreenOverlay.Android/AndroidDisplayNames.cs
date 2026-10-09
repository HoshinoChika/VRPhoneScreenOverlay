namespace VRPhoneScreenOverlay.Android;

/// <summary>
/// Display names for the Android device enums.
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
}
