using System.Text.Json;
using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.Network;

namespace VRPhoneScreenOverlay.Update;

internal static class UpdateInstallLayout
{
    private static readonly string[] _requiredRuntimeFiles = ["VRPhoneScreenOverlay.dll", "VRPhoneScreenOverlay.runtimeconfig.json",
        "VRPhoneScreenOverlay.deps.json", AppIdentity.MaintenanceExecutableName,
        "VRPhoneScreenOverlay.SteamVR.BindingTool.exe", "manifest.vrmanifest", "action_manifest.json",
        "resources/android-platform-tools/adb.exe", "resources/scrcpy/scrcpy-server-v4.1"];
    public static string ResolveInstallDirectory(string runtimeDirectory, string? processPath)
    {
        string runtime = Path.GetFullPath(runtimeDirectory).TrimEnd(Path.DirectorySeparatorChar);
        string? executableDirectory = Path.GetDirectoryName(processPath);
        return executableDirectory is not null &&
            string.Equals(Path.GetFileName(processPath), AppIdentity.ExecutableName,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(runtime, Path.Combine(executableDirectory, "app"),
                StringComparison.OrdinalIgnoreCase)
                ? executableDirectory
                : throw new UpdateException(UpdateReasonCodes.InstallPathInvalid, "软件安装目录无效");
    }

    public static string MaintenancePath(string installDirectory)
    {
        return Path.Combine(installDirectory, "app", AppIdentity.MaintenanceExecutableName);
    }

    public static void PreserveClientConfiguration(string installDirectory, string stageDirectory)
    {
        string currentRuntime = Path.Combine(installDirectory, "app");
        string current = Path.Combine(currentRuntime, "service.config.json");
        if (!File.Exists(current)) { return; }
        ClientServiceConfigurationResult result = ClientServiceConfiguration.Load(current);
        if (result.ReasonCode != "SERVICE_CONFIG_LOADED")
        { throw new UpdateException("UPDATE_SERVICE_CONFIG_INVALID", "当前服务配置无效，请修复配置后重试更新"); }
        string stageRuntime = Path.Combine(stageDirectory, "app");
        // Preserve a validated client projection, never copy arbitrary maintenance fields.
        File.WriteAllText(Path.Combine(stageRuntime, "service.config.json"),
            JsonSerializer.Serialize(new { SchemaVersion = 1, Client = result.Configuration }));
    }

    // Only the hash-verified full-install ZIP is accepted. No legacy layout conversion.
    public static void PrepareFullPackageStage(string stageDirectory)
    {
        string packageRoot = Path.Combine(stageDirectory, AppIdentity.LocalDataFolderName);
        string[] entries = Directory.GetFileSystemEntries(stageDirectory);
        if (entries.Length != 1 || !Directory.Exists(packageRoot) ||
            (File.GetAttributes(packageRoot) & FileAttributes.ReparsePoint) != 0)
        { throw new InvalidDataException("The full package must contain one application directory."); }
        ValidateFullInstallation(packageRoot);
        foreach (string entry in Directory.GetFileSystemEntries(packageRoot))
        {
            string destination = Path.Combine(stageDirectory, Path.GetFileName(entry));
            if (Directory.Exists(entry)) { Directory.Move(entry, destination); }
            else { File.Move(entry, destination); }
        }
        Directory.Delete(packageRoot);
    }

    internal static void ValidateFullInstallation(string directory)
    {
        string[] rootEntries = Directory.GetFileSystemEntries(directory);
        if (rootEntries.Length != 2 || !File.Exists(Path.Combine(directory, AppIdentity.ExecutableName)) ||
            !Directory.Exists(Path.Combine(directory, "app")))
        { throw new InvalidDataException("The installation must contain only the executable and app directory."); }
        foreach (string relative in _requiredRuntimeFiles)
        {
            string path = Path.Combine(directory, "app", relative);
            if (!File.Exists(path) || new FileInfo(path).Length == 0 || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            { throw new InvalidDataException("The installation is missing a required runtime file."); }
        }
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "app", "manifest.vrmanifest")));
        JsonElement[] applications = manifest.RootElement.GetProperty("applications").EnumerateArray()
            .Where(application => application.GetProperty("app_key").GetString() == AppIdentity.SteamVrApplicationKey).ToArray();
        if (applications.Length != 1 || applications[0].GetProperty("binary_path_windows").GetString() != "../" + AppIdentity.ExecutableName)
        { throw new InvalidDataException("The full package application identity or launch path is invalid."); }
    }
}
