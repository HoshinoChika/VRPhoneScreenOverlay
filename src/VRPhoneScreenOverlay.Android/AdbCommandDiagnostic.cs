using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace VRPhoneScreenOverlay.Android;

public sealed record AdbCommandDiagnostic(string Command, string EndpointSource, string EndpointKind,
    int? ServerPort, string EndpointTag, string ToolLocation,
    int? ExitCode = null, bool TimedOut = false, bool OutputTruncated = false,
    string ErrorCategory = "none", string? ExceptionType = null, int? HResult = null, int? NativeErrorCode = null,
    string? TimeoutPhase = null)
{
    public int? ServerProtocolVersion { get; init; }
    public string? TargetDeviceKey { get; init; }
    public bool ClientStillRunning { get; init; }
    public int? CleanupNativeErrorCode { get; init; }
    public bool NativeServerRestartAttempted { get; init; }
    public string? CommandDetail { get; init; }
    public string? StandardErrorSummary { get; init; }
}

internal sealed record AdbEndpoint(AdbCommandDiagnostic Diagnostic, string? Host);

internal static class AdbDiagnosticPolicy
{
    internal static AdbCommandDiagnostic Describe(IReadOnlyList<string> arguments, string location,
        Func<string, string?>? environment = null) => Resolve(arguments, location, environment).Diagnostic;

