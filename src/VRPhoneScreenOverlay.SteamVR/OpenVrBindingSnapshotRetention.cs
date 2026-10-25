using System.Text.Json;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.SteamVR;

internal static class OpenVrBindingSnapshotRetention
{
    internal const int MaximumDirectoryScan = 128;
    internal const int MaximumFilesPerDirectory = 32;
    internal const long MaximumDirectoryBytes = 32L * 1024 * 1024;
    private const long _inspectionBudget = 64L * 1024 * 1024;

    internal static BindingSnapshotRetentionDiagnostic Skip(string reason, Exception? error = null)
        => new(DateTimeOffset.UtcNow, reason, 0, 0, 0, 0, 0, 0, false, error?.GetType().FullName);

    internal static BindingSnapshotRetentionDiagnostic Prune(string root, string current, IReadOnlyList<string>? references)
    {
        int scanned = 0, deleted = 0, skipped = 0;
        long inspected = 0;
        bool truncated = false;
        HashSet<string> protectedDirectories = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!TryProtect(root, references, protectedDirectories, out string? reason)) { return Skip(reason!); }
            string canonicalRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            string canonicalCurrent = Path.GetFullPath(current);
            if (!string.Equals(Path.GetDirectoryName(canonicalCurrent), canonicalRoot, StringComparison.OrdinalIgnoreCase)) { return Skip("current-outside-root"); }
            protectedDirectories.Add(canonicalCurrent);
            string[] directories = Directory.EnumerateDirectories(canonicalRoot).Take(MaximumDirectoryScan + 1).ToArray();
            truncated = directories.Length > MaximumDirectoryScan;
            foreach (string directory in directories.Take(MaximumDirectoryScan).OrderByDescending(Directory.GetLastWriteTimeUtc).ThenBy(path => path, StringComparer.Ordinal))
            {
                scanned++;
                if (protectedDirectories.Contains(Path.GetFullPath(directory))) { continue; }
                if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) { skipped++; continue; }
                if (!TryReadOwnedFiles(directory, ref inspected, out string[] files, out bool budgetExceeded))
                { skipped++; if (budgetExceeded) { truncated = true; break; } continue; }
                foreach (string file in files) { File.Delete(file); }
                Directory.Delete(directory, recursive: false);
                deleted++;
                if (inspected >= _inspectionBudget) { truncated = true; break; }
            }
            return new(DateTimeOffset.UtcNow, truncated ? "bounded-pass" : skipped > 0 ? "completed-with-skips" : "completed",
                protectedDirectories.Count, scanned, 0, deleted, skipped, inspected, truncated);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException or NotSupportedException)
        {
            return new(DateTimeOffset.UtcNow, "cleanup-incomplete", protectedDirectories.Count, scanned, 0, deleted,
                skipped, inspected, truncated, error.GetType().FullName);
        }
    }

    internal static bool TryProtect(string root, IReadOnlyList<string>? references, HashSet<string> protectedDirectories, out string? reason)
    {
        reason = null;
        if (references is null || references.Count > 128) { reason = "references-unavailable"; return false; }
        string canonicalRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        if (Directory.GetParent(canonicalRoot) is null) { reason = "unsafe-root"; return false; }
        OpenVrBindingDefaults.AssertNoReparsePoints(canonicalRoot);
        foreach (string reference in references)
        {
            if (!Uri.TryCreate(reference, UriKind.Absolute, out Uri? uri)) { reason = "unresolved-reference"; return false; }
            if (!uri.IsFile) { continue; }
            if (uri.IsUnc) { reason = "unresolved-file-alias"; return false; }
            string path = Path.GetFullPath(uri.LocalPath);
            if (!path.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { continue; }
            OpenVrBindingDefaults.AssertNoReparsePoints(path);
            string relative = Path.GetRelativePath(canonicalRoot, path);
            int separator = relative.IndexOf(Path.DirectorySeparatorChar);
            if (separator < 1) { reason = "unresolved-owned-reference"; return false; }
            protectedDirectories.Add(Path.Combine(canonicalRoot, relative[..separator]));
            if (!File.Exists(path)) { reason = "referenced-file-missing"; return false; }
        }
        return true;
    }

    private static bool TryReadOwnedFiles(string directory, ref long inspected, out string[] files, out bool budgetExceeded)
    {
        files = []; long bytes = 0; budgetExceeded = false;
        OpenVrBindingDefaults.AssertNoReparsePoints(directory);
        string[] entries = Directory.EnumerateFileSystemEntries(directory).Take(MaximumFilesPerDirectory + 1).ToArray();
        if (entries.Length > MaximumFilesPerDirectory) { return false; }
        foreach (string file in entries)
        {
            if ((File.GetAttributes(file) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 ||
                !string.Equals(Path.GetExtension(file), ".json", StringComparison.OrdinalIgnoreCase) ||
                !OpenVrGenericBinding.ValidType(Path.GetFileNameWithoutExtension(file))) { return false; }
            long length = new FileInfo(file).Length;
            if (length > 1_048_576) { return false; }
            bytes += length;
        }
        if (bytes > MaximumDirectoryBytes) { return false; }
        if (inspected + bytes > _inspectionBudget) { budgetExceeded = true; return false; }
        foreach (string file in entries)
        {
            using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > 1_048_576) { return false; }
            if (inspected + stream.Length > _inspectionBudget) { budgetExceeded = true; return false; }
            byte[] contents = new byte[checked((int)stream.Length)];
            stream.ReadExactly(contents);
            inspected += contents.Length;
            try
            {
                using JsonDocument document = JsonDocument.Parse(contents);
                JsonElement json = document.RootElement;
                if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("app_key", out JsonElement app) ||
                    app.ValueKind != JsonValueKind.String || app.GetString() != OpenVrInputManifest.ApplicationKey ||
                    !json.TryGetProperty("controller_type", out JsonElement type) || type.ValueKind != JsonValueKind.String ||
                    type.GetString() != Path.GetFileNameWithoutExtension(file)) { return false; }
            }
            catch (JsonException) { return false; }
        }
        files = entries;
        return true;
    }
}
