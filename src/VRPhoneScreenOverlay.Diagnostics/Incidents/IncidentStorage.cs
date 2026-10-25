using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Diagnostics;

internal static class IncidentStorage
{
    internal const int MaximumBytes = 128 * 1024;
    internal static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppIdentity.LocalDataFolderName, "diagnostics", "incidents");

    internal static void ValidateDirectory(string directory)
    {
        for (string? path = Path.GetFullPath(directory); path is not null; path = Path.GetDirectoryName(path))
        {
            if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            { throw new IOException("Incident directory is redirected."); }
        }
    }

    internal static byte[] Serialize<T>(T value, JsonTypeInfo<T> type)
    {
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(value, type);
        if (data.Length > MaximumBytes) { throw new IOException("Incident record exceeds capacity."); }
        return data;
    }

    internal static async ValueTask WriteAsync<T>(string path, T value, JsonTypeInfo<T> type, CancellationToken token)
    {
        byte[] data = Serialize(value, type);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(data, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
                // FlushAsync drains managed buffers; this separate OS durability barrier
                // has no asynchronous equivalent and runs only at lifecycle boundaries.
                RandomAccess.FlushToDisk(stream.SafeFileHandle);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { TryDelete(temporary); }
    }

    // Fatal handlers cannot await: write a separate bounded record before unwinding.
    internal static void WriteFatal(string path, IncidentFailure failure)
    {
        byte[] data = Serialize(failure, IncidentJsonContext.Default.IncidentFailure);
        using FileStream stream = new(path, FileMode.Create, FileAccess.Write,
            FileShare.Read, 4096, FileOptions.WriteThrough);
        stream.Write(data);
        stream.Flush(flushToDisk: true);
    }

    internal static T? Read<T>(string path, JsonTypeInfo<T> type)
    {
        FileInfo file = new(path);
        if (!file.Exists) { return default; }
        if (file.Length > MaximumBytes || (file.Attributes & FileAttributes.ReparsePoint) != 0)
        { throw new IOException("Incident record is not a bounded regular file."); }
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return JsonSerializer.Deserialize(stream, type);
    }

    internal static bool IsStorageFailure(Exception error) =>
        error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException;

    internal static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception error) when (IsStorageFailure(error)) { }
    }

    internal static void Prune(string root)
    {
        foreach (DirectoryInfo directory in new DirectoryInfo(root).EnumerateDirectories().Take(128))
        {
            if (!Guid.TryParseExact(directory.Name, "N", out _)) { continue; }
            try
            {
                ValidateDirectory(directory.FullName);
                using FileStream owner = new(Path.Combine(directory.FullName, "owner.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                using FileStream monitor = new(Path.Combine(directory.FullName, "monitor.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                TryDelete(Path.Combine(directory.FullName, "monitor.exe"));
            }
            catch (Exception error) when (IsStorageFailure(error)) { }
        }
        // Active application and monitor owners hold leases. Never evict their evidence.
        foreach (DirectoryInfo directory in new DirectoryInfo(root).EnumerateDirectories()
            .Where(d => Guid.TryParseExact(d.Name, "N", out _))
            .Where(d => (d.Attributes & FileAttributes.ReparsePoint) == 0)
            .Where(d => d.EnumerateFiles().Where(f => f.Extension is ".json" or ".jsonl")
                .All(f => f.LastWriteTimeUtc < DateTime.UtcNow - DiagnosticRetentionPolicy.LocalWindow)))
        {
            try
            {
                ValidateDirectory(directory.FullName);
                using (FileStream owner = new(Path.Combine(directory.FullName, "owner.lock"),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                using (FileStream monitor = new(Path.Combine(directory.FullName, "monitor.lock"),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    foreach (FileInfo file in directory.EnumerateFiles())
                    {
                        if (file.Name.EndsWith(".json", StringComparison.Ordinal) || file.Name.EndsWith(".tmp", StringComparison.Ordinal) || file.Name.EndsWith(".jsonl", StringComparison.Ordinal))
                        { file.Delete(); }
                    }
                }
                File.Delete(Path.Combine(directory.FullName, "owner.lock"));
                File.Delete(Path.Combine(directory.FullName, "monitor.lock"));
                Directory.Delete(directory.FullName, recursive: false);
            }
            catch (Exception error) when (IsStorageFailure(error)) { }
        }
    }
}
