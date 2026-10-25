using System.IO.Compression;
using System.Text.Json;

namespace VRPhoneScreenOverlay.Diagnostics;

internal static class DiagnosticCoverageReader
{
    internal static async Task<string?> ReadWarningAsync(ZipArchive archive, CancellationToken token)
    {
        using JsonDocument? report = await ReadAsync(archive, "log-collection.json", token).ConfigureAwait(false);
        if (report is null) { return "（缺少日志采集报告）"; }
        JsonElement root = report.RootElement;
        bool incomplete = Text(root, "status") is not ("selected" or "not-collected") ||
            True(root, "enumerationTruncated") || True(root, "incidentTimelinesIncomplete");
        bool hasRuntimeEvents = false;
        foreach (JsonElement file in Items(root, "files"))
        {
            incomplete |= Text(file, "status") != "collected" || Positive(file, "invalidLines") ||
                Positive(file, "unknownTimeLines") || True(file, "shortRead") || True(file, "outputTruncated");
            hasRuntimeEvents |= Positive(file, "lines") && !(Text(file, "file")?.StartsWith("incidents/", StringComparison.Ordinal) ?? false);
        }
        using JsonDocument? current = await ReadAsync(archive, "diagnostic-health.json", token).ConfigureAwait(false);
        JsonElement currentHealth = current?.RootElement ?? default;
        bool lost = false, pending = false, currentMatched = false;
        using JsonDocument? incidents = await ReadAsync(archive, "incident-summary.json", token).ConfigureAwait(false);
        if (incidents is not null)
        {
            JsonElement incidentRoot = incidents.RootElement;
            incomplete |= Text(incidentRoot, "status") == "collection-failed" || True(incidentRoot, "enumerationTruncated") ||
                Positive(incidentRoot, "omittedSessions");
            foreach (JsonElement session in Items(incidentRoot, "sessions"))
            {
                incomplete |= Text(session, "collectionStatus") is "collection-failed" or "state-missing";
                JsonElement health = Later(Part(session, "health"), Part(Part(session, "state"), "healthAtCheckpoint"));
                // A checkpoint can contain pending writes that a later snapshot
                // confirms were flushed. Compare within one process session only.
                if (Text(currentHealth, "sessionId") is { } id && id == Text(session, "sessionId"))
                { health = Later(health, currentHealth); currentMatched = true; }
                lost |= HasLoss(health);
                pending |= HasPending(health);
            }
        }
        if (!currentMatched) { lost |= HasLoss(currentHealth); pending |= HasPending(currentHealth); }
        if (lost || incomplete)
        { return "（部分诊断信息缺失或写入失败，详见包内报告）"; }
        if (pending)
        { return "（有诊断事件尚未落盘，时间覆盖可能不完整）"; }
        return hasRuntimeEvents ? null : "（未采集到运行日志，无法确认该时段覆盖）";
    }

    private static bool HasLoss(JsonElement health)
    {
        JsonElement log = Part(health, "log");
        return Positive(health, "incidentStorageFailures") ||
            Text(health, "incidentStorageStatus") is "write-failed" or "storage-unavailable" ||
            Text(health, "healthPersistenceStatus") is "write-failed" or "shutdown-timeout" ||
            Text(log, "state") is "write-failed" or "shutdown-timeout" ||
            Positive(log, "dropped") || Positive(log, "priorityDropped") || Positive(log, "rejected") ||
            Positive(log, "writeFailures");
    }

    private static bool HasPending(JsonElement health) => Positive(Part(health, "log"), "pending");

    private static JsonElement Later(JsonElement first, JsonElement second)
    {
        DateTimeOffset? a = Time(first), b = Time(second);
        return b is not null && (a is null || b >= a) ? second : first.ValueKind == JsonValueKind.Object ? first : second;
    }

    private static DateTimeOffset? Time(JsonElement health) =>
        Part(health, "capturedAt") is { ValueKind: JsonValueKind.String } value && value.TryGetDateTimeOffset(out DateTimeOffset time) ? time : null;

    private static JsonElement Part(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out JsonElement child) ? child : default;
    private static string? Text(JsonElement value, string name) =>
        Part(value, name) is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;
    private static bool Positive(JsonElement value, string name) =>
        Part(value, name) is { ValueKind: JsonValueKind.Number } number && number.TryGetInt64(out long count) && count > 0;
    private static bool True(JsonElement value, string name) => Part(value, name).ValueKind == JsonValueKind.True;
    private static JsonElement.ArrayEnumerator Items(JsonElement value, string name) =>
        Part(value, name) is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray() : default;

    private static async Task<JsonDocument?> ReadAsync(ZipArchive archive, string name, CancellationToken token)
    {
        if (archive.GetEntry(name) is not { } entry) { return null; }
        await using Stream stream = await entry.OpenAsync(token).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
    }
}
