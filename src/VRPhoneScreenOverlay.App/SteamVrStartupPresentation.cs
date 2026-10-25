using VRPhoneScreenOverlay.Session;

namespace VRPhoneScreenOverlay.App;

internal static class SteamVrStartupPresentation
{
    internal const string Unknown = "无法读取自动启动设置，请重新设置此开关";
    internal const string Pending = "自动启动设置尚未生效，请重新打开 SteamVR";
    internal static (string Text, bool Warning) Read(SteamVrStartupStatus status) => !status.PreferenceKnown
        ? (Unknown, true) : status.Pending ? (Pending, true) : (string.Empty, false);
}
