using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.Settings;

namespace VRPhoneScreenOverlay.Session;

public static class PhoneVideoOptionsFactory
{
    public static AndroidVideoOptions Create(AppSettings settings, AndroidDeviceDetails? device, PhoneOverlaySnapshot overlay)
    {
        int width = device?.NativeDisplayWidth ?? 0;
        int height = device?.NativeDisplayHeight ?? 0;
        if (width > 0 && height > 0 && overlay.Width > 0 && overlay.Height > 0 &&
            (width > height) != (overlay.Width > overlay.Height))
        { (width, height) = (height, width); }
        VideoResolutionProfile resolution = width > 0 && height > 0
            ? VideoResolutionProfiles.Resolve(width, height, settings.VideoResolutionPercent)
            : new VideoResolutionProfile(100, 1, 1, 0, false);
        return new AndroidVideoOptions
        {
            ResolutionPercent = resolution.Percent,
            MaximumSize = resolution.MaximumSize,
            VideoBitrateBitsPerSecond = settings.VideoBitrateMbps * 1_000_000,
            MaximumFramesPerSecond = settings.VideoMaximumFramesPerSecond,
        };
    }
}
