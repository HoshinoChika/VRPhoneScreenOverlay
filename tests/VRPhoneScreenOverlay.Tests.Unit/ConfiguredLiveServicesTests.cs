using System.Diagnostics;
using System.Text.Json;
using VRPhoneScreenOverlay.Diagnostics;
using VRPhoneScreenOverlay.Maintenance;
using VRPhoneScreenOverlay.Network;
using VRPhoneScreenOverlay.Update;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class ConfiguredLiveServicesTests
{
    [ConfiguredLiveServicesFact]
    public async Task ConfiguredSignedDownloadAndSyntheticDiagnosticUploadCompleteEndToEnd()
    {
        string repository = FindRepository();
        string output = Path.GetFullPath(Environment.GetEnvironmentVariable("VRPSO_LIVE_SERVICES_DIRECTORY")!);
        Assert.StartsWith(Path.Combine(repository, "artifacts") + Path.DirectorySeparatorChar, output, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(output), "Use a fresh isolated verification directory.");
        Directory.CreateDirectory(output);
        ClientServiceConfigurationResult loaded = ClientServiceConfiguration.Load(Environment.GetEnvironmentVariable("VRPSO_LIVE_SERVICE_CONFIG")!);
        Assert.Equal("SERVICE_CONFIG_LOADED", loaded.ReasonCode);
        Assert.NotNull(loaded.Configuration.UpdateManifestUri);
        Assert.NotNull(loaded.Configuration.DiagnosticsInitUri);
        using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(10));
        using HttpUpdateService update = new(UpdateServiceOptions.FromConfiguration(loaded.Configuration));
        // A download probe deliberately requests the published package, including the same version.
        UpdateCheckResult check = await update.CheckAsync("0.0.0", deadline.Token);
        Assert.True(check.UpdateAvailable);
        UpdateRelease release = Assert.IsType<UpdateRelease>(check.Release);
        string current = Path.Combine(repository, "release", "VRPhoneScreenOverlay");
        string install = Path.Combine(output, "installation");
        foreach (string source in Directory.EnumerateFiles(current, "*", SearchOption.AllDirectories))
        {
            Assert.False((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0);
            string destination = Path.Combine(install, Path.GetRelativePath(current, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
        }
        using AppHttpClient http = new(new NetworkClientOptions("update-client", TimeSpan.FromSeconds(30)) { UseSystemProxy = false });
        PreparedUpdate prepared = await UpdatePackageStager.DownloadAndStageAsync(http, release, 350L * 1024 * 1024,
            TimeSpan.FromSeconds(30), null, deadline.Token, new(Path.Combine(output, "transfer"), install));
        Assert.Equal(loaded.Configuration, ClientServiceConfiguration.Load(Path.Combine(prepared.StageDirectory, "app", "service.config.json")).Configuration);
        int launches = 0;
        bool StartAndCheck()
        {
            using Process process = Process.Start(new ProcessStartInfo(Path.Combine(install, "VRPhoneScreenOverlay.exe"), "--smoke-test")
            { WorkingDirectory = install, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden })!;
            launches++;
            if (!process.WaitForExit(30000)) { process.Kill(); return false; }
            return process.ExitCode == 0;
        }
        Assert.Equal(0, UpdateDirectoryTransaction.Apply(install, prepared.StageDirectory, prepared.TransactionToken, StartAndCheck, () => { }, () => { }));
        Assert.Equal(1, launches);
        string version = FileVersionInfo.GetVersionInfo(Path.Combine(install, "app", "VRPhoneScreenOverlay.dll")).ProductVersion!.Split('+')[0];
        Assert.Equal(release.Version, version);
        string outgoing = Path.Combine(output, "diagnostics-outgoing");
        using AnonymousDiagnosticsService diagnostics = new(DiagnosticsServiceOptions.FromConfiguration(loaded.Configuration) with
        { FailedBundleDirectory = output, OutgoingBundleDirectory = outgoing });
        DiagnosticUploadContext context = new(version, new Dictionary<string, object?> { ["verification"] = "synthetic-configured-service-test" },
            new Dictionary<string, object?>(), UpdateAndDiagnosticsTests.CreateHardwareSummary(),
            UpdateAndDiagnosticsTests.CreateSessionSummary(), UpdateAndDiagnosticsTests.CreateBindingSummary(),
            new(DiagnosticIssueType.Other, DateTimeOffset.UtcNow, "Synthetic upload verification; no device content or real logs."), null);
        DiagnosticUploadResult result = await diagnostics.BuildAndUploadAsync(context, null, deadline.Token);
        Assert.True(result.Succeeded, result.ReasonCode);
        Assert.NotNull(result.ReceivedAt);
        Assert.Null(result.RetainedBundlePath);
        Assert.Empty(Directory.EnumerateFiles(outgoing));
        await File.WriteAllTextAsync(Path.Combine(output, "verified.json"), JsonSerializer.Serialize(new
        {
            signedManifest = true,
            release.Version,
            release.PackageSize,
            release.PackageSha256,
            downloadedAndHashVerified = true,
            isolatedReplacementAndSmoke = true,
            clientConfigurationPreserved = true,
            diagnosticUploaded = true,
            result.ReasonCode,
            result.ReceivedAt,
            realLogsIncluded = false,
        }), deadline.Token);
    }

    private static string FindRepository()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "VRPhoneScreenOverlay.slnx"))) { directory = directory.Parent; }
        return directory?.FullName ?? throw new InvalidOperationException("Repository not found.");
    }
}

public sealed class ConfiguredLiveServicesFactAttribute : FactAttribute
{
    public ConfiguredLiveServicesFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VRPSO_LIVE_SERVICES_DIRECTORY")) ||
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VRPSO_LIVE_SERVICE_CONFIG")))
        { Skip = "Requires explicit configured services and an isolated repository artifact directory."; }
    }
}
