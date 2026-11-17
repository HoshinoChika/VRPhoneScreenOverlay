using System.IO.Compression;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Diagnostics;

/// <summary>Bounded private capture retention; never follows links or removes unknown files.</summary>
internal static partial class DiagnosticCaptureStore
{
    private static readonly string[] _triggers = ["first-chance", "unhandled", "termination", "unresponsive", "functional-failure", "pipeline-stall", "user-report"];
    private static readonly HashSet<string> _rootFiles = new(StringComparer.Ordinal)
    { "state.json", "flight-record.json", "capture-status.json", "capture.exe", "capture.lock", "capture-ready.json", "capture-safe", "capture-stop", "capture-heartbeat", "capture-request" };

    internal static FileStream Lock(string root)
    {
        IncidentStorage.ValidateDirectory(root);
        Directory.CreateDirectory(root);
        return new(Path.Combine(root, "store.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    internal static bool Reserve(string root)
    {
        DirectoryInfo[] runs = Runs(root);
        int remaining = runs.Length;
        long retainedBytes = runs.Sum(RunBytes);
        // Routine launches should evict empty/healthy captures before older fault evidence.
        foreach (DirectoryInfo run in runs.OrderBy(HasDump).ThenBy(d => d.LastWriteTimeUtc))
        {
            if (HasRecentDump(run, DateTimeOffset.UtcNow - DiagnosticRetentionPolicy.UploadWindow)) { continue; }
            if (HasDump(run) && remaining < 16 && retainedBytes <= 384L * 1024 * 1024 &&
                run.LastWriteTimeUtc >= DateTime.UtcNow.AddDays(-7)) { continue; }
            long bytes = RunBytes(run);
            if (TryRemove(run)) { remaining--; retainedBytes -= bytes; }
        }
        // Reserve 128 MiB for the new helper plus up to 3 x 32 MiB dumps.
        // Refuse new capture at capacity rather than silently remove the six-hour evidence window.
        return remaining < 16 && retainedBytes <= 384L * 1024 * 1024;
    }

    private static long RunBytes(DirectoryInfo run)
    {
        IncidentStorage.ValidateDirectory(run.FullName);
        long bytes = run.EnumerateFiles().Sum(f => f.Length);
        string dumps = Path.Combine(run.FullName, "private-dumps");
        IncidentStorage.ValidateDirectory(dumps);
        if (Directory.Exists(dumps)) { bytes += new DirectoryInfo(dumps).EnumerateFiles().Sum(f => f.Length); }
        using FileStream? lease = TryLockRun(run.FullName);
        return lease is null ? Math.Max(bytes, 128L * 1024 * 1024) : bytes;
    }

    private static bool HasRecentDump(DirectoryInfo run, DateTimeOffset cutoff)
    {
        string path = Path.Combine(run.FullName, "private-dumps");
        IncidentStorage.ValidateDirectory(path);
        if (!Directory.Exists(path)) { return false; }
        foreach (FileInfo file in new DirectoryInfo(path).EnumerateFiles("*.dmp"))
        {
            try
            {
                DiagnosticCaptureRecord? record = IncidentStorage.Read(file.FullName + ".json", IncidentJsonContext.Default.DiagnosticCaptureRecord);
                if (record is not null ? record.RecordedAt >= cutoff : file.LastWriteTimeUtc >= cutoff.UtcDateTime) { return true; }
            }
            catch (Exception error) when (IncidentStorage.IsStorageFailure(error)) { return true; }
        }
        return false;
    }

    private static bool HasDump(DirectoryInfo run)
    {
        try
        {
            string path = Path.Combine(run.FullName, "private-dumps");
            IncidentStorage.ValidateDirectory(path);
            return Directory.Exists(path) && Directory.EnumerateFiles(path, "*.dmp").Any();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return true; }
    }

    private static DirectoryInfo[] Runs(string root) => new DirectoryInfo(root).EnumerateDirectories()
        .Where(d => Guid.TryParseExact(d.Name, "N", out _)).Take(128).ToArray();

    private static bool IsDumpFile(string name)
    {
        if (name.EndsWith(".json", StringComparison.Ordinal)) { name = name[..^5]; }
        return Enumerable.Range(1, 3).Any(i => _triggers
            .Any(kind => name == $"{i}-{kind}.dmp"));
    }

    private static bool IsOwnedRootFile(string name)
    {
        if (_rootFiles.Contains(name)) { return true; }
        // Atomic incident writes may leave a staging file when the parent crashes.
        foreach (string prefix in new[] { "state.json.", "flight-record.json.", "capture-status.json." })
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".tmp", StringComparison.Ordinal) &&
                Guid.TryParseExact(name[prefix.Length..^4], "N", out _)) { return true; }
        }
        return false;
    }

