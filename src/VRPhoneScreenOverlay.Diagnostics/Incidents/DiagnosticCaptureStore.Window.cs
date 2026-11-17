using System.IO.Compression;
using System.Text.Json;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Diagnostics;

internal static partial class DiagnosticCaptureStore
{
    internal static async Task<string?> ExportWindowAsync(string root, DateTimeOffset end, CancellationToken token, string? ownedActiveCapture = null)
    {
        using FileStream store = Lock(root);
        DateTimeOffset start = end - DiagnosticRetentionPolicy.UploadWindow;
        string exports = Path.Combine(root, "exports");
        IncidentStorage.ValidateDirectory(exports);
        Directory.CreateDirectory(exports);
        string final = Path.Combine(exports, "window-" + Guid.NewGuid().ToString("N") + ".zip");
        string temporary = final + ".tmp";
        List<MemoryWindowEntry> entries = [];
        long rawBytes = 0;
        int skippedActive = 0;
        int incomplete = 0;
        try
        {
            using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (ZipArchive zip = new(output, ZipArchiveMode.Create))
            {
                foreach (DirectoryInfo run in Runs(root).OrderBy(d => d.Name, StringComparer.Ordinal))
                { await AddRunAsync(run).ConfigureAwait(false); }
                MemoryWindowManifest manifest = new(1, start, end, "retained-complete-dumps-by-recorded-time",
                    entries.ToArray(), rawBytes, skippedActive, incomplete);
                await using Stream index = await zip.CreateEntry("window-index.json").OpenAsync(token).ConfigureAwait(false);
                await JsonSerializer.SerializeAsync(index, manifest, IncidentJsonContext.Default.MemoryWindowManifest, token).ConfigureAwait(false);

                async Task AddRunAsync(DirectoryInfo run)
                {
                    IncidentStorage.ValidateDirectory(run.FullName);
                    await using FileStream? lease = TryLockRun(run.FullName);
                    if (lease is null && run.Name != ownedActiveCapture) { skippedActive++; return; }
                    string directory = Path.Combine(run.FullName, "private-dumps");
                    if (!Directory.Exists(directory)) { return; }
                    IncidentStorage.ValidateDirectory(directory);
                    bool includedRun = false;
                    foreach (FileInfo dump in new DirectoryInfo(directory).EnumerateFiles("*.dmp").Where(f => IsDumpFile(f.Name)))
                    {
                        DiagnosticCaptureRecord? record = IncidentStorage.Read(dump.FullName + ".json", IncidentJsonContext.Default.DiagnosticCaptureRecord);
                        if (record is not { Status: "saved" } || record.FileName != dump.Name || record.Bytes != dump.Length)
                        { incomplete++; continue; }
                        if (record.RecordedAt < start || record.RecordedAt > end) { continue; }
                        await AddAsync(dump, run.Name + "/" + dump.Name).ConfigureAwait(false);
                        await AddAsync(new(dump.FullName + ".json"), run.Name + "/" + dump.Name + ".json").ConfigureAwait(false);
                        entries.Add(new(run.Name, dump.Name, record.RecordedAt, record.Trigger, record.Bytes));
                        includedRun = true;
                    }
                    if (includedRun)
                    {
                        foreach (string name in new[] { "state.json", "flight-record.json", "capture-status.json" })
                        {
                            FileInfo file = new(Path.Combine(run.FullName, name));
                            if (file.Exists) { await AddAsync(file, run.Name + "/" + name).ConfigureAwait(false); }
                        }
                    }
                }

                async Task AddAsync(FileInfo file, string name)
                {
                    if (file.Length > 32L * 1024 * 1024 || (file.Attributes & FileAttributes.ReparsePoint) != 0)
                    { throw new IOException("Invalid memory window file."); }
                    await using FileStream source = new(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                    if (source.Length > 32L * 1024 * 1024 || (file.Extension == ".dmp" && source.Length != file.Length))
                    { throw new IOException("Memory window file changed during export."); }
                    rawBytes += source.Length;
                    if (rawBytes > 512L * 1024 * 1024) { throw new IOException("Memory window export exceeds local limit."); }
                    await using Stream entry = await zip.CreateEntry(name, CompressionLevel.Fastest).OpenAsync(token).ConfigureAwait(false);
                    await source.CopyToAsync(entry, token).ConfigureAwait(false);
                }
            }
            if (entries.Count == 0) { return null; }
            File.Move(temporary, final);
            foreach (FileInfo old in new DirectoryInfo(exports).EnumerateFiles("*.zip").OrderByDescending(f => f.LastWriteTimeUtc).Skip(2))
            { if ((old.Attributes & FileAttributes.ReparsePoint) == 0) { old.Delete(); } }
            return final;
        }
        finally { IncidentStorage.TryDelete(temporary); }
    }
}

internal sealed record MemoryWindowEntry(string CaptureId, string FileName, DateTimeOffset RecordedAt, string Trigger, long Bytes);
internal sealed record MemoryWindowManifest(int SchemaVersion, DateTimeOffset WindowStart, DateTimeOffset WindowEnd,
    string SelectionPolicy, MemoryWindowEntry[] Dumps, long UncompressedBytes, int SkippedActiveCaptures, int IncompleteFiles);
