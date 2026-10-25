using System.Text.Json;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Diagnostics;

internal sealed record ComponentRecoveryPart(string Location, string Status, ComponentRecoveryEvidence? Evidence);
internal sealed record ComponentRecoveryCollection(int SchemaVersion, int MaximumBytesPerRecord, ComponentRecoveryPart[] Records);

internal static class ComponentRecoveryCollector
{
    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    internal static ComponentRecoveryCollection Collect(string? root = null)
    {
        root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppIdentity.LocalDataFolderName, "diagnostics", "component-recovery");
        List<ComponentRecoveryPart> records = [];
        foreach (string location in new[] { "installation", "runtime" })
        {
            try
            {
                IncidentStorage.ValidateDirectory(root);
                string path = Path.Combine(root, location + ".json");
                if (!File.Exists(path)) { records.Add(new(location, "not-collected", null)); continue; }
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) { throw new IOException("Redirected evidence."); }
                using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                if (stream.Length > 16384) { records.Add(new(location, "size-limit", null)); continue; }
                ComponentRecoveryEvidence? evidence = JsonSerializer.Deserialize<ComponentRecoveryEvidence>(stream, _json);
                if (evidence is null || evidence.SchemaVersion != 1 || evidence.Location != location || evidence.Files is not { Length: 3 } ||
                    evidence.Files.Any(f => f is null) ||
                    evidence.Files.Select(f => f.File).Distinct(StringComparer.Ordinal).Count() != 3 ||
                    evidence.Files.Any(f => f.File is not ("adb.exe" or "AdbWinApi.dll" or "AdbWinUsbApi.dll")))
                { records.Add(new(location, "invalid", null)); continue; }
                records.Add(new(location, "collected", evidence));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            { records.Add(new(location, "collection-failed", null)); }
        }
        return new(1, 16384, records.ToArray());
    }
}
