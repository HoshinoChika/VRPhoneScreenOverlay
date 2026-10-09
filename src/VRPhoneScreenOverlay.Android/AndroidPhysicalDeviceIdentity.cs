namespace VRPhoneScreenOverlay.Android;

// Transport addresses and model names are not physical identities. Persist only
// opaque hashes, using authorized firmware identity or advertised TLS identity.
internal static class AndroidPhysicalDeviceIdentity
{
    public static string? FromService(string? service)
    {
        const string suffix = "._adb-tls-connect._tcp";
        if (service is null) { return null; }
        service = service.TrimEnd('.');
        if (!service.StartsWith("adb-", StringComparison.Ordinal) || !service.EndsWith(suffix, StringComparison.Ordinal)) { return null; }
        string name = service[4..^suffix.Length];
        int salt = name.LastIndexOf('-');
        return salt > 0 ? AdbOutputParser.CreateDeviceKey(name[..salt]) : null;
    }

    public static string Saved(SavedAndroidConnection saved) => saved.PhysicalDeviceKey ??
        (saved.Transport == AndroidTransport.Usb ? saved.DeviceKey : FromService(saved.ServiceName)) ?? "route:" + saved.DeviceKey;

    public static string Current(AdbDeviceRecord device, string? services, ConnectionPreferences preferences) => device.PhysicalDeviceKey ??
        (device.Transport == AndroidTransport.Usb ? AdbOutputParser.CreateDeviceKey(device.Serial) :
            FromService(WirelessAdbEndpoint.TryNormalize(device.Serial, out string endpoint)
                ? services is null ? null : WirelessAdbEndpoint.FindConnectService(services, endpoint) : device.Serial)) ??
        preferences.Devices.FirstOrDefault(saved => saved.DeviceKey == device.DeviceKey)?.PhysicalDeviceKey ?? "route:" + device.DeviceKey;
}
