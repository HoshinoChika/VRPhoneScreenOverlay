using VRPhoneScreenOverlay.Maintenance;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class UpdateDirectoryTransactionTests
{
    [Fact]
    public void BackupCleanupFailureDoesNotUndoHealthyInstallation()
    {
        using TransactionFiles files = new();
        string readOnly = Path.Combine(files.Install, "read-only-note.txt");
        File.WriteAllText(readOnly, "old note");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        int stopped = 0;
        int restarted = 0;

        int result = UpdateDirectoryTransaction.Apply(
            files.Install, files.Stage, files.Token, () => true, () => stopped++, () => restarted++);

        Assert.Equal(0, result);
        Assert.Equal("new", File.ReadAllText(Path.Combine(files.Install, "component.dll")));
        Assert.Equal(0, stopped);
        Assert.Equal(0, restarted);
        Assert.True(File.Exists(Path.Combine(files.Backup, "read-only-note.txt")));
        Assert.False(Directory.Exists(files.Failed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedHealthOrLaunchRestoresCompleteBaseline(bool throwDuringLaunch)
    {
        using TransactionFiles files = new();
        int stopped = 0;
        int restarted = 0;

        int result = UpdateDirectoryTransaction.Apply(
            files.Install, files.Stage, files.Token,
            () => throwDuringLaunch ? throw new IOException("synthetic launch failure") : false,
            () => stopped++,
            () =>
            {
                Assert.Equal("old", File.ReadAllText(Path.Combine(files.Install, "component.dll")));
                restarted++;
            });

        Assert.Equal(4, result);
        Assert.Equal("old", File.ReadAllText(Path.Combine(files.Install, "component.dll")));
        Assert.Equal(1, stopped);
        Assert.Equal(1, restarted);
        Assert.False(Directory.Exists(files.Backup));
        Assert.False(Directory.Exists(files.Failed));
    }

    private sealed class TransactionFiles : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(),
            $"VRPhoneScreenOverlay-update-transaction-{Guid.NewGuid():N}");

        public TransactionFiles()
        {
            Install = Path.Combine(_root, "installation");
            Stage = Path.Combine(_root, $".VRPhoneScreenOverlay-stage-{Token}");
            Backup = Path.Combine(_root, $".VRPhoneScreenOverlay-backup-{Token}");
            Failed = Path.Combine(_root, $".VRPhoneScreenOverlay-failed-{Token}");
            Directory.CreateDirectory(Install);
            Directory.CreateDirectory(Stage);
            File.WriteAllText(Path.Combine(Install, "component.dll"), "old");
            File.WriteAllText(Path.Combine(Stage, "component.dll"), "new");
        }

        public string Token { get; } = Guid.NewGuid().ToString("N");
        public string Install { get; }
        public string Stage { get; }
        public string Backup { get; }
        public string Failed { get; }

        public void Dispose()
        {
            foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }
    }
}
