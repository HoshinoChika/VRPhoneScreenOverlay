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
        foreach (string location in new[] { "installation", "runtime", "binding-tool" })
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
                string[] expected = location == "binding-tool" ?
                    ["VRPhoneScreenOverlay.SteamVR.BindingTool.exe", "VRPhoneScreenOverlay.SteamVR.BindingTool.dll",
                        "VRPhoneScreenOverlay.SteamVR.BindingTool.deps.json", "VRPhoneScreenOverlay.SteamVR.BindingTool.runtimeconfig.json"] :
                    ["adb.exe", "AdbWinApi.dll", "AdbWinUsbApi.dll"];
                bool failedBeforeInventory = location == "binding-tool" && evidence?.Files is { Length: 0 } && evidence.Phase.EndsWith("-failed", StringComparison.Ordinal);
                if (evidence is null || evidence.SchemaVersion != 1 || evidence.Location != location || evidence.Files is null || (!failedBeforeInventory && evidence.Files.Length != expected.Length) ||
                    evidence.Files.Any(f => f is null) ||
                    (!failedBeforeInventory && evidence.Files.Select(f => f.File).Distinct(StringComparer.Ordinal).Count() != expected.Length) ||
                    evidence.Files.Any(f => !expected.Contains(f.File, StringComparer.Ordinal)))
                { records.Add(new(location, "invalid", null)); continue; }
                records.Add(new(location, "collected", evidence));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            { records.Add(new(location, "collection-failed", null)); }
        }
        return new(1, 16384, records.ToArray());
    }
}
