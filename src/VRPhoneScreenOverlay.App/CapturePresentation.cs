namespace VRPhoneScreenOverlay.App;

internal static class CapturePresentation
{
    internal static bool IsActive(string status) => status is "starting" or "automatic" or "observing" or "heartbeat-failed" or "stop-pending";

    internal static string Message(string status) => status switch
    {
        "automatic" => "自动故障记录已启用；内存默认仅存本机，上传时单独确认。",
        "observing" => "已开启15分钟采集，请复现问题；转储仅保存在本机。",
        "starting" => "正在启动独立采集器…",
        "stopped" or "finished" => "采集已结束；可从“打开软件日志”的 captures 目录查看。",
        "capacity-limit" => "本机资料暂时无法清理，请先整理已有 captures 资料。",
        "startup-timeout" => "深入诊断启动超时，软件仍可使用；请稍后重试。",
        "start-failed" => "采集器未能启动，普通日志仍可使用。",
        "heartbeat-failed" => "心跳记录失败，请停止采集后重试。",
        "observer-failed" => "采集器异常结束；已完成的资料仍保留，普通日志可用。",
        "stop-pending" => "采集器正在退出；请稍后重试停止。",
        "startup-request-unavailable" => "下次启动采集请求不可用，普通日志仍可使用。",
        "startup-armed" => "已设为下次启动采集一次，最长15分钟；仅本机保存。",
        "cancelled" => "采集已取消。",
        _ => "含内存片段，仅本机保存；普通上传不包含转储。"
    };
}
