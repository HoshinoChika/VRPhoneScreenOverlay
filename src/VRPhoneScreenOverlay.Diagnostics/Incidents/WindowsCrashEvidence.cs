using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace VRPhoneScreenOverlay.Diagnostics;

internal static class WindowsCrashEvidence
{
    internal static async Task<IncidentCrashEvidence> ReadAsync(int pid, string processName,
        DateTimeOffset startedAt, DateTimeOffset exitedAt, CancellationToken token)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        ProcessStartInfo start = new()
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wevtutil.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        string from = exitedAt.AddSeconds(-10).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        string until = exitedAt.AddSeconds(5).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        string query = $"/q:*[System[(EventID=1000) and TimeCreated[@SystemTime >= '{from}' and @SystemTime <= '{until}']]]";
        foreach (string arg in new[] { "qe", "Application", query,
            "/f:xml", "/e:Events", "/c:16", "/rd:true" }) { start.ArgumentList.Add(arg); }
        using Process process = new() { StartInfo = start };
        try
        {
            if (!process.Start()) { return new("query-failed", []); }
            Task<string> output = ReadBoundedAsync(process.StandardOutput, deadline.Token);
            Task<string> errors = ReadBoundedAsync(process.StandardError, deadline.Token);
            try
            {
                await Task.WhenAll(output, errors, process.WaitForExitAsync(deadline.Token)).ConfigureAwait(false);
                return process.ExitCode == 0
                    ? Parse(await output.ConfigureAwait(false), pid, processName, startedAt, exitedAt)
                    : new("query-failed", []);
            }
            finally
            {
                if (!process.HasExited) { process.Kill(entireProcessTree: true); }
                // Observe both readers even if parsing, timeout or the other stream failed.
                try { await Task.WhenAll(output, errors).ConfigureAwait(false); }
                catch (Exception error) when (error is OperationCanceledException or IOException) { }
            }
        }
        catch (OperationCanceledException) { return new("query-timeout", []); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or InvalidOperationException or XmlException)
        { return new("query-failed", []); }
    }

    internal static IncidentCrashEvidence Parse(string xml, int pid, string processName,
        DateTimeOffset startedAt, DateTimeOffset exitedAt)
    {
        using StringReader text = new(xml);
        using XmlReader reader = XmlReader.Create(text, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65536 });
        XDocument document = XDocument.Load(reader);
        XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
        List<IncidentCrashEvent> matches = [];
        foreach (XElement item in document.Descendants(ns + "Event").Take(16))
        {
            string? time = (string?)item.Element(ns + "System")?.Element(ns + "TimeCreated")?.Attribute("SystemTime");
            if (!DateTimeOffset.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset at) ||
                at < startedAt || at > exitedAt.AddSeconds(5)) { continue; }
            Dictionary<string, string> fields = new(StringComparer.OrdinalIgnoreCase);
            foreach (XElement field in item.Element(ns + "EventData")?.Elements(ns + "Data") ?? [])
            {
                if ((string?)field.Attribute("Name") is string name) { fields[name] = field.Value; }
            }
            string processId = fields.GetValueOrDefault("ProcessId", "");
            bool hexadecimal = processId.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            if (!int.TryParse(hexadecimal ? processId[2..] : processId,
                hexadecimal ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture, out int eventPid) ||
                eventPid != pid || !string.Equals(fields.GetValueOrDefault("AppName"), processName + ".exe", StringComparison.OrdinalIgnoreCase))
            { continue; }
            matches.Add(new(at, SafeModule(fields.GetValueOrDefault("ModuleName", "unknown")),
                SafeHex(fields.GetValueOrDefault("ExceptionCode", "")), SafeHex(fields.GetValueOrDefault("FaultingOffset", ""))));
        }
        return new(matches.Count == 0 ? "no-matching-event" : "matched-application-error", matches.ToArray());
    }

    private static string SafeModule(string value)
    {
        string name = Path.GetFileName(value);
        return name.Length <= 128 ? name : "invalid-module";
    }

    private static string SafeHex(string value) =>
        value.Length <= 18 && value.All(c => char.IsAsciiHexDigit(c) || c is 'x' or 'X') ? value : "unavailable";

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token)
    {
        StringBuilder result = new();
        char[] buffer = new char[2048];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
        {
            if (result.Length + count > 65536) { throw new IOException("Crash event query exceeded capacity."); }
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
}
