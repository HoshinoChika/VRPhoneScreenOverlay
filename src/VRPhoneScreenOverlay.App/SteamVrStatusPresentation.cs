using VRPhoneScreenOverlay.Session;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.App;

internal static class SteamVrStatusPresentation
{
    internal static string Text(PhoneOverlaySnapshot overlay, OpenVrPlayspaceDragState runtime, OpenVrBindingResult? startupWarning = null, bool registering = false) => runtime switch
    {
        _ when startupWarning is { Succeeded: false } && startupWarning.ReasonCode.StartsWith("BINDING_COMPONENT_", StringComparison.Ordinal) => "组件异常",
        _ when registering && startupWarning is null => "正在登记",
        OpenVrPlayspaceDragState.Ready => overlay.State == PhoneOverlayState.Running
            ? overlay.WorldAnchored ? "已就绪" : "等待定位" : "已连接",
        _ when startupWarning is { Succeeded: false, ReasonCode: OpenVrReasonCodes.StartupRegistrationTimeout } => "连接超时",
        _ when startupWarning is { Succeeded: false, ReasonCode: OpenVrReasonCodes.StartupRegistrationFailed or OpenVrReasonCodes.StartupRegistrationUnconfirmed } => "登记异常",
        _ when startupWarning is { Succeeded: false } => "连接异常",
        OpenVrPlayspaceDragState.Starting => "连接中",
        OpenVrPlayspaceDragState.WaitingForSteamVr => "等待启动",
        OpenVrPlayspaceDragState.Faulted => "连接异常",
        _ => "等待连接",
    };
}
