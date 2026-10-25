using System.Text.Json;

namespace VRPhoneScreenOverlay.Android;

public sealed record AdbServerSummary(string Status, string? Version = null, string? Build = null,
    string? UsbBackend = null, string? MdnsBackend = null, string? ExecutableOrigin = null)
{
    public DateTimeOffset? SampledAt { get; init; }
}

internal static class AdbServerSummaryParser
{
    internal static AdbServerSummary Parse(string text, string runtimeExecutable, string sourceExecutable)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        foreach (string line in text.Split('\n').Take(32))
        {
            int separator = line.IndexOf(':');
            if (separator <= 0) { continue; }
            string key = line[..separator].Trim();
            if (key is not ("version" or "build" or "usb_backend" or "mdns_backend" or "executable_absolute_path")) { continue; }
            string value = line[(separator + 1)..].Trim();
            if (value.Length > 4096) { continue; }
            if (value.StartsWith('"'))
            {
                try { value = JsonSerializer.Deserialize<string>(value) ?? ""; }
                catch (JsonException) { continue; }
            }
            values[key] = value;
        }
        string origin = "unavailable";
        if (values.TryGetValue("executable_absolute_path", out string? executable))
        {
            try
            {
                string path = Path.GetFullPath(executable);
                origin = path.Equals(Path.GetFullPath(runtimeExecutable), StringComparison.OrdinalIgnoreCase) ? "runtime-copy" :
                    path.Equals(Path.GetFullPath(sourceExecutable), StringComparison.OrdinalIgnoreCase) ? "installation" : "other-executable";
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException) { }
        }
        return new(values.Count == 0 ? "unrecognized" : "available", Version(values.GetValueOrDefault("version")),
            Version(values.GetValueOrDefault("build")), values.GetValueOrDefault("usb_backend") is "NATIVE" or "LIBUSB" ? values["usb_backend"] : null,
            values.GetValueOrDefault("mdns_backend") is "BONJOUR" or "OPENSCREEN" ? values["mdns_backend"] : null, origin);
    }

    private static string? Version(string? text) => text is { Length: > 0 and <= 96 } &&
        text.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_') ? text : null;
}