    private static bool TryRemove(DirectoryInfo run)
    {
        try
        {
            IncidentStorage.ValidateDirectory(run.FullName);
            using (FileStream lease = new(Path.Combine(run.FullName, "capture.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None))
            {
                FileInfo[] roots = run.EnumerateFiles().ToArray();
                DirectoryInfo[] directories = run.EnumerateDirectories().ToArray();
                if (roots.Any(f => !IsOwnedRootFile(f.Name) || (f.Attributes & FileAttributes.ReparsePoint) != 0) ||
                    directories.Any(d => d.Name != "private-dumps" || (d.Attributes & FileAttributes.ReparsePoint) != 0)) { return false; }
                DirectoryInfo? dumps = directories.SingleOrDefault();
                FileInfo[] files = dumps?.EnumerateFiles().ToArray() ?? [];
                if (files.Any(f => !IsDumpFile(f.Name) || (f.Attributes & FileAttributes.ReparsePoint) != 0) ||
                    dumps?.EnumerateDirectories().Any() == true) { return false; }
                foreach (FileInfo file in files) { file.Delete(); }
                dumps?.Delete();
                foreach (FileInfo file in roots.Where(f => f.Name != "capture.lock")) { file.Delete(); }
            }
            File.Delete(Path.Combine(run.FullName, "capture.lock"));
            run.Delete();
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    internal static async Task<string?> ExportLatestAsync(string root, CancellationToken token)
    {
        using FileStream store = Lock(root);
        foreach (DirectoryInfo run in Runs(root).OrderByDescending(d => d.LastWriteTimeUtc))
        {
            string? result = await ExportRunAsync(root, run, token).ConfigureAwait(false);
            if (result is not null) { return result; }
        }

        return null;
    }

    private static async Task<string?> ExportRunAsync(string root, DirectoryInfo run, CancellationToken token)
    {
        IncidentStorage.ValidateDirectory(run.FullName);
        await using FileStream? lease = TryLockRun(run.FullName);
        if (lease is null) { return null; }
        {
            string dumps = Path.Combine(run.FullName, "private-dumps");
            if (!Directory.Exists(dumps)) { return null; }
            IncidentStorage.ValidateDirectory(dumps);
            FileInfo[] files = new DirectoryInfo(dumps).EnumerateFiles().Where(f => IsDumpFile(f.Name)).ToArray();
            if (!files.Any(f => f.Extension == ".dmp")) { return null; }
            if (files.Length > 12 || files.Any(f => f.Length > 32 * 1024 * 1024 || (f.Attributes & FileAttributes.ReparsePoint) != 0))
            { throw new IOException("Capture export limits exceeded."); }
            List<FileInfo> completed = [];
            foreach (FileInfo dump in files.Where(f => f.Extension == ".dmp"))
            {
                DiagnosticCaptureRecord? record = IncidentStorage.Read(dump.FullName + ".json", IncidentJsonContext.Default.DiagnosticCaptureRecord);
                if (record is not { Status: "saved" } || record.FileName != dump.Name || record.Bytes != dump.Length) { continue; }
                completed.Add(dump);
                completed.Add(new(dump.FullName + ".json"));
            }
            // A killed/timed-out writer may leave a partial .dmp. Never present it as a completed export.
            if (completed.Count == 0) { return null; }
            string exports = Path.Combine(root, "exports");
            IncidentStorage.ValidateDirectory(exports);
            Directory.CreateDirectory(exports);
            string final = Path.Combine(exports, run.Name + ".zip");
            string temporary = final + ".tmp";
            if (File.Exists(temporary) && (File.GetAttributes(temporary) & FileAttributes.ReparsePoint) != 0)
            { throw new IOException("Redirected export staging file."); }
            try
            {
                using (FileStream output = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                using (ZipArchive zip = new(output, ZipArchiveMode.Create))
                {
                    FileInfo flight = new(Path.Combine(run.FullName, "flight-record.json"));
                    FileInfo coverage = new(Path.Combine(run.FullName, "capture-status.json"));
                    IEnumerable<FileInfo> selected = completed.Append(new FileInfo(Path.Combine(run.FullName, "state.json")));
                    if (flight.Exists) { selected = selected.Append(flight); }
                    if (coverage.Exists) { selected = selected.Append(coverage); }
                    foreach (FileInfo file in selected)
                    {
                        if (!file.Exists || file.Length > 32 * 1024 * 1024 || (file.Attributes & FileAttributes.ReparsePoint) != 0)
                        { throw new IOException("Invalid capture export file."); }
                        await using Stream entry = await zip.CreateEntry(file.Name, CompressionLevel.Fastest).OpenAsync(token).ConfigureAwait(false);
                        await using FileStream source = file.OpenRead();
                        await source.CopyToAsync(entry, token).ConfigureAwait(false);
                    }
                }
                File.Move(temporary, final, overwrite: true);
                foreach (FileInfo old in new DirectoryInfo(exports).EnumerateFiles("*.zip")
                    .OrderByDescending(f => f.LastWriteTimeUtc).Skip(2))
                { if ((old.Attributes & FileAttributes.ReparsePoint) == 0) { old.Delete(); } }
                return final;
            }
            finally { IncidentStorage.TryDelete(temporary); }
        }
    }

    private static FileStream? TryLockRun(string directory)
    {
        try { return new(Path.Combine(directory, "capture.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return null; }
    }
}
