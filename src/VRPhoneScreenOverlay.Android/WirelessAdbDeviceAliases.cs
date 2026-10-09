namespace VRPhoneScreenOverlay.Android;

// A matching advertised endpoint proves an IP transport and a TLS service-name
// transport refer to the same connection. Model names never prove identity.
internal static class WirelessAdbDeviceAliases
{
    public static bool NeedsResolution(IReadOnlyList<AdbDeviceRecord> devices) =>
        devices.Any(device => device.Transport == AndroidTransport.Network &&
            WirelessAdbEndpoint.TryNormalize(device.Serial, out _)) &&
        devices.Any(device => device.Transport == AndroidTransport.Network &&
            device.Serial.Contains("._adb-tls-connect._tcp", StringComparison.Ordinal));

    public static IReadOnlyList<AdbDeviceRecord> Collapse(IReadOnlyList<AdbDeviceRecord> devices,
        string? mdns, string? preferredKey)
    {
        if (string.IsNullOrWhiteSpace(mdns)) { return devices; }
        HashSet<string> removed = new(StringComparer.Ordinal);
        foreach (AdbDeviceRecord ip in devices)
        {
            if (ip.Transport != AndroidTransport.Network || !WirelessAdbEndpoint.TryNormalize(ip.Serial, out _)) { continue; }
            string? service = WirelessAdbEndpoint.FindConnectService(mdns, ip.Serial);
            if (service is null) { continue; }
            AdbDeviceRecord[] matches = devices.Where(device => device.Transport == AndroidTransport.Network &&
                string.Equals(device.Serial.TrimEnd('.'), service, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1) { continue; }
            AdbDeviceRecord named = matches[0];
            bool keepIp = ip.Status == AndroidDeviceStatus.Ready &&
                (named.Status != AndroidDeviceStatus.Ready || ip.DeviceKey == preferredKey);
            removed.Add(keepIp ? named.DeviceKey : ip.DeviceKey);
        }
        return devices.Where(device => !removed.Contains(device.DeviceKey)).ToArray();
    }
}
