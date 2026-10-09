using System.Diagnostics;
using System.Security.Cryptography;
using VRPhoneScreenOverlay.Maintenance;
using VRPhoneScreenOverlay.Network;
using VRPhoneScreenOverlay.Update;

namespace VRPhoneScreenOverlay.Tests.Unit;

// Explicit opt-in: isolated installation, signed public manifest, direct HTTPS only.
// --smoke-test never starts phone, VR, diagnostics or other external application services.
public sealed class CloudLiveUpdateTests
{
    [CloudProbeFact]
    public async Task Signed115ReleaseDownloadsReplacesIsolatedCopyAndStartsNewExecutable()
    {
        string root = Path.GetFullPath(Environment.GetEnvironmentVariable("VRPSO_LIVE_UPDATE_ROOT")!);
        string repository = FindRepository();
        string allowed = Path.Combine(repository, "artifacts") + Path.DirectorySeparatorChar;
        Assert.True(root.StartsWith(allowed, StringComparison.OrdinalIgnoreCase), "Probe must stay inside repository artifacts");
        Assert.False(Directory.Exists(root), "Use a fresh isolated probe directory");
        string previous = Environment.GetEnvironmentVariable("VRPSO_LIVE_PREVIOUS_DIRECTORY") ?? Path.Combine(repository, "release", "VRPhoneScreenOverlay-previous");
        previous = Path.GetFullPath(previous);
        Assert.True(previous.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || previous == Path.Combine(repository, "release", "VRPhoneScreenOverlay-previous"), "Baseline must be a repository artifact or previous installation");
        Assert.True(File.Exists(Path.Combine(previous, "VRPhoneScreenOverlay.exe")));
        string install = Path.Combine(root, "installation");
        Directory.CreateDirectory(install);
        foreach (string file in Directory.EnumerateFiles(previous, "*", SearchOption.AllDirectories))
        {
            Assert.False((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0);
            string destination = Path.Combine(install, Path.GetRelativePath(previous, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
        string oldVersion = FileVersionInfo.GetVersionInfo(Path.Combine(install, "app", "VRPhoneScreenOverlay.dll")).ProductVersion!.Split('+')[0];
        Uri manifest = new(Environment.GetEnvironmentVariable("VRPSO_LIVE_UPDATE_MANIFEST")!);
        Assert.Equal("https", manifest.Scheme);
        using HttpUpdateService update = new(new(manifest, "beta", 350L * 1024 * 1024, TimeSpan.FromSeconds(30)));
        using CancellationTokenSource deadline = new(TimeSpan.FromMinutes(8));
        UpdateCheckResult check = await update.CheckAsync(oldVersion, deadline.Token);
        Assert.True(check.UpdateAvailable, "Published version must be newer than the isolated previous installation");
        UpdateRelease release = check.Release!;
        Assert.True(VRPhoneScreenOverlay.Protocols.UpdateDownloadAddressPolicy.IsAllowed(release.PackageUri));
        using AppHttpClient download = new(new NetworkClientOptions("update-client", TimeSpan.FromSeconds(30)) { UseSystemProxy = false });
        PreparedUpdate prepared = await UpdatePackageStager.DownloadAndStageAsync(download, release, 350L * 1024 * 1024,
            TimeSpan.FromSeconds(30), null, deadline.Token, new(Path.Combine(root, "transfer"), install));
        Assert.StartsWith(root + Path.DirectorySeparatorChar, prepared.StageDirectory, StringComparison.OrdinalIgnoreCase);
        int launchCount = 0;
        bool StartAndCheck()
        {
            ProcessStartInfo start = new(Path.Combine(install, "VRPhoneScreenOverlay.exe"), "--smoke-test")
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = install };
            using Process process = Process.Start(start)!;
            launchCount++;
            if (!process.WaitForExit(30000)) { process.Kill(); return false; }
            return process.ExitCode == 0;
        }
        int result = UpdateDirectoryTransaction.Apply(install, prepared.StageDirectory, prepared.TransactionToken,
            StartAndCheck, () => { }, () => { });
        Assert.Equal(0, result);
        Assert.Equal(1, launchCount);
        string installedVersion = FileVersionInfo.GetVersionInfo(Path.Combine(install, "app", "VRPhoneScreenOverlay.dll")).ProductVersion!.Split('+')[0];
        Assert.Equal(release.Version, installedVersion);
        await using FileStream expectedFile = File.OpenRead(Path.Combine(repository, "release", "VRPhoneScreenOverlay", "app", "VRPhoneScreenOverlay.dll"));
        string expected = Convert.ToHexString(await SHA256.HashDataAsync(expectedFile, deadline.Token));
        await using FileStream installed = File.OpenRead(Path.Combine(install, "app", "VRPhoneScreenOverlay.dll"));
        Assert.Equal(expected, Convert.ToHexString(await SHA256.HashDataAsync(installed, deadline.Token)));
        await File.WriteAllTextAsync(Path.Combine(root, "verified.txt"), $"{oldVersion} -> {installedVersion}\nSigned 115 download, package hash, portable staging, replacement and new executable smoke start passed.\n", deadline.Token);
    }

    private static string FindRepository()
    {
        DirectoryInfo? path = new(AppContext.BaseDirectory);
        while (path is not null && !File.Exists(Path.Combine(path.FullName, "VRPhoneScreenOverlay.slnx"))) { path = path.Parent; }
        return path?.FullName ?? throw new InvalidOperationException("Repository not found");
    }
}

public sealed class CloudProbeFactAttribute : FactAttribute
{
    public CloudProbeFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VRPSO_LIVE_UPDATE_ROOT")) ||
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VRPSO_LIVE_UPDATE_MANIFEST")))
        { Skip = "Requires explicit live manifest and a fresh repository artifact directory"; }
    }
}
