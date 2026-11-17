using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

namespace VRPhoneScreenOverlay.Diagnostics;

public sealed partial class LocalSoftwareFileInspector
{
    private async Task<string> InspectBindingToolAsync(CancellationToken token)
    {
        using Stream? source = Assembly.GetEntryAssembly()?.GetManifestResourceStream("BindingToolRecovery.zip");
        if (source is null) { return "bindingToolIntegrity=unknown;trustedInventoryUnavailable=true"; }
        try
        {
            using ZipArchive archive = new(source, ZipArchiveMode.Read);
            ZipArchiveEntry? manifest = archive.GetEntry("inventory.json");
            if (manifest is null || manifest.Length > 16384) { return "bindingToolIntegrity=unknown;invalidInventory=true"; }
            await using Stream input = await manifest.OpenAsync(token).ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(input, cancellationToken: token).ConfigureAwait(false);
            List<string> results = [];
            foreach (string suffix in new[] { ".exe", ".dll", ".deps.json", ".runtimeconfig.json" })
            {
                string name = "VRPhoneScreenOverlay.SteamVR.BindingTool" + suffix;
                string? hash = document.RootElement.GetProperty("files").GetProperty(name).GetString();
                if (hash is not { Length: 64 }) { return "bindingToolIntegrity=unknown;invalidInventory=true"; }
                results.Add(name + "=" + await InspectFileAsync(Path.Combine(_application, name), suffix.EndsWith(".json", StringComparison.Ordinal), hash, token).ConfigureAwait(false));
            }
            return string.Join(';', results) + ";trustedInventory=embedded_application;scope=helper_four_files";
        }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException)
        { return "bindingToolIntegrity=unknown;invalidInventory=true"; }
    }
}