    internal static AdbEndpoint Resolve(IReadOnlyList<string> arguments, string location,
        Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        string? host = null;
        string? port = null;
        string? socket = null;
        int index = 0;
        bool specified = false;
        string? serial = null;
        bool selected = false;
        while (index < arguments.Count)
        {
            string argument = arguments[index];
            if (argument is "-s" or "-t")
            { if (argument == "-s" && index + 1 < arguments.Count) { serial = arguments[index + 1]; } selected = true; index += 2; continue; }
            if (argument is "-d" or "-e" or "-a") { selected |= argument is "-d" or "-e"; index++; continue; }
            if (argument is "-L" or "-H" or "-P")
            {
                string value = index + 1 < arguments.Count ? arguments[index + 1] : "";
                if (argument == "-L") { socket = value; }
                else if (argument == "-H") { host = value; }
                else { port = value; }
                index += 2; specified = true; continue;
            }
            if (argument.StartsWith("-P", StringComparison.Ordinal) && argument.Length > 2)
            { port = argument[2..]; index++; specified = true; continue; }
            if (argument.StartsWith("-H", StringComparison.Ordinal) && argument.Length > 2)
            { host = argument[2..]; index++; specified = true; continue; }
            break;
        }
        string command = index < arguments.Count ? arguments[index] : "unknown";
        bool conflicting = socket is not null && (host is not null || port is not null);
        if (command is not ("devices" or "version" or "server-status" or "mdns" or "shell" or "push" or "forward" or
            "connect" or "disconnect" or "pair" or "tcpip" or "get-state" or "start-server")) { command = "other"; }
        if (!specified) { socket = environment("ADB_SERVER_SOCKET"); }
        string source = specified ? "arguments" : socket is not null ? "ADB_SERVER_SOCKET" :
            environment("ANDROID_ADB_SERVER_ADDRESS") is not null || environment("ANDROID_ADB_SERVER_PORT") is not null ? "ANDROID_ADB_SERVER_ADDRESS/PORT" : "default";
        string kind;
        string endpoint;
        int? number = null;
        if (socket is not null)
        {
            endpoint = socket;
            if (socket.StartsWith("tcp:", StringComparison.Ordinal))
            {
                string target = socket[4..];
                int colon = target.LastIndexOf(':');
                if (int.TryParse(target, NumberStyles.Integer, CultureInfo.InvariantCulture, out int socketPort))
                { host = "localhost"; port = socketPort.ToString(CultureInfo.InvariantCulture); }
                else if (target.StartsWith('[') && target.IndexOf(']') is int bracket && bracket > 0)
                {
                    host = target[1..bracket];
                    port = bracket + 1 == target.Length ? "5555" : target[(bracket + 1)..].StartsWith(':') ? target[(bracket + 2)..] : "invalid";
                }
                else if (colon < 0) { host = target; port = "5555"; }
                else if (target.IndexOf(':') == colon) { host = target[..colon]; port = target[(colon + 1)..]; }
                else { host = null; port = null; }
                number = ParsePort(port);
                kind = host is null ? "unknown-tcp" : number is null ? "invalid" : HostKind(host);
            }
            else { kind = socket.StartsWith("local", StringComparison.Ordinal) ? "local-socket" : "unknown-socket"; }
        }
        else
        {
            host ??= environment("ANDROID_ADB_SERVER_ADDRESS") ?? "localhost";
            port ??= environment("ANDROID_ADB_SERVER_PORT") ?? "5037";
            number = ParsePort(port.Length == 0 ? "5037" : port);
            kind = number is null ? "invalid" : HostKind(host);
            endpoint = "tcp:" + host + ":" + port;
        }
        if (conflicting) { kind = "invalid"; }
        string tag = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint)))[..16];
        if (!selected) { serial = environment("ANDROID_SERIAL"); }
        string? detail = null;
        if (command == "shell" && index + 1 < arguments.Count)
        {
            string verb = arguments[index + 1];
            detail = verb is "input" or "getprop" or "wm" or "dumpsys" or "cmd" or "settings" or "app_process" ? verb : "other-shell";
        }
        else if (command == "forward" && index + 1 < arguments.Count)
        { detail = arguments[index + 1] == "--remove" ? "remove-owned-forward" : "configure-forward"; }
        // AOSP's Windows network_loopback_client uses IPv4 for its default/localhost case.
        // Do not probe an unrelated IPv6 listener while adb will use 127.0.0.1.
        string? probeHost = host == "localhost" ? "127.0.0.1" : host?.Trim('[', ']');
        return new(new(command, source, kind, number, tag, location)
        { TargetDeviceKey = string.IsNullOrWhiteSpace(serial) ? null : AdbOutputParser.CreateDeviceKey(serial), CommandDetail = detail },
            kind is "loopback" or "remote" ? probeHost : null);
    }

    internal static string ErrorCategory(string text)
    {
        if (text.Contains("version", StringComparison.OrdinalIgnoreCase) && text.Contains("match", StringComparison.OrdinalIgnoreCase)) { return "server-version-mismatch"; }
        if (text.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)) { return "device-unauthorized"; }
        if (text.Contains("offline", StringComparison.OrdinalIgnoreCase)) { return "device-offline"; }
        if (text.Contains("INJECT_EVENTS", StringComparison.Ordinal)) { return "input-permission-denied"; }
        if (text.Contains("SecurityException", StringComparison.Ordinal)) { return "android-security-exception"; }
        if (text.Contains("cannot connect", StringComparison.OrdinalIgnoreCase) || text.Contains("connection refused", StringComparison.OrdinalIgnoreCase)) { return "server-unreachable"; }
        if (text.Contains("more than one", StringComparison.OrdinalIgnoreCase)) { return "multiple-devices"; }
        if (text.Contains("unknown command", StringComparison.OrdinalIgnoreCase) || text.Contains("unknown host service", StringComparison.OrdinalIgnoreCase)) { return "unsupported-command"; }
        if (text.Contains("not found", StringComparison.OrdinalIgnoreCase) || text.Contains("no devices", StringComparison.OrdinalIgnoreCase)) { return "device-not-found"; }
        return string.IsNullOrWhiteSpace(text) ? "none" : "other-tool-error";
    }

    private static int? ParsePort(string? text) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int port) && port is > 0 and <= 65535 ? port : null;
    private static string HostKind(string host) => string.IsNullOrWhiteSpace(host) ? "invalid" :
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        IPAddress.TryParse(host.Trim('[', ']'), out IPAddress? address) && IPAddress.IsLoopback(address) ? "loopback" : "remote";
}
