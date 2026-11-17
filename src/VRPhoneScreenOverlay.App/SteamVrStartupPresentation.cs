using VRPhoneScreenOverlay.SteamVR;
using VRPhoneScreenOverlay.Session;

namespace VRPhoneScreenOverlay.App;

internal static class SteamVrStartupPresentation
{
    internal static OpenVrBindingResult? ResolveWarning(OpenVrBindingResult? previous, OpenVrBindingResult? previousRegistration, SteamVrStartupStatus current)
    {
        if (previous is not null && previous != previousRegistration) { return previous; }
        if (!current.Registration.Succeeded) { return current.Registration; }
        return current.Pending ? previous : null;
    }
    internal const string Registering = "正在登记 SteamVR 应用…";
    internal const string Unknown = "无法读取自动启动设置，请重新设置此开关";
    internal const string Pending = "自动启动设置尚未生效，请重新打开 SteamVR";
    internal static (string Text, bool Warning) Read(SteamVrStartupStatus status) =>
        status.Attempts == 0 && status.Pending ? (Registering, false) :
        status.Registration.ReasonCode is OpenVrReasonCodes.StartupRegistrationFailed or OpenVrReasonCodes.StartupRegistrationUnconfirmed
        ? (UserFacingErrorMessages.FromReason(status.Registration.ReasonCode, status.Registration.Message), true) :
        status.Registration.ReasonCode.StartsWith("BINDING_COMPONENT_", StringComparison.Ordinal)
        ? (UserFacingErrorMessages.FromReason(status.Registration.ReasonCode, status.Registration.Message), true) : !status.PreferenceKnown
        ? (Unknown, true) : status.Pending ? (Pending, true) : (string.Empty, false);
}
