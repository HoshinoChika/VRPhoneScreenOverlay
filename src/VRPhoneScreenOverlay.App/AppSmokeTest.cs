using System.Text.Json;
using VRPhoneScreenOverlay.Network;

namespace VRPhoneScreenOverlay.App;

internal static class AppSmokeTest
{
    public static int Run()
    {
        try
        {
            string root = AppContext.BaseDirectory;
            ClientServiceConfigurationResult configuration = ClientServiceConfiguration.Load(Path.Combine(root, "service.config.json"));
            if (configuration.ReasonCode == "SERVICE_CONFIG_INVALID") { return 15; }
            string[] requiredFiles =
            [
                "action_manifest.json",
                "manifest.vrmanifest",
                "openvr_api.dll",
                "VRPhoneScreenOverlay.SteamVR.BindingTool.exe",
                "VRPhoneScreenOverlay.Maintenance.exe",
            ];
            foreach (string relativePath in requiredFiles)
            {
                if (!File.Exists(Path.Combine(root, relativePath)))
                {
                    return 10;
                }
            }

            using JsonDocument applicationManifest = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(root, "manifest.vrmanifest")));
            JsonElement application = applicationManifest.RootElement.GetProperty("applications")
                .EnumerateArray().Single(item => item.GetProperty("app_key").GetString() ==
                    "local.spacedraglite.desktop.v1");
            string executable = Path.GetFullPath(Path.Combine(root,
                application.GetProperty("binary_path_windows").GetString()!));
            if (!File.Exists(executable) ||
                !string.Equals(executable, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
            {
                return 14;
            }

            using JsonDocument actionManifest = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(root, "action_manifest.json")));
            if (actionManifest.RootElement.GetProperty("actions").GetArrayLength() == 0 ||
                actionManifest.RootElement.GetProperty("default_bindings").GetArrayLength() == 0)
            {
                return 11;
            }

            return 0;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or
                KeyNotFoundException or InvalidOperationException)
        {
            return 12;
        }
    }
}
