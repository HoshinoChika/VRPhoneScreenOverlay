using System.Net;
using System.Text.Json;
using VRPhoneScreenOverlay.Network;
using VRPhoneScreenOverlay.Service;
using VRPhoneScreenOverlay.Update;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class CloudUpdateTests
{
    [Theory]
    [InlineData("https://cdn.115.com/a?t=1", true)]
    [InlineData("https://cdnfhnfile.115cdn.net/a", true)]
    [InlineData("https://cdnfhnfile.115cdn.net.evil.invalid/a", false)]
    [InlineData("http://cdn.115.com/a", false)]
    [InlineData("https://cdn.115.com.evil.invalid/a", false)]
    [InlineData("https://account@cdn.115.com/a", false)]
    [InlineData("https://cdn.115.com:8443/a", false)]
    public void OnlyHttpsProviderDownloadsAreAccepted(string url, bool expected) => Assert.Equal(expected, OpenListCloudStorage.IsDownloadUrl(url));

    [Theory]
    [InlineData("{\"hashinfo\":{\"sha1\":\"ABC\"}}")]
    [InlineData("{\"hashinfo\":\"{\\\"sha1\\\":\\\"ABC\\\"}\"}")]
    public void ProviderHashSupportsBothOpenListWireFormats(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.True(OpenListCloudStorage.MatchesProviderHash(document.RootElement, "abc"));
        Assert.False(OpenListCloudStorage.MatchesProviderHash(document.RootElement, "different"));
    }

    [Fact]
    public void ExpiredProviderLinksAreRejectedAndUnknownExpiryIsConservative()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Assert.Throws<CloudTransferException>(() => OpenListCloudStorage.ReadExpiry(new Uri("https://cdn.115.com/f?t=1"), now));
        Assert.Equal(now.AddMinutes(15), OpenListCloudStorage.ReadExpiry(new Uri("https://cdn.115.com/f"), now));
    }

    [Fact]
    public async Task ConcurrentUpdateChecksShareOneRefreshAndFailuresKeepOnlyUnexpiredLeases()
    {
        using TempDirectory directory = new();
        Clock time = new();
        FakeCloud cloud = new(time);
        using CloudReleaseCache cache = new(new(directory.Path, 1024, 14, 8), cloud, time, (_, _) => Task.FromResult<CloudReleaseRecord?>(Record()));
        var requests = Enumerable.Range(0, 20).Select(_ => cache.GetAsync("0.2.6-beta.11", false, CancellationToken.None));
        Assert.All(await Task.WhenAll(requests), lease => Assert.NotNull(lease));
        Assert.Equal(1, cloud.Links);
        time.Now = time.Now.AddMinutes(4);
        cloud.Fail = true;
        Assert.NotNull(await cache.GetAsync("0.2.6-beta.11", false, CancellationToken.None));
        Assert.Equal(2, cloud.Links);
        Assert.NotNull(await cache.GetAsync("0.2.6-beta.11", true, CancellationToken.None));
        Assert.Equal(2, cloud.Links); // Forced requests cannot bypass the one-minute retry floor.
        time.Now = time.Now.AddMinutes(2);
        Assert.Null(await cache.GetAsync("0.2.6-beta.11", false, CancellationToken.None));
        Assert.Equal(2, cache.Failures);
    }

    [Fact]
    public async Task BrowserDownloadsCacheByAgentAndThrottleNewVariantsWithoutBlockingAppUpdates()
    {
        using TempDirectory directory = new();
        Clock time = new(); FakeCloud cloud = new(time);
        using CloudReleaseCache cache = new(new(directory.Path, 1024, 14, 8), cloud, time, (_, _) => Task.FromResult<CloudReleaseRecord?>(Record()));
        for (int i = 0; i < 6; i++) { Assert.NotNull(await cache.GetAsync("0.2.6-beta.11", false, CancellationToken.None, "Browser/" + i)); }
        Assert.Null(await cache.GetAsync("0.2.6-beta.11", false, CancellationToken.None, "Browser/next"));
        Assert.NotNull(await cache.GetAsync("0.2.6-beta.11", false, CancellationToken.None, "Browser/0"));
        Assert.NotNull(await cache.GetAsync("0.2.6-beta.11", false, CancellationToken.None));
        Assert.Equal(7, cloud.Links);
        Assert.Single(cloud.Paths.Distinct());
        Assert.All(cloud.Paths, path => Assert.EndsWith("/package.zip", path));
        Assert.Null(await cache.GetAsync("0.2.6-beta.11", false, CancellationToken.None, "bad\r\nheader"));
    }

    [Fact]
    public async Task BrowserCacheEvictionPreservesTheActiveSoftwareLease()
    {
        using TempDirectory directory = new();
        Clock time = new(); FakeCloud cloud = new(time) { Lifetime = TimeSpan.FromHours(1) };
        using CloudReleaseCache cache = new(new(directory.Path, 1024, 14, 8), cloud, time, (_, _) => Task.FromResult<CloudReleaseRecord?>(Record()));
        Assert.NotNull(await cache.GetAsync("0.2.6-beta.11", false, CancellationToken.None));
        for (int batch = 0; batch < 11; batch++)
        {
            for (int i = 0; i < 6; i++) { Assert.NotNull(await cache.GetAsync("0.2.6-beta.11", false, CancellationToken.None, $"Browser/{batch}-{i}")); }
            time.Now = time.Now.AddMinutes(1);
        }
        int before = cloud.Links;
        Assert.NotNull(await cache.GetAsync("0.2.6-beta.11", false, CancellationToken.None));
        Assert.Equal(before, cloud.Links);
    }

    [Fact]
    public async Task ForbiddenLeaseRefreshesOnceViaSignedReleaseEndpoint()
    {
        using RequestRecorder client = new();
        UpdateRelease release = new("0.2.6-beta.11", DateTimeOffset.UtcNow, new("https://cdn.115.com/file"), 10, new string('a', 64), "notes",
            new("https://example.invalid/vrphonescreen/api/v1/updates/download/0.2.6-beta.11"), DateTimeOffset.UtcNow.AddMinutes(5));
        using HttpResponseMessage response = await UpdatePackageStager.OpenPackageAsync(client, release, CancellationToken.None);
        Assert.Equal(2, client.Requests.Count);
        Assert.Equal("refresh=true", client.Requests[1].Query.TrimStart('?'));
        Assert.Equal("example.invalid", client.Requests[1].Host);
    }

    [Fact]
    public async Task WaitingPastLeaseExpirySkipsTheExpiredAddress()
    {
        using RequestRecorder client = new() { FailFirst = false };
        Uri stable = new("https://example.invalid/updates/download/0.2.6-beta.11");
        UpdateRelease release = new("0.2.6-beta.11", DateTimeOffset.UtcNow, new("https://cdn.115.com/file"), 10, new string('a', 64), "notes", stable, DateTimeOffset.UtcNow.AddSeconds(-1));
        using HttpResponseMessage response = await UpdatePackageStager.OpenPackageAsync(client, release, CancellationToken.None);
        Assert.Equal(stable, Assert.Single(client.Requests));
    }

    private static CloudReleaseRecord Record() => new(new("", ""), new("releases/0.2.6-beta.11/package.zip", 10, "hash"));
    internal sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    internal sealed class FakeCloud(Clock time) : ICloudStorage
    {
        public TimeSpan Lifetime { get; init; } = TimeSpan.FromMinutes(5);
        public int Links { get; private set; }
        public bool Fail { get; set; }
        public List<string> Archives { get; } = [];
        public List<string> Paths { get; } = [];
        public Task<CloudFileLink> GetLinkAsync(string path, long size, CancellationToken cancellationToken, string? userAgent = null)
        {
            Links++;
            Paths.Add(path);
            return Fail ? Task.FromException<CloudFileLink>(new CloudTransferException("TEST_FAILURE"))
                : Task.FromResult(new CloudFileLink("https://cdn.115.com/test", time.Now.Add(Lifetime), size));
        }
        public Task UploadArchiveAsync(string source, string destination, CancellationToken cancellationToken)
        {
            if (Fail) { throw new CloudTransferException("TEST_FAILURE"); }
            Archives.Add(destination);
            return Task.CompletedTask;
        }
    }
    internal sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VRPSO-cloud-tests-" + Guid.NewGuid().ToString("N"));
        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
    private sealed class RequestRecorder : IAppHttpClient
    {
        public List<Uri> Requests { get; } = [];
        public bool FailFirst { get; init; } = true;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "IAppHttpClient transfers response ownership through ValueTask; both tests dispose the returned response with using.")]
        public ValueTask<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> createRequest, NetworkRetryPolicy retryPolicy,
            HttpCompletionOption completionOption = HttpCompletionOption.ResponseHeadersRead, Func<HttpStatusCode, bool>? isSuccess = null, CancellationToken cancellationToken = default)
        {
            using HttpRequestMessage request = createRequest(); Requests.Add(request.RequestUri!);
            return FailFirst && Requests.Count == 1 ? ValueTask.FromException<HttpResponseMessage>(new NetworkException("TEST_FORBIDDEN", "test", 403))
                : ValueTask.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
        public void Dispose() { }
    }
}
