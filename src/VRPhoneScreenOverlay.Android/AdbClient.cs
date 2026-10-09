using System.Globalization;

namespace VRPhoneScreenOverlay.Android;

internal interface IAdbClient
{
    public ValueTask<AdbListResult> ListDevicesAsync(CancellationToken cancellationToken);

    public ValueTask<AdbProbeResult> ProbeAsync(string serial, CancellationToken cancellationToken);

    public ValueTask<string?> ResolveWirelessServiceAsync(string endpoint, CancellationToken cancellationToken);

}

internal sealed record AdbListResult(
    IReadOnlyList<AdbDeviceRecord> Devices,
    TimeSpan Duration,
    string ToolVersion,
    string? WirelessServices = null);

internal sealed record AdbProbeResult(
    bool Succeeded,
    string ReasonCode,
    string Message,
    string Manufacturer,
    string Brand,
    string Model,
    string DeviceCodeName,
    string AndroidVersion,
    int? AndroidSdk,
    string CpuAbi,
    int DisplayWidth,
    int DisplayHeight,
    TimeSpan Duration)
{
    public string MarketName { get; init; } = string.Empty;
    public string? PhysicalDeviceKey { get; init; }
}

internal sealed class AdbClient(
    AdbCommandRunner runner,
    TimeSpan commandTimeout) : IAdbClient
{
    private readonly AdbCommandRunner _runner = runner;
    private readonly TimeSpan _commandTimeout = commandTimeout;
    private string? _toolVersion;

    public async ValueTask<string?> ResolveWirelessServiceAsync(string endpoint, CancellationToken cancellationToken)
    {
        AdbCommandResult result = await _runner.RunAsync(
            ["mdns", "services"], TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? WirelessAdbEndpoint.FindConnectService(result.StandardOutput, endpoint) : null;
    }

    public async ValueTask<AdbListResult> ListDevicesAsync(CancellationToken cancellationToken)
    {
        _toolVersion ??= await ReadToolVersionAsync(cancellationToken).ConfigureAwait(false);
        AdbCommandResult result = await _runner.RunAsync(
            ["devices", "-l"],
            _commandTimeout,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, AndroidReasonCodes.Scan);
        IReadOnlyList<AdbDeviceRecord> devices = AdbOutputParser.ParseDevices(result.StandardOutput);
        string? wirelessServices = null;
        if (WirelessAdbDeviceAliases.NeedsResolution(devices))
        {
            // An unavailable discovery service must not hide existing USB/Wi-Fi transports.
            try
            {
                AdbCommandResult mdns = await _runner.RunAsync(["mdns", "services"],
                    TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                if (mdns.Succeeded) { wirelessServices = mdns.StandardOutput; }
            }
            catch (Exception exception) when (exception is AndroidConnectionException or IOException or
                UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                // Alias resolution is optional. Preserve the authoritative device list.
            }
        }
        return new AdbListResult(devices, result.Duration, _toolVersion, wirelessServices);
    }

    public async ValueTask<AdbProbeResult> ProbeAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        AdbCommandResult result = await _runner.RunAsync(
            ["-s", serial, "shell", "getprop"],
            _commandTimeout,
            cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return new AdbProbeResult(
                false,
                result.TimedOut ? AndroidReasonCodes.ProbeTimeout : AndroidReasonCodes.ProbeFailed,
                result.TimedOut ? "读取手机信息超时" : "手机已发现，但暂时无法读取系统信息",
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                null,
                string.Empty,
                0,
                0,
                result.Duration);
        }

        IReadOnlyDictionary<string, string> properties =
            AdbOutputParser.ParseProperties(result.StandardOutput);
        string sdkText = ReadProperty(properties, "ro.build.version.sdk");
        int? sdk = int.TryParse(sdkText, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;
        AdbCommandResult display = await _runner.RunAsync(
            ["-s", serial, "shell", "wm", "size"],
            _commandTimeout,
            cancellationToken).ConfigureAwait(false);
        AndroidDisplaySize displaySize = display.Succeeded &&
            AndroidDisplaySize.TryParseWmSize(display.StandardOutput, out AndroidDisplaySize parsed)
                ? parsed
                : default;
        return new AdbProbeResult(
            true,
            AndroidReasonCodes.ProbeReady,
            "手机系统信息已读取",
            ReadProperty(properties, "ro.product.manufacturer"),
            ReadProperty(properties, "ro.product.brand"),
            ReadProperty(properties, "ro.product.model"),
            ReadProperty(properties, "ro.product.device"),
            ReadProperty(properties, "ro.build.version.release"),
            sdk,
            ReadProperty(properties, "ro.product.cpu.abi"),
            displaySize.Width,
            displaySize.Height,
            result.Duration + display.Duration)
        {
            MarketName = AndroidDeviceIdentity.ReadMarketName(properties),
            PhysicalDeviceKey = PhysicalIdentity(properties),
        };
    }

    private static string? PhysicalIdentity(IReadOnlyDictionary<string, string> properties)
    {
        foreach (string name in new[] { "ro.serialno", "ro.boot.serialno" })
        {
            string value = ReadProperty(properties, name).Trim();
            if (value.Length > 2 && value.Length <= 256 && !value.Equals("unknown", StringComparison.OrdinalIgnoreCase) && value.Any(character => character != '0'))
            { return AdbOutputParser.CreateDeviceKey(value); }
        }
        return null;
    }


    private async ValueTask<string> ReadToolVersionAsync(CancellationToken cancellationToken)
    {
        AdbCommandResult result = await _runner.RunAsync(
            ["version"],
            _commandTimeout,
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, AndroidReasonCodes.ToolVersion);
        string[] lines = result.StandardOutput
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.FirstOrDefault(line => line.StartsWith("Version ", StringComparison.OrdinalIgnoreCase))
            ?? lines.FirstOrDefault()
            ?? "unknown";
    }

    private static void EnsureSuccess(AdbCommandResult result, string operation)
    {
        if (result.TimedOut)
        {
            throw new AndroidConnectionException($"{operation}_TIMEOUT", "手机连接工具响应超时");
        }

        if (!result.Succeeded)
        {
            throw new AndroidConnectionException($"{operation}_FAILED", "手机连接工具执行失败");
        }
    }

    private static string ReadProperty(
        IReadOnlyDictionary<string, string> properties,
        string name) =>
        properties.TryGetValue(name, out string? value) ? value.Trim() : string.Empty;

}

public sealed class AndroidConnectionException(string reasonCode, string message) : Exception(message)
{
    public string ReasonCode { get; } = reasonCode;
}
