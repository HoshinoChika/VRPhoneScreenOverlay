using System.IO.Compression;
using System.Security.Cryptography;
using VRPhoneScreenOverlay.Protocols;
using VRPhoneScreenOverlay.Service;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class DiagnosticUploadRetryTests
{
    [Fact]
    public async Task InitRetryKeepsGrantWithoutConsumingRateAndDoesNotShareAcrossSources()
    {
        using StoreFiles files = new(uploadsPerHour: 1);
        DiagnosticUploadInitResponse first = await files.InitAsync("source-a");
        DiagnosticUploadInitResponse repeat = await files.InitAsync("source-a");
        DiagnosticUploadInitResponse other = await files.InitAsync("source-b");

        Assert.Equal(first, repeat);
        Assert.NotEqual(first.UploadId, other.UploadId);
        Assert.NotEqual(first.UploadToken, other.UploadToken);
        Assert.Null(await files.Store.CreateAsync(
            files.Request with { CreatedAt = files.Request.CreatedAt.AddSeconds(1) },
            "source-a", "https", "example.invalid", CancellationToken.None));
    }

    [Fact]
    public async Task CompletionRetriesPreserveResultIncludingAfterRestartAndRejectWrongIdentity()
    {
        using StoreFiles files = new();
        DiagnosticUploadInitResponse init = await files.InitAsync();
        await files.UploadAsync(init);
        DiagnosticUploadCompleteRequest request = new(init.UploadId, files.Hash);
        Assert.Null(await files.Store.CompleteAsync(
            request with { BundleSha256 = new string('0', 64) }, CancellationToken.None));

        DiagnosticUploadCompleteResponse? first = await files.Store.CompleteAsync(request, CancellationToken.None);
        Assert.True(first?.Accepted);
        Assert.Equal(first, await files.Store.CompleteAsync(request, CancellationToken.None));
        DiagnosticUploadStore restarted = files.Restart();
        Assert.Equal(first, await restarted.CompleteAsync(request, CancellationToken.None));
        Assert.Null(await restarted.CompleteAsync(
            request with { BundleSha256 = new string('0', 64) }, CancellationToken.None));
        Assert.Null(await restarted.CompleteAsync(
            request with { UploadId = "../outside" }, CancellationToken.None));
        Assert.Single(Directory.EnumerateFiles(files.Completed, "*.zip"));
        string metadata = await File.ReadAllTextAsync(Path.Combine(files.Completed, init.UploadId + ".json"));
        Assert.DoesNotContain(init.UploadToken, metadata, StringComparison.Ordinal);
        Assert.DoesNotContain("source-a", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrentCompletionCommitsOneBundleAndOneReceipt()
    {
        using StoreFiles files = new();
        DiagnosticUploadInitResponse init = await files.InitAsync();
        await files.UploadAsync(init);
        DiagnosticUploadCompleteRequest request = new(init.UploadId, files.Hash);

        DiagnosticUploadCompleteResponse?[] results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => files.Store.CompleteAsync(request, CancellationToken.None).AsTask()));

        Assert.All(results, result => Assert.Equal(results[0], result));
        Assert.True(results[0]?.Accepted);
        Assert.Single(Directory.EnumerateFiles(files.Completed, "*.zip"));
        Assert.Single(Directory.EnumerateFiles(files.Completed, "*.json"));
        Assert.Empty(Directory.EnumerateFiles(files.Completed, "*.tmp"));
    }

    [Fact]
    public async Task PreparedReceiptRecoversCompletionInterruptedBetweenRenames()
    {
        using StoreFiles files = new();
        DiagnosticUploadInitResponse init = await files.InitAsync();
        await files.UploadAsync(init);
        DiagnosticUploadCompleteRequest request = new(init.UploadId, files.Hash);
        DiagnosticUploadCompleteResponse? first = await files.Store.CompleteAsync(request, CancellationToken.None);
        string metadata = Path.Combine(files.Completed, init.UploadId + ".json");
        File.Move(metadata, metadata + ".tmp");

        Assert.Equal(first, await files.Restart().CompleteAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task FailedReceiptPreparationKeepsUploadedBundleAvailableForRetry()
    {
        using StoreFiles files = new();
        DiagnosticUploadInitResponse init = await files.InitAsync();
        await files.UploadAsync(init);
        DiagnosticUploadCompleteRequest request = new(init.UploadId, files.Hash);
        string blocker = Path.Combine(files.Completed, init.UploadId + ".json.tmp");
        Directory.CreateDirectory(blocker);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await files.Store.CompleteAsync(request, CancellationToken.None));
        Assert.True(File.Exists(files.PendingPath(init)));
        Directory.Delete(blocker);

        Assert.True((await files.Store.CompleteAsync(request, CancellationToken.None))?.Accepted);
    }

    [Fact]
    public async Task DuplicateOrUnauthorizedPutCannotDeleteAcceptedBundle()
    {
        using StoreFiles files = new();
        DiagnosticUploadInitResponse init = await files.InitAsync();
        using MemoryStream rejectedBody = new(files.Zip);
        DiagnosticStoreResult rejected = await files.Store.UploadAsync(
            init.UploadId, "invalid", rejectedBody, files.Zip.Length, CancellationToken.None);
        Assert.Equal(401, rejected.StatusCode);
        await files.UploadAsync(init);
        using MemoryStream repeatedBody = new(files.Zip);
        DiagnosticStoreResult duplicate = await files.Store.UploadAsync(
            init.UploadId, init.UploadToken, repeatedBody, files.Zip.Length, CancellationToken.None);

        Assert.Equal(409, duplicate.StatusCode);
        Assert.True(File.Exists(files.PendingPath(init)));
        Assert.True((await files.Store.CompleteAsync(
            new DiagnosticUploadCompleteRequest(init.UploadId, files.Hash), CancellationToken.None))?.Accepted);
    }

    [Fact]
    public async Task RestartCleanupRemovesExpiredPendingButPreservesFreshAndUnrelatedFiles()
    {
        using StoreFiles files = new();
        DiagnosticUploadInitResponse old = await files.InitAsync("source-a");
        DiagnosticUploadInitResponse fresh = await files.InitAsync("source-b");
        await files.UploadAsync(old);
        await files.UploadAsync(fresh);
        File.SetLastWriteTimeUtc(files.PendingPath(old), files.Clock.GetUtcNow().AddDays(-30).UtcDateTime);
        string unrelated = Path.Combine(files.Pending, "operator-notes.txt");
        await File.WriteAllTextAsync(unrelated, "retain");
        File.SetLastWriteTimeUtc(unrelated, files.Clock.GetUtcNow().AddDays(-30).UtcDateTime);

        files.Restart().CleanupExpired();

        Assert.False(File.Exists(files.PendingPath(old)));
        Assert.True(File.Exists(files.PendingPath(fresh)));
        Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public async Task LiveGrantIsProtectedAndExpiredReceiptCannotBeReplayed()
    {
        using StoreFiles files = new();
        DiagnosticUploadInitResponse init = await files.InitAsync();
        await files.UploadAsync(init);
        File.SetLastWriteTimeUtc(files.PendingPath(init), files.Clock.GetUtcNow().AddDays(-30).UtcDateTime);
        files.Store.CleanupExpired();
        Assert.True(File.Exists(files.PendingPath(init)));
        DiagnosticUploadCompleteRequest request = new(init.UploadId, files.Hash);
        Assert.True((await files.Store.CompleteAsync(request, CancellationToken.None))?.Accepted);
        files.Clock.Advance(TimeSpan.FromDays(15));

        Assert.Null(await files.Restart().CompleteAsync(request, CancellationToken.None));
        files.Store.CleanupExpired();
        Assert.Empty(Directory.EnumerateFiles(files.Completed));
    }

    [Fact]
    public async Task MetadataCapacityPreservesRetriesAndReclaimsExpiredGrants()
    {
        using StoreFiles files = new(uploadsPerHour: 2048);
        DiagnosticUploadInitResponse first = await files.InitAsync();
        for (int index = 1; index < 1024; index++)
        {
            Assert.NotNull(await files.Store.CreateAsync(
                files.Request with { CreatedAt = files.Request.CreatedAt.AddTicks(index) },
                "source-a", "https", "example.invalid", CancellationToken.None));
        }

        Assert.Null(await files.Store.CreateAsync(
            files.Request with { CreatedAt = files.Request.CreatedAt.AddTicks(1024) },
            "source-a", "https", "example.invalid", CancellationToken.None));
        Assert.Equal(first, await files.InitAsync());
        files.Clock.Advance(TimeSpan.FromMinutes(11));
        DiagnosticUploadInitResponse next = await files.InitAsync();
        Assert.NotEqual(first.UploadId, next.UploadId);
        using MemoryStream body = new(files.Zip);
        Assert.Equal(401, (await files.Store.UploadAsync(
            first.UploadId, first.UploadToken, body, files.Zip.Length, CancellationToken.None)).StatusCode);
    }

    private sealed class StoreFiles : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(),
            $"VRPhoneScreenOverlay-diagnostic-retry-{Guid.NewGuid():N}");

        public StoreFiles(int uploadsPerHour = 8)
        {
            Options = new ServiceOptions(_root, 1024 * 1024, 14, uploadsPerHour);
            Store = new DiagnosticUploadStore(Options, Clock);
            using MemoryStream output = new();
            using (ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true))
            using (StreamWriter writer = new(archive.CreateEntry("diagnostic.json").Open()))
            {
                writer.Write("{}");
            }

            Zip = output.ToArray();
            Hash = Convert.ToHexStringLower(SHA256.HashData(Zip));
            Request = new DiagnosticUploadInitRequest("audit", Zip.Length, Hash, Clock.GetUtcNow());
        }

        public TestClock Clock { get; } = new();
        public ServiceOptions Options { get; }
        public DiagnosticUploadStore Store { get; }
        public byte[] Zip { get; }
        public string Hash { get; }
        public DiagnosticUploadInitRequest Request { get; }
        public string Pending => Path.Combine(_root, "diagnostics", "pending");
        public string Completed => Path.Combine(_root, "diagnostics", "completed");
        public string PendingPath(DiagnosticUploadInitResponse response) => Path.Combine(Pending, response.UploadId + ".upload");
        public DiagnosticUploadStore Restart() => new(Options, Clock);

        public async Task<DiagnosticUploadInitResponse> InitAsync(string address = "source-a") =>
            Assert.IsType<DiagnosticUploadInitResponse>(await Store.CreateAsync(
                Request, address, "https", "example.invalid", CancellationToken.None));

        public async Task UploadAsync(DiagnosticUploadInitResponse init)
        {
            using MemoryStream body = new(Zip);
            Assert.True((await Store.UploadAsync(
                init.UploadId, init.UploadToken, body, Zip.Length, CancellationToken.None)).Succeeded);
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }
}
