using System.Diagnostics;
using System.Text;
using System.Text.Json;
using VRPhoneScreenOverlay.Protocols;

namespace VRPhoneScreenOverlay.Diagnostics;

/// <summary>Read-only, fixed application data sources; no model-supplied paths, processes or commands.</summary>
public sealed class LocalDiagnosticReadTools(string logDirectory)
{
    public static TroubleshootingEvidence SteamVrProcesses()
    {
        string Present(string name)
        {
            try
            {
                Process[] processes = Process.GetProcessesByName(name);
                bool present = processes.Length > 0;
                foreach (Process process in processes) { process.Dispose(); }
                return present ? "True" : "False";
            }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
            { return "unknown"; }
        }
        return new("steamvrProcesses", "runtime_processes", $"vrserverPresent={Present("vrserver")};vrmonitorPresent={Present("vrmonitor")};processPresenceDoesNotProveReadiness=true");
    }

    public async Task<TroubleshootingCheckResult> ReadLogsAsync(CancellationToken token)
    {
        DateTimeOffset start = DateTimeOffset.UtcNow;
        List<string> lines = [];
        int malformed = 0, omitted = 0;
        try
        {
            IncidentStorage.ValidateDirectory(logDirectory);
            if (!Directory.Exists(logDirectory)) { return Result("unavailable", "没有可读取的近期日志。"); }
            string[] paths = Directory.EnumerateFiles(logDirectory, "android-*.jsonl").Take(129).ToArray();
            if (paths.Length > 128) { return Result("unavailable", "日志目录超过本次读取范围。"); }
            foreach (string path in paths.OrderByDescending(p => p, StringComparer.Ordinal).Take(2).OrderBy(p => p, StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) { omitted++; continue; }
                await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.Asynchronous);
                long length = stream.Length;
                int bytes = (int)Math.Min(length, 256 * 1024);
                stream.Seek(length - bytes, SeekOrigin.Begin);
                byte[] buffer = new byte[bytes];
                await stream.ReadExactlyAsync(buffer, token).ConfigureAwait(false);
                using StringReader reader = new(Encoding.UTF8.GetString(buffer));
                if (length > bytes) { _ = await reader.ReadLineAsync(token).ConfigureAwait(false); omitted++; }
                while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        using JsonDocument document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 24 });
                        JsonElement root = document.RootElement;
                        if (!root.TryGetProperty("timestamp", out JsonElement stamp) || !stamp.TryGetDateTimeOffset(out DateTimeOffset at) || at < start.AddMinutes(-10)) { continue; }
                        string Read(string key, int limit) => root.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String
                            ? DiagnosticSanitizer.SanitizeText(value.GetString()!)[..Math.Min(DiagnosticSanitizer.SanitizeText(value.GetString()!).Length, limit)] : "unknown";
                        // Keep metadata, never original exception text, command arguments, serials or arbitrary object fields.
                        string safe = $"at={at:O};event={Read("eventName", 70)};reason={Read("reasonCode", 80)};message={Read("message", 160)}";
                        lines.Add(safe.Replace('\r', ' ').Replace('\n', ' '));
                        if (lines.Count > 48) { lines.RemoveAt(0); omitted++; }
                    }
                    catch (Exception error) when (error is JsonException or InvalidOperationException) { malformed++; }
                }
            }
            return Result("completed", "已读取近期日志片段，完整性和时间范围已标注。");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return Result("unavailable", "近期日志读取未完成，不能据此认为没有异常。"); }

        TroubleshootingCheckResult Result(string status, string summary)
        {
            List<TroubleshootingEvidence> evidence = [new("coverage", "log_coverage", $"lookbackMinutes=10;tailBytesPerFile=262144;maxFiles=2;observedLines={lines.Count};malformed={malformed};omitted={omitted};partialWindow=true")];
            string chunk = string.Empty;
            foreach (string line in lines)
            {
                if (chunk.Length + line.Length > 950)
                {
                    if (evidence.Count >= 7) { evidence[0] = evidence[0] with { Observation = evidence[0].Observation + ";outputTruncated=true" }; break; }
                    evidence.Add(new("log" + evidence.Count, "recent_logs", chunk));
                    chunk = string.Empty;
                }
                chunk += line + " | ";
            }
            if (chunk.Length > 0 && evidence.Count < 8) { evidence.Add(new("log" + evidence.Count, "recent_logs", chunk)); }
            return new(TroubleshootingChecks.Logs, start, DateTimeOffset.UtcNow, status, summary, evidence.ToArray());
        }
    }
}
