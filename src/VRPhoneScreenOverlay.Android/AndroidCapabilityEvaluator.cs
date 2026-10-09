namespace VRPhoneScreenOverlay.Android;

public static class AndroidCapabilityEvaluator
{
    private const int _minimumScrcpySdk = 21;
    private const int _minimumAudioSdk = 30;

    public static AndroidDeviceCapabilities Evaluate(int? androidSdk)
    {
        if (androidSdk is null)
        {
            return AndroidDeviceCapabilities.Unknown;
        }

        AndroidCapability video = androidSdk >= _minimumScrcpySdk
            ? Available(AndroidReasonCodes.VideoCapabilityAvailable, "支持 scrcpy 视频协议")
            : Unavailable(AndroidReasonCodes.VideoRequiresApi21, "视频要求 Android 5.0 / API 21 或更高版本");
        AndroidCapability control = androidSdk >= _minimumScrcpySdk
            ? Available(AndroidReasonCodes.ControlCapabilityAvailable, "支持 Android 输入注入协议，实际权限由系统决定")
            : Unavailable(AndroidReasonCodes.ControlRequiresApi21, "控制要求 Android 5.0 / API 21 或更高版本");
        AndroidCapability audio = androidSdk >= _minimumAudioSdk
            ? Available(AndroidReasonCodes.AudioCapabilityAvailable, "支持 Android 内部音频捕获，应用仍可拒绝录音")
            : Unavailable(AndroidReasonCodes.AudioRequiresApi30, "内部音频要求 Android 11 / API 30 或更高版本");
        return new AndroidDeviceCapabilities(video, control, audio);
    }

    private static AndroidCapability Available(string code, string message) =>
        new(AndroidCapabilityState.Available, code, message);

    private static AndroidCapability Unavailable(string code, string message) =>
        new(AndroidCapabilityState.Unavailable, code, message);
}
