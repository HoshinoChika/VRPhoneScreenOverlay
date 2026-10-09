using System.Text.Json;
using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.Network;
using VRPhoneScreenOverlay.Update;
using TestDirectory = VRPhoneScreenOverlay.Tests.Unit.CompiledBindingDefaultsTests.TestDirectory;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class UpdateInstallLayoutTests
{
    private static readonly string[] _runtimeFiles = ["VRPhoneScreenOverlay.dll", "VRPhoneScreenOverlay.runtimeconfig.json", "VRPhoneScreenOverlay.deps.json",
        AppIdentity.MaintenanceExecutableName, "VRPhoneScreenOverlay.SteamVR.BindingTool.exe", "action_manifest.json"];

    [Fact]
    public void InstallationRootIsResolvedOnlyFromTheFullLayout()
    {
        string root = Path.GetFullPath("installation");
        Assert.Equal(root, UpdateInstallLayout.ResolveInstallDirectory(Path.Combine(root, "app"), Path.Combine(root, AppIdentity.ExecutableName)));
        Assert.Throws<UpdateException>(() => UpdateInstallLayout.ResolveInstallDirectory(root, Path.Combine(root, AppIdentity.ExecutableName)));
        Assert.Throws<UpdateException>(() => UpdateInstallLayout.ResolveInstallDirectory(root, null));
    }

    [Fact]
    public void FullPackageUnwrapsWithoutChangingRuntimeOrBindingBytes()
    {
        using TestDirectory directory = new();
        string package = Path.Combine(directory.Path, AppIdentity.LocalDataFolderName);
        CreateFullInstallation(package);
        UpdateInstallLayout.PrepareFullPackageStage(directory.Path);
        UpdatePackageStager.ValidateInstallDirectory(directory.Path);
        Assert.Equal([AppIdentity.ExecutableName], Directory.GetFiles(directory.Path).Select(Path.GetFileName));
        Assert.Equal("host", File.ReadAllText(Path.Combine(directory.Path, AppIdentity.ExecutableName)));
        Assert.Equal("binding", File.ReadAllText(Path.Combine(directory.Path, "app", "bindings", "test.json")));
        Assert.False(Directory.Exists(package));
    }

    [Theory]
    [InlineData("extra-root-file")]
    [InlineData("missing-runtime")]
    [InlineData("wrong-identity")]
    [InlineData("flat-layout")]
    public void InvalidFullLayoutsAreRejectedBeforeMovingAnyInstalledContent(string failure)
    {
        using TestDirectory directory = new();
        string package = Path.Combine(directory.Path, AppIdentity.LocalDataFolderName);
        CreateFullInstallation(package);
        if (failure == "extra-root-file") { File.WriteAllText(Path.Combine(directory.Path, "extra.txt"), "unexpected"); }
        if (failure == "missing-runtime") { File.Delete(Path.Combine(package, "app", AppIdentity.MaintenanceExecutableName)); }
        if (failure == "wrong-identity") { File.WriteAllText(Path.Combine(package, "app", "manifest.vrmanifest"), """{"applications":[]} """); }
        if (failure == "flat-layout") { Directory.Move(Path.Combine(package, "app"), Path.Combine(directory.Path, "app")); }
        Assert.Throws<InvalidDataException>(() => UpdateInstallLayout.PrepareFullPackageStage(directory.Path));
        Assert.True(File.Exists(Path.Combine(package, AppIdentity.ExecutableName)));
        Assert.False(File.Exists(Path.Combine(directory.Path, AppIdentity.ExecutableName)));
    }

    [Fact]
    public void VerifiedStagingPreservesClientConfigurationAndRejectsMaintenanceFields()
    {
        using TestDirectory directory = new();
        string install = Path.Combine(directory.Path, "install");
        string stage = Path.Combine(directory.Path, "stage");
        CreateFullInstallation(install);
        CreateFullInstallation(stage);
        string current = Path.Combine(install, "app", "service.config.json");
        File.WriteAllText(current, JsonSerializer.Serialize(new { SchemaVersion = 1, Client = TestServiceConfiguration.Value }));
        UpdateInstallLayout.PreserveClientConfiguration(install, stage);
        Assert.Equal(TestServiceConfiguration.Value, ClientServiceConfiguration.Load(Path.Combine(stage, "app", "service.config.json")).Configuration);
        File.WriteAllText(current, """{"SchemaVersion":1,"Client":{},"Deployment":{"Password":"synthetic"}}""");
        Assert.Equal("UPDATE_SERVICE_CONFIG_INVALID", Assert.Throws<UpdateException>(() =>
            UpdateInstallLayout.PreserveClientConfiguration(install, stage)).ReasonCode);
    }

    internal static void CreateFullInstallation(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "app", "bindings"));
        Directory.CreateDirectory(Path.Combine(root, "app", "resources", "android-platform-tools"));
        Directory.CreateDirectory(Path.Combine(root, "app", "resources", "scrcpy"));
        File.WriteAllText(Path.Combine(root, AppIdentity.ExecutableName), "host");
        foreach (string name in _runtimeFiles) { File.WriteAllText(Path.Combine(root, "app", name), "synthetic"); }
        File.WriteAllText(Path.Combine(root, "app", "bindings", "test.json"), "binding");
        File.WriteAllText(Path.Combine(root, "app", "resources", "android-platform-tools", "adb.exe"), "adb");
        File.WriteAllText(Path.Combine(root, "app", "resources", "scrcpy", "scrcpy-server-v4.1"), "server");
        File.WriteAllText(Path.Combine(root, "app", "manifest.vrmanifest"), """
            {"applications":[{"app_key":"local.spacedraglite.desktop.v1","binary_path_windows":"../VRPhoneScreenOverlay.exe"}]}
            """);
    }
}
