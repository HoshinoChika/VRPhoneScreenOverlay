using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

internal sealed class OpenVrApplicationRegistrationException(string operation, EVRApplicationError error)
    : InvalidOperationException("SteamVR application registration failed.")
{
    // Only controlled operation names and native enum values may reach the UI.
    public string UserMessage => error == EVRApplicationError.None
        ? $"SteamVR {operation}失败，请重试"
        : $"SteamVR {operation}失败（{error}），请重试";
}
