namespace VRPhoneScreenOverlay.Android;

internal static class AndroidDeviceSelector
{
    public static AdbDeviceRecord? Select(
        IReadOnlyList<AdbDeviceRecord> devices,
        string? preferredDeviceKey)
    {
        ArgumentNullException.ThrowIfNull(devices);

        AdbDeviceRecord[] ready = devices
            .Where(device => device.Status == AndroidDeviceStatus.Ready &&
                device.Transport is AndroidTransport.Usb or AndroidTransport.Network)
            .ToArray();
        if (!string.IsNullOrWhiteSpace(preferredDeviceKey))
        {
            AdbDeviceRecord? preferred = ready.FirstOrDefault(device =>
                string.Equals(device.DeviceKey, preferredDeviceKey, StringComparison.Ordinal));
            if (preferred is not null)
            {
                return preferred;
            }
        }

        return ready
            .OrderBy(device => device.Transport == AndroidTransport.Usb ? 0 : 1)
            .ThenBy(device => device.Model, StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.DeviceKey, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
