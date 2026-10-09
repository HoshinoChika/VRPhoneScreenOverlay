using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace VRPhoneScreenOverlay.Android;

internal static class WirelessAdbEndpoint
{
    internal static string[] FindServices(string output, string type, string? name, string? host)
    {
        return output.Split('\n').Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(fields => fields.Length == 3 && fields[1].TrimEnd('.') == type &&
                (name is null || fields[0].TrimEnd('.') == name) && TryNormalize(fields[2], out _) &&
                (host is null || fields[2][..fields[2].LastIndexOf(':')] == host))
            .Select(fields => { TryNormalize(fields[2], out string value); return value; })
            .Distinct(StringComparer.Ordinal).Take(2).ToArray();
    }

    public static string? FindConnectService(string output, string endpoint)
    {
        if (!TryNormalize(endpoint, out string requested)) { return null; }
        foreach (string line in output.Split('\n'))
        {
            string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 3 ||
                !string.Equals(fields[1].TrimEnd('.'), "_adb-tls-connect._tcp", StringComparison.Ordinal) ||
                !TryNormalize(fields[2], out string candidate) ||
                !string.Equals(candidate, requested, StringComparison.Ordinal))
            {
                continue;
            }
            return fields[0].TrimEnd('.') + "._adb-tls-connect._tcp";
        }
        return null;
    }

    public static bool TryNormalize(string value, out string endpoint)
    {
        endpoint = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80) { return false; }
        value = value.Trim();
        int separator = value.LastIndexOf(':');
        if (separator < 1 || !int.TryParse(value.AsSpan(separator + 1), NumberStyles.None,
                CultureInfo.InvariantCulture, out int port) || port is < 1 or > 65535)
        {
            return false;
        }

        string host = value[..separator];
        bool bracketed = host.StartsWith('[') && host.EndsWith(']');
        if (bracketed) { host = host[1..^1]; }
        if (!IPAddress.TryParse(host, out IPAddress? address) || IPAddress.IsLoopback(address))
        {
            return false;
        }

        byte[] bytes = address.GetAddressBytes();
        bool allowed = address.AddressFamily == AddressFamily.InterNetwork
            ? !bracketed && host.Split('.').Length == 4 &&
              (bytes[0] == 10 || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 169 && bytes[1] == 254))
            : bracketed && (address.IsIPv6LinkLocal || (bytes[0] & 0xfe) == 0xfc);
        if (!allowed) { return false; }
        endpoint = address.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{address}]:{port.ToString(CultureInfo.InvariantCulture)}"
            : $"{address}:{port.ToString(CultureInfo.InvariantCulture)}";
        return true;
    }
}
