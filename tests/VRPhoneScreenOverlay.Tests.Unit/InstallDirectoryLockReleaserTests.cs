using System.Diagnostics;
using VRPhoneScreenOverlay.Maintenance;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class InstallDirectoryLockReleaserTests
{
    [Fact]
    public void MissingBundledAdbDoesNotStartACommand()
    {
        using TemporaryDirectory temporary = new();
        bool invoked = false;

        InstallDirectoryLockReleaser.Release(
            temporary.Path,
            _ =>
            {
                invoked = true;
                return 0;
            },
            _ => throw new InvalidOperationException("Missing ADB must not inspect processes."));

        Assert.False(invoked);
    }

    [Fact]
    public void BundledAdbIsStoppedBeforeTheInstallDirectoryIsMoved()
    {
        using TemporaryDirectory temporary = new();
        string adbDirectory = Path.Combine(
            Path.Combine(temporary.Path, "app"),
            "resources",
            "android-platform-tools");
        Directory.CreateDirectory(adbDirectory);
        string adbPath = Path.Combine(adbDirectory, "adb.exe");
        File.WriteAllBytes(adbPath, []);
        ProcessStartInfo? captured = null;
        string? sweptPath = null;

        InstallDirectoryLockReleaser.Release(
            temporary.Path,
            startInfo =>
            {
                captured = startInfo;
                return 0;
            },
            path => sweptPath = path);

        Assert.NotNull(captured);
        Assert.Equal(adbPath, captured.FileName);
        Assert.Equal(adbDirectory, captured.WorkingDirectory);
        Assert.False(captured.UseShellExecute);
        Assert.True(captured.CreateNoWindow);
        Assert.Equal(["kill-server"], captured.ArgumentList);
        Assert.Equal(adbPath, sweptPath);
    }

    [Fact]
    public void FailedBundledAdbShutdownStopsTheUpdate()
    {
        using TemporaryDirectory temporary = new();
        string adbDirectory = Path.Combine(
            temporary.Path, "app",
            "resources",
            "android-platform-tools");
        Directory.CreateDirectory(adbDirectory);
        File.WriteAllBytes(Path.Combine(adbDirectory, "adb.exe"), []);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            InstallDirectoryLockReleaser.Release(temporary.Path, _ => 1,
                _ => throw new InvalidOperationException("Failed shutdown must not sweep processes.")));

        Assert.Contains("exit code 1", exception.Message, StringComparison.Ordinal);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"VRPhoneScreenOverlay-maintenance-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
