using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace VRPhoneScreenOverlay.Android;

internal sealed record AdbDeviceRecord(
    string Serial,
    string DeviceKey,
    string StateText,
    string Product,
    string Model,
    string DeviceCodeName,
    AndroidDeviceStatus Status,
    AndroidTransport Transport)
{
    public string? PhysicalDeviceKey { get; init; }
    public string? RecognizedName { get; init; }
}

internal static partial class AdbOutputParser
{
    public static IReadOnlyList<AdbDeviceRecord> ParseDevices(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        List<AdbDeviceRecord> devices = [];
        foreach (string rawLine in output.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 ||
                line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith('*'))
            {
                continue;
            }

            int separator = IndexOfWhitespace(line);
            if (separator <= 0)
            {
                continue;
            }

            string serial = line[..separator];
            string remainder = line[separator..].TrimStart();
            string stateText = ReadState(remainder);
            Dictionary<string, string> properties = PropertyPattern()
                .Matches(remainder)
                .ToDictionary(
                    match => match.Groups["name"].Value,
                    match => match.Groups["value"].Value,
                    StringComparer.OrdinalIgnoreCase);

            AndroidTransport transport = ClassifyTransport(serial, properties);
            devices.Add(new AdbDeviceRecord(
                serial,
                CreateDeviceKey(serial),
                stateText,
                ReadProperty(properties, "product"),
                ReadProperty(properties, "model").Replace('_', ' '),
                ReadProperty(properties, "device"),
                ClassifyStatus(stateText),
                transport));
        }

        return devices;
    }

    public static IReadOnlyDictionary<string, string> ParseProperties(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        Dictionary<string, string> properties = new(StringComparer.Ordinal);
        string normalized = output.Replace("\r", string.Empty, StringComparison.Ordinal);
        foreach (Match match in GetPropertyPattern().Matches(normalized))
        {
            properties[match.Groups["name"].Value] = match.Groups["value"].Value;
        }

        return properties;
    }

    public static string CreateDeviceKey(string serial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(serial));
        return Convert.ToHexStringLower(hash.AsSpan(0, 6));
    }

    private static int IndexOfWhitespace(string value)
    {
        for (int index = 0; index < value.Length; index++)
        {
            if (char.IsWhiteSpace(value[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static string ReadState(string remainder)
    {
        if (remainder.StartsWith("no permissions", StringComparison.OrdinalIgnoreCase))
        {
            return "no permissions";
        }

        int separator = IndexOfWhitespace(remainder);
        return separator < 0 ? remainder : remainder[..separator];
    }

    private static AndroidDeviceStatus ClassifyStatus(string state) =>
        state.ToLowerInvariant() switch
        {
            "device" => AndroidDeviceStatus.Ready,
            "unauthorized" => AndroidDeviceStatus.Unauthorized,
            "offline" => AndroidDeviceStatus.Offline,
            "no permissions" => AndroidDeviceStatus.NoPermissions,
            _ => AndroidDeviceStatus.Unknown,
        };

    private static AndroidTransport ClassifyTransport(
        string serial,
        Dictionary<string, string> properties)
    {
        if (serial.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase))
        {
            return AndroidTransport.Emulator;
        }

        if (properties.ContainsKey("usb") ||
            (!serial.Contains(':') && !serial.Contains("._adb-tls-", StringComparison.OrdinalIgnoreCase)))
        {
            return AndroidTransport.Usb;
        }

        if (serial.Contains(':') || serial.Contains("._adb-tls-", StringComparison.OrdinalIgnoreCase))
        {
            return AndroidTransport.Network;
        }

        return AndroidTransport.Unknown;
    }

    private static string ReadProperty(
        Dictionary<string, string> properties,
        string name) =>
        properties.TryGetValue(name, out string? value) ? value : string.Empty;

    [GeneratedRegex(@"(?<name>[A-Za-z0-9_.-]+):(?<value>[^\s]+)", RegexOptions.CultureInvariant)]
    private static partial Regex PropertyPattern();

    [GeneratedRegex(@"^\[(?<name>[^\]]+)\]: \[(?<value>.*)\]$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex GetPropertyPattern();
}
