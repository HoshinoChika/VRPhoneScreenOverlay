using System.Text.RegularExpressions;

namespace VRPhoneScreenOverlay.Android;

internal static partial class AdbOutputRedactor
{
    internal static string Summarize(string text, IReadOnlyList<string> arguments, string? input, string? host)
    {
        // Input text may be echoed by Android errors; omit that diagnostic body entirely.
        if (arguments.Contains("input", StringComparer.Ordinal) && arguments.Contains("text", StringComparer.Ordinal)) { return "<input-output-omitted>"; }
        string safe = text;
        foreach (string? secret in arguments.Where(value => value.Length >= 4 && value is not
            ("devices" or "version" or "server-status" or "mdns" or "shell" or "getprop" or "input" or "keyevent" or "forward" or "connect" or "disconnect" or "pair"))
            .Concat([input, host, Environment.GetEnvironmentVariable("ANDROID_SERIAL")]))
        {
            if (!string.IsNullOrWhiteSpace(secret) && secret.Length >= 3) { safe = safe.Replace(secret, "<redacted>", StringComparison.Ordinal); }
        }
        try
        {
            safe = SecretPattern().Replace(safe, "$1=<redacted>");
            safe = WindowsPathPattern().Replace(safe, "<path>");
            safe = UnixPathPattern().Replace(safe, "<path>");
            safe = AddressPattern().Replace(safe, "<address>");
            safe = Ipv6Pattern().Replace(safe, "<address>");
            return safe.Length <= 800 ? safe : safe[^800..];
        }
        catch (RegexMatchTimeoutException) { return "<redaction-timeout>"; }
    }

    [GeneratedRegex("(?i)\\b(token|password|passwd|secret|authorization|serial|pairing[_ -]?code)\\s*[:=]\\s*[^\\r\\n,;]+", RegexOptions.CultureInvariant, 50)]
    private static partial Regex SecretPattern();
    [GeneratedRegex(@"(?i)(?:[a-z]:[\\/]|\\\\)[^\r\n]+", RegexOptions.CultureInvariant, 50)]
    private static partial Regex WindowsPathPattern();
    [GeneratedRegex(@"(?<!\S)/(?:[^\s]+)", RegexOptions.CultureInvariant, 50)]
    private static partial Regex UnixPathPattern();
    [GeneratedRegex(@"(?<!\d)(?:\d{1,3}\.){3}\d{1,3}(?::\d+)?", RegexOptions.CultureInvariant, 50)]
    private static partial Regex AddressPattern();
    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:[0-9A-Fa-f]{0,4}:){2,}[0-9A-Fa-f:.%]*", RegexOptions.CultureInvariant, 50)]
    private static partial Regex Ipv6Pattern();
}
