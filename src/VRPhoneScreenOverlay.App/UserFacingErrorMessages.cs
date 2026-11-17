using System.Text.Json;
using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.Diagnostics;

namespace VRPhoneScreenOverlay.App;

// Presentation only: technical messages and stable reason codes remain unchanged
// in services and diagnostic records. Unmapped, already actionable instructions
// are preserved, including addresses and ports needed for manual phone pairing.
internal static class UserFacingErrorMessages
{
    internal static string FromReason(string reasonCode, string fallbackMessage) => reasonCode switch
    {
        "BINDING_COMPONENT_RECOVERY_FAILED" or "BINDING_COMPONENT_RECOVERY_UNAVAILABLE" or
        "BINDING_COMPONENT_VERSION_MISMATCH" or "BINDING_COMPONENT_HOST_FAILED" =>
            "软件绑定辅助组件损坏或无法加载，请从官网下载完整安装包修复。",
        "UPDATE_SERVICE_NOT_CONFIGURED" or "UPDATE_SERVICE_CONFIG_INVALID" or
        "UPDATE_INSTALL_PATH_INVALID" or "UPDATE_MAINTENANCE_MISSING" =>
            "当前无法自动更新，请从官网下载完整安装包。",
        "UPDATE_MANIFEST_SIGNATURE_INVALID" => "更新信息校验失败，已停止更新。请稍后重试。",
        "UPDATE_MANIFEST_INVALID" or "UPDATE_MANIFEST_PAYLOAD_INVALID" or "UPDATE_VERSION_INVALID" =>
            "暂时无法获取有效的更新信息，请稍后重试。",
        "UPDATE_PACKAGE_HASH_MISMATCH" or "UPDATE_PACKAGE_SIZE_INVALID" or "UPDATE_PACKAGE_TOO_LARGE" or
        "UPDATE_PACKAGE_LAYOUT_INVALID" or "UPDATE_PACKAGE_PATH_INVALID" =>
            "更新包校验失败，已停止更新。请重新下载。",
        "UPDATE_PACKAGE_EXTRACTION_TIMEOUT" => "更新包解压超时，请稍后重试。",
        "UPDATE_PACKAGE_TRUNCATED" => "更新包下载不完整，请重新下载。",
        "UPDATE_CHECK_FAILED" => "检查更新失败，请稍后重试。",
        "UPDATE_MAINTENANCE_START_FAILED" or "UPDATE_INSTALL_FAILED" => "更新未完成，请重试或从官网下载完整安装包。",
        "UPDATE_SHARED_ADB_IN_USE" or "UPDATE_ADB_OWNER_UNAVAILABLE" =>
            "暂时无法更新，请退出其他正在连接手机的软件后重试。",
        "UPDATE_HISTORY_CAPACITY_EXCEEDED" => "更新未完成，原版本及历史已保留。翻译历史超出迁移预算，请先整理历史后重试。",
        "UPDATE_RECOVERY_UNAVAILABLE" => "更新未完成，原版本已保留。无法准备中断恢复，请稍后重试或上传诊断。",
        "UPDATE_NOT_PUBLISHED" => "暂时没有可用更新。",

        "NETWORK_HTTP_STATUS" or "NETWORK_RESPONSE_INVALID" or "NETWORK_RESPONSE_TOO_LARGE" =>
            "服务暂时无法使用，请稍后重试。",
        "NETWORK_TIMEOUT" => "连接超时，请检查网络后重试。",
        "NETWORK_UNAVAILABLE" => "无法连接服务，请检查网络后重试。",
        "OPENVR_NAMESPACE_UNAVAILABLE" => "SteamVR 通信入口不可用，已暂停连接尝试；请在方便时重启 SteamVR。",
        "NETWORK_INSECURE_SCHEME" => "无法安全连接服务，请稍后重试。",

        "DIAGNOSTICS_SERVICE_NOT_CONFIGURED" => "当前无法上传诊断，请打开软件日志，将诊断包提供给作者。",
        "DIAGNOSTICS_INIT_INVALID" or "DIAGNOSTICS_UPLOAD_POLICY_INVALID" or "DIAGNOSTICS_COMPLETE_INVALID" or
        "DIAGNOSTICS_NOT_ACCEPTED" => "诊断上传未完成，请稍后重试。",
        "DIAGNOSTICS_BUNDLE_SIZE_INVALID" => "诊断包过大，未上传。已保存在本机，请点击“打开软件日志”查看。",
        "DIAGNOSTICS_FOLDER_OPEN_BUSY" => "正在打开日志文件夹，请稍候。",
        "DIAGNOSTICS_FOLDER_OPEN_TIMEOUT" => "打开日志文件夹超时，请重试。",
        "DIAGNOSTICS_FOLDER_OPEN_FAILED" => "无法打开日志文件夹，请检查访问权限后重试。",

        "OPENVR_STARTUP_REGISTRATION_FAILED" => "SteamVR 启动登记校验未通过，自动启动设置尚未生效；请重试或上传诊断。",
        "OPENVR_STARTUP_REGISTRATION_UNCONFIRMED" => "尚未确认 SteamVR 启动登记；请重试或上传诊断。",
        "OPENVR_STARTUP_REGISTRATION_TIMEOUT" => "连接 SteamVR 超时，请确认已启动后重试。",
        "OPENVR_INITIALIZATION_FAILED" or "OPENVR_INITIALIZATION_UNEXPECTED" or "OPENVR_OVERLAY_INTERFACE_MISSING" =>
            "无法连接 SteamVR，请确认已启动后重试。",
        "OPENVR_NATIVE_LIBRARY_FAILED" => "无法打开浮窗，请从官网下载完整安装包后重试。",
        "OPENVR_OVERLAY_CREATE_FAILED" or "OPENVR_OVERLAY_SHOW_FAILED" or "OPENVR_OVERLAY_WIDTH_FAILED" or
        "OPENVR_OVERLAY_ALPHA_FAILED" or "OPENVR_OVERLAY_INPUT_FAILED" =>
            "浮窗打开失败，请重启 SteamVR 后重试。",
        "OPENVR_OVERLAY_HIDE_FAILED" => "浮窗隐藏失败，请重试。",
        "OPENVR_OVERLAY_TEXTURE_FAILED" or "D3D11_VIDEO_PRESENT_FAILED" => "手机画面显示失败，请重新打开浮窗。",
        "D3D11_STEAMVR_ADAPTER_MISSING" or "D3D11_VIDEO_INITIALIZATION_FAILED" =>
            "无法显示手机画面，请检查显卡驱动并重启 SteamVR。",
        "D3D11_VIDEO_FRAME_TRUNCATED" or "D3D11_VIDEO_NV12_ODD_DIMENSIONS" =>
            "当前画面尺寸暂不兼容，请降低分辨率后重试。",
        "D3D11_VIDEO_PIXEL_FORMAT_UNSUPPORTED" => "当前手机画面暂不兼容，请上传诊断包联系作者。",
        "OPENVR_BINDING_DEFAULT_RESTORE_FAILED" => "恢复手柄绑定失败，请确认 SteamVR 已启动后重试。",
        "OPENVR_BINDING_LOAD_FAILED" or "OPENVR_LOCAL_BINDING_ACTIVATION_FAILED" =>
            "手柄绑定未生效，请重新打开浮窗或在 SteamVR 中检查绑定。",
        "OPENVR_LOCAL_BINDING_PREPARE_FAILED" => "手柄设置未准备完成，请确认 SteamVR 已启动后重试。",
        "OPENVR_BINDING_UI_FAILED" => "无法打开手柄绑定页面，请在 SteamVR 中手动打开。",
        "OPENVR_BINDING_UI_OPENED" => "已请求打开 SteamVR 手柄绑定页面。",

        "VIDEO_TRIAL_CHANGE_BUSY" => "画面设置正在更改，请稍后重试。",
        "VIDEO_TRIAL_ALREADY_ACTIVE" or "VIDEO_TRIAL_CHANGE_MISMATCH" => "请先关闭浮窗，再重新更改画面设置。",
        "VIDEO_TRIAL_NO_FIRST_FRAME" => "尚未显示手机画面，请稍后重试。",
        "VIDEO_TRIAL_NOT_STABLE_YET" => "画面尚未稳定，请稍后重试。",
        "VIDEO_TRIAL_OWNER_UNAVAILABLE" => "无法保存画面设置，请关闭重复打开的软件并检查写入权限。",
        "VIDEO_TRIAL_STORAGE_UNAVAILABLE" or "VIDEO_TRIAL_RECOVERY_REQUIRED" =>
            "画面暂时无法打开，请关闭浮窗后重新开启。",
        "VIDEO_TRIAL_STALE_ATTEMPT" => "画面状态已变化，请重新操作。",
        "VIDEO_TRIAL_WRITE_FAILED" => "无法保存画面设置，本次操作已停止。请检查磁盘空间和写入权限。",
        "VIDEO_RECOVERY_PROFILE_UNAVAILABLE" => "没有适用于当前手机的可恢复画面设置。",
        "VIDEO_DECODE_FRAME_LIMIT" or "VIDEO_DECODE_PROBE_TIMEOUT" or "VIDEO_OVERLAY_START_TIMEOUT" =>
            "未能及时显示手机画面，请重新连接手机后重试。",
        "VIDEO_DECODE_CODEC_UNSUPPORTED" or "VIDEO_OVERLAY_CODEC_UNSUPPORTED" or "MEDIA_H264_NV12_NOT_SUPPORTED" =>
            "当前手机画面暂不兼容，请上传诊断包联系作者。",
        "MEDIA_FOUNDATION_UNAVAILABLE" =>
            "当前系统缺少或无法加载 Windows 媒体组件，无法显示手机画面。Windows N 版请安装微软 Media Feature Pack（媒体功能包）后重试；其他版本请恢复系统媒体组件。",
        "MEDIA_H264_DECODER_NOT_FOUND" or "MEDIA_H264_INITIALIZATION_FAILED" =>
            "无法打开手机画面，请重启软件后重试；仍失败请上传诊断包。",
        "MEDIA_H264_DECODE_FAILED" or "MEDIA_H264_DRAIN_FAILED" or "MEDIA_H264_OUTPUT_EMPTY" or "MEDIA_H264_OUTPUT_MISSING" =>
            "手机画面处理失败，请重新打开浮窗。",
        "MEDIA_H264_CLEANUP_FAILED" => "浮窗未能完全关闭，请重启软件后重试。",
        "PHONE_CONTROL_START_FAILED" => "暂时无法操作手机，请重新连接手机后重试。",
        "PHONE_CONTROL_SEND_FAILED" => "手机操作失败，请唤醒手机或重新连接后重试。",
        "PHONE_MEDIA_PAUSING_FOR_SCREEN_RESTART" or "PHONE_MEDIA_RESUMING_AFTER_SCREEN_RESTART" => "正在调整手机画面…",

        _ => AndroidDisplayNames.FailureMessage(reasonCode) ??
            (string.IsNullOrWhiteSpace(fallbackMessage) ? "操作未完成，请重试。" : fallbackMessage),
    };

    internal static string FromException(Exception exception) => exception switch
    {
        DiagnosticsException diagnostics => FromReason(diagnostics.ReasonCode, diagnostics.Message),
        IReasonCoded coded => FromReason(coded.ReasonCode, exception.Message),
        JsonException => "暂时无法获取服务信息，请稍后重试。",
        OperationCanceledException => "操作已取消。",
        HttpRequestException => "无法连接服务，请检查网络后重试。",
        UnauthorizedAccessException => "无法访问所需文件，请检查访问权限后重试。",
        IOException => "文件操作失败，请检查磁盘空间和访问权限后重试。",
        _ => "操作未完成，请稍后重试。",
    };
}
