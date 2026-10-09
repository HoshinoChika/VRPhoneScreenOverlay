using System.Security.Cryptography;
using System.Text.Json;
using VRPhoneScreenOverlay.Service;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class MonthlyDiagnosticArchiveTests
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BeijingMonthRolloverArchivesWholeClosedMonthAndRetentionWaitsForVerification(bool failUpload)
    {
        using CloudUpdateTests.TempDirectory directory = new();
        CloudUpdateTests.Clock time = new() { Now = new(2026, 10, 31, 16, 16, 0, TimeSpan.Zero) };
        ServiceOptions options = new(directory.Path, 1024, 14, 8) { ArchiveDiagnosticsToCloud = true };
        DiagnosticUploadStorage storage = new(options, time);
        string old = await AddAsync(directory.Path, 'A', new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero));
        string lastOctober = await AddAsync(directory.Path, 'B', new(2026, 10, 31, 15, 59, 0, TimeSpan.Zero));
        string november = await AddAsync(directory.Path, 'C', new(2026, 10, 31, 16, 0, 0, TimeSpan.Zero));
        storage.CleanupExpired(new HashSet<string>(), time.Now);
        Assert.True(File.Exists(old));
        CloudUpdateTests.FakeCloud cloud = new(time) { Fail = failUpload };
        MonthlyDiagnosticArchiver archiver = new(options, cloud, time);
        if (failUpload)
        {
            await Assert.ThrowsAsync<CloudTransferException>(() => archiver.ArchiveClosedMonthsAsync(CancellationToken.None));
            storage.CleanupExpired(new HashSet<string>(), time.Now);
            Assert.True(File.Exists(old));
            Assert.Empty(Directory.GetFiles(Path.Combine(directory.Path, "diagnostics", "archived")));
        }
        else
        {
            Assert.Equal(1, await archiver.ArchiveClosedMonthsAsync(CancellationToken.None));
            Assert.Equal("diagnostics/2026-10.zip", Assert.Single(cloud.Archives));
            Assert.Equal(0, await archiver.ArchiveClosedMonthsAsync(CancellationToken.None));
            storage.CleanupExpired(new HashSet<string>(), time.Now);
            Assert.False(File.Exists(old));
        }
        Assert.True(File.Exists(lastOctober));
        Assert.True(File.Exists(november));
    }

    [Fact]
    public async Task MonthBoundaryWaitsForExistingUploadGrantsToSettle()
    {
        using CloudUpdateTests.TempDirectory directory = new();
        CloudUpdateTests.Clock time = new() { Now = new(2026, 10, 31, 16, 1, 0, TimeSpan.Zero) };
        ServiceOptions options = new(directory.Path, 1024, 14, 8) { ArchiveDiagnosticsToCloud = true };
        await AddAsync(directory.Path, 'F', new(2026, 10, 31, 15, 59, 0, TimeSpan.Zero));
        CloudUpdateTests.FakeCloud cloud = new(time);
        Assert.Equal(0, await new MonthlyDiagnosticArchiver(options, cloud, time).ArchiveClosedMonthsAsync(CancellationToken.None));
        Assert.Empty(cloud.Archives);
    }

    [Fact]
    public async Task CompletedMonthIndexRecoversInterruptedPerFileMarkersWithoutReupload()
    {
        using CloudUpdateTests.TempDirectory directory = new();
        CloudUpdateTests.Clock time = new();
        ServiceOptions options = new(directory.Path, 1024, 14, 8) { ArchiveDiagnosticsToCloud = true };
        DiagnosticUploadStorage storage = new(options, time);
        string source = await AddAsync(directory.Path, 'D', new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        string metadata = Path.ChangeExtension(source, ".json");
        string markers = Path.Combine(directory.Path, "diagnostics", "archived");
        Directory.CreateDirectory(markers);
        await File.WriteAllTextAsync(Path.Combine(markers, "2026-10.index.json"), "[" + await File.ReadAllTextAsync(metadata) + "]");
        CloudUpdateTests.FakeCloud cloud = new(time);
        Assert.Equal(0, await new MonthlyDiagnosticArchiver(options, cloud, time).ArchiveClosedMonthsAsync(CancellationToken.None));
        Assert.Empty(cloud.Archives);
        storage.CleanupExpired(new HashSet<string>(), time.Now);
        Assert.False(File.Exists(source));
    }

    [Fact]
    public async Task UnarchivedDiagnosticStorageCannotExceedAdmissionBudget()
    {
        using CloudUpdateTests.TempDirectory directory = new();
        CloudUpdateTests.Clock time = new();
        ServiceOptions options = new(directory.Path, 1024, 14, 8) { ArchiveDiagnosticsToCloud = true, MaximumStoredDiagnosticBytes = 10 };
        DiagnosticUploadStorage storage = new(options, time);
        await AddAsync(directory.Path, 'E', time.Now);
        Assert.False(storage.HasCapacity(1));
    }

    private static async Task<string> AddAsync(string root, char suffix, DateTimeOffset received)
    {
        string directory = Path.Combine(root, "diagnostics", "completed"); Directory.CreateDirectory(directory);
        string id = "VD-20261007-" + new string(suffix, 32);
        byte[] bytes = "synthetic diagnostic bundle"u8.ToArray();
        string path = Path.Combine(directory, id + ".zip");
        await File.WriteAllBytesAsync(path, bytes);
        string metadata = Path.Combine(directory, id + ".json");
        await File.WriteAllTextAsync(metadata, JsonSerializer.Serialize(new
        {
            id,
            receivedAt = received,
            appVersion = "0.2.6-beta.11",
            bundleSize = bytes.Length,
            bundleSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            expiresAt = received.AddDays(14)
        }, _json));
        File.SetLastWriteTimeUtc(path, received.UtcDateTime); File.SetLastWriteTimeUtc(metadata, received.UtcDateTime);
        return path;
    }
}
