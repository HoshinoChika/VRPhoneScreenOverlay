using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Session;

public static class PhonePlaceholderPresentation
{
    public static OpenVrPhonePlaceholderState Select(bool phoneReady, PhoneMediaSessionState media) =>
        !phoneReady ? OpenVrPhonePlaceholderState.Disconnected :
        media == PhoneMediaSessionState.Faulted ? OpenVrPhonePlaceholderState.Failed : OpenVrPhonePlaceholderState.Connecting;
}
