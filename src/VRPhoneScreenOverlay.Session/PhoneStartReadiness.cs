using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.Session;

public static class PhoneStartReadiness
{
    public static bool CanStart(AndroidConnectionSnapshot? connection, bool steamVrReady, string? deviceKey)
    {
        // Ready is published only after a successful ADB scan and device probe.
        return steamVrReady && connection is { IsReady: true, SelectedDevice: not null } &&
            !string.IsNullOrWhiteSpace(deviceKey) && connection.SelectedDevice.DeviceKey == deviceKey &&
            connection.Devices.Any(device => device.DeviceKey == deviceKey &&
                device.IsSelected && device.Status == AndroidDeviceStatus.Ready);
    }
}
