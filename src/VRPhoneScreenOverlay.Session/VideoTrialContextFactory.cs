using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.Media;
using VRPhoneScreenOverlay.Settings;

namespace VRPhoneScreenOverlay.Session;

public static class VideoTrialContextFactory
{
    public static VideoTrialContext Create(AndroidDeviceDetails? device, VideoDecoderPreference preference) =>
        new(device?.DeviceKey ?? "unknown", device?.NativeDisplayWidth ?? 0, device?.NativeDisplayHeight ?? 0,
            preference == VideoDecoderPreference.Software ? "software" : "automatic");
}
