namespace VRPhoneScreenOverlay.Session;

public static class TranslationFailureMessages
{
    public static string Describe(Exception exception) => exception switch
    {
        OperationCanceledException or TimeoutException => "截图或识别超时，请检查网络和 SteamVR 后重试。",
        HttpRequestException => "无法连接 DeepSeek，请检查网络后重试。",
        _ => exception.Message switch
        {
            "TRANSLATION_BUILTIN_UNAVAILABLE" => "软件内置服务暂不可用，请稍后重试。",
            "TRANSLATION_VR_UNAVAILABLE" => "SteamVR 尚未就绪，请开启头显并等待画面显示后重试。",
            "TRANSLATION_CAPTURE_FAILED" => "未能获取 SteamVR 画面，请确认头显正在显示目标场景。",
            "TRANSLATION_CAPTURE_FORMAT_UNSUPPORTED" => "当前 SteamVR 画面格式暂不支持截图。" +
                (exception.Data["CaptureFormat"] is string format ? "\n格式信息：" + format : ""),
            "TRANSLATION_KEY_INVALID" or "TRANSLATION_KEY_REJECTED" => "API Key 无效，请在 PC 设置中的 DeepSeek API 页面重新验证。",
            "TRANSLATION_BALANCE_REQUIRED" => "DeepSeek 账户余额不足，请检查账户。",
            "TRANSLATION_RATE_LIMITED" => "DeepSeek 请求过于频繁，请稍后重试。",
            "TRANSLATION_RESPONSE_INCOMPLETE" => "回答超出长度限制，未收到完整结果。请缩小目标范围后重试。",
            "TRANSLATION_RESPONSE_FILTERED" => "服务未返回此图片的识别结果，请更换目标后重试。",
            "TRANSLATION_RESPONSE_INTERRUPTED" => "回答未正常完成，请稍后重试。",
            "TRANSLATION_RESPONSE_INVALID" => "未收到可用的识别结果，请重新尝试。",
            "TRANSLATION_IMAGE_INVALID" => "截图过大或格式不支持，无法发送。",
            _ => "识别未完成，请检查 SteamVR 和 API 设置后重试。",
        },
    };
}
