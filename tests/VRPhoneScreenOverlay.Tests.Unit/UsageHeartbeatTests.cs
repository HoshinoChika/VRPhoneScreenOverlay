using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using VRPhoneScreenOverlay.Diagnostics.Usage;
using VRPhoneScreenOverlay.Network;
using VRPhoneScreenOverlay.Service;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class UsageHeartbeatTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vrpso-usage-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DamagedStatisticsDoNotPreventOtherEndpointsFromStarting()
    {
        Directory.CreateDirectory(Path.Combine(_root, "usage"));
        string file = Path.Combine(_root, "usage", "installations.json");
        await File.WriteAllTextAsync(file, "{broken");
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton(new ServiceOptions(_root, 1, 1, 1));
        builder.Services.AddUsageHeartbeat();
        await using WebApplication app = builder.Build();
        app.MapGet("/unrelated", () => "still-serving");
        app.MapUsageHeartbeat();
        await app.StartAsync();
        UsageHeartbeatStore store = app.Services.GetRequiredService<UsageHeartbeatStore>();
        Assert.False(store.IsAvailable);
        Assert.Equal(503, store.Record(new(1, Guid.NewGuid().ToString("N"))));
        string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using HttpClientHandler handler = new() { UseProxy = false };
        using HttpClient http = new(handler);
        Assert.Equal("still-serving", await http.GetStringAsync(new Uri(address + "/unrelated")));
        await app.StopAsync();
        Assert.Equal("{broken", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public void AdmissionLimitsNewIdentitiesButNeverExistingHeartbeats()
    {
        ManualTime time = new();
        using UsageHeartbeatStore store = new(new(_root, 1, 1, 1), time);
        string first = Guid.NewGuid().ToString("N");
        Assert.Equal(204, store.Record(new(1, first), "synthetic-network"));
        for (int index = 1; index < 120; index++)
        { Assert.Equal(204, store.Record(new(1, Guid.NewGuid().ToString("N")), "synthetic-network")); }
        Assert.Equal(429, store.Record(new(1, Guid.NewGuid().ToString("N")), "synthetic-network"));
        Assert.Equal(204, store.Record(new(1, first), "synthetic-network"));
        Assert.Equal(204, store.Record(new(1, Guid.NewGuid().ToString("N")), "another-network"));
        time.Now += TimeSpan.FromHours(1);
        Assert.Equal(204, store.Record(new(1, Guid.NewGuid().ToString("N")), "synthetic-network"));
    }

    [Fact]
    public async Task HttpEndpointsValidateRequestsAndProtectStatistics()
    {
        string? previous = Environment.GetEnvironmentVariable("USAGE_STATS_TOKEN");
        string token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable("USAGE_STATS_TOKEN", token);
        try
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddSingleton(new ServiceOptions(_root, 1, 1, 1));
            builder.Services.AddUsageHeartbeat();
            await using WebApplication app = builder.Build();
            app.MapUsageHeartbeat();
            await app.StartAsync();
            IServer server = app.Services.GetRequiredService<IServer>();
            string address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using HttpClientHandler handler = new() { UseProxy = false };
            using HttpClient http = new(handler) { BaseAddress = new Uri(address) };
            const string heartbeat = "/vrphonescreen/api/v1/usage/heartbeat";
            const string stats = "/vrphonescreen/api/v1/usage/stats";
            using HttpResponseMessage denied = await http.GetAsync(stats);
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            using StringContent invalid = new("{broken", Encoding.UTF8, "application/json");
            using HttpResponseMessage rejected = await http.PostAsync(heartbeat, invalid);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            using StringContent huge = new(new string('x', 1025), Encoding.UTF8, "application/json");
            using HttpResponseMessage oversized = await http.PostAsync(heartbeat, huge);
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
            using HttpResponseMessage accepted = await http.PostAsJsonAsync(heartbeat,
                new { schemaVersion = 1, installationId = Guid.NewGuid().ToString("N") });
            Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
            http.DefaultRequestHeaders.Authorization = new("Bearer", token);
            using HttpResponseMessage summary = await http.GetAsync(stats);
            Assert.True(summary.IsSuccessStatusCode);
            Assert.True(summary.Headers.CacheControl!.NoStore);
            using JsonDocument json = JsonDocument.Parse(await summary.Content.ReadAsStringAsync());
            Assert.Equal(1, json.RootElement.GetProperty("totalInstallations").GetInt32());
            Assert.Equal(1, json.RootElement.GetProperty("online").GetInt32());
            await app.StopAsync();
            Assert.True(File.Exists(Path.Combine(_root, "usage", "installations.json")));
        }
        finally { Environment.SetEnvironmentVariable("USAGE_STATS_TOKEN", previous); }
    }

    [Fact]
    public async Task IdentitySurvivesRestartAndConcurrentCreation()
    {
        string path = Path.Combine(_root, "id.txt");
        string[] identities = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => UsageInstallationIdentity.LoadAsync(path, CancellationToken.None)));
        Assert.Single(identities.Distinct());
        Assert.Equal(identities[0], await UsageInstallationIdentity.LoadAsync(path, CancellationToken.None));
        Assert.True(Guid.TryParseExact(identities[0], "N", out _));
    }

    [Fact]
    public async Task CorruptIdentityDoesNotCreateAnotherUser()
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "id.txt");
        await File.WriteAllTextAsync(path, "broken");
        await Assert.ThrowsAsync<InvalidDataException>(() => UsageInstallationIdentity.LoadAsync(path, CancellationToken.None));
        Assert.Equal("broken", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task CountsDeduplicateExpireAndSurviveServerRestart()
    {
        ManualTime time = new();
        ServiceOptions options = new(_root, 1, 1, 1);
        string id = Guid.NewGuid().ToString("N");
        using (UsageHeartbeatStore store = new(options, time))
        {
            Assert.Equal(204, store.Record(new(1, id)));
            Assert.Equal(204, store.Record(new(1, id.ToUpperInvariant())));
            Assert.Equal(1, store.GetStatistics().TotalInstallations);
            Assert.Equal(1, store.GetStatistics().Online);
            time.Now += TimeSpan.FromSeconds(180);
            Assert.Equal(0, store.GetStatistics().Online);
            Assert.Equal(1, store.GetStatistics().Active24Hours);
            time.Now += TimeSpan.FromDays(1);
            Assert.Equal(0, store.GetStatistics().Active24Hours);
            Assert.Equal(1, store.GetStatistics().Active7Days);
            time.Now += TimeSpan.FromDays(7);
            Assert.Equal(0, store.GetStatistics().Active7Days);
            Assert.Equal(1, store.GetStatistics().Active30Days);
            time.Now += TimeSpan.FromDays(30);
            Assert.Equal(0, store.GetStatistics().Active30Days);
            store.Record(new(1, Guid.NewGuid().ToString("N")));
            await store.SaveAsync(CancellationToken.None);
            Assert.DoesNotContain(id, await File.ReadAllTextAsync(Path.Combine(_root, "usage", "installations.json")), StringComparison.OrdinalIgnoreCase);
        }
        using UsageHeartbeatStore reloaded = new(options, time);
        Assert.Equal(2, reloaded.GetStatistics().TotalInstallations);
        Assert.Equal(1, reloaded.GetStatistics().Online);
        reloaded.Record(new(1, id));
        Assert.Equal(2, reloaded.GetStatistics().Online);
    }

    [Theory]
    [InlineData(2, "01234567890123456789012345678901")]
    [InlineData(1, "bad")]
    [InlineData(1, "00000000000000000000000000000000")]
    [InlineData(1, null)]
    public void InvalidHeartbeatsNeverCount(int schema, string? id)
    {
        using UsageHeartbeatStore store = new(new(_root, 1, 1, 1));
        Assert.Equal(400, store.Record(new(schema, id!)));
        Assert.Equal(0, store.GetStatistics().TotalInstallations);
    }

    [Fact]
    public void StatsRequireServerSecret()
    {
        string token = new('a', 64);
        Assert.True(UsageEndpoints.IsAuthorized("Bearer " + token, token));
        Assert.False(UsageEndpoints.IsAuthorized("Bearer " + new string('b', 64), token));
        Assert.False(UsageEndpoints.IsAuthorized("", null));
        Assert.False(UsageEndpoints.IsAuthorized("Bearer short", "short"));
    }

    [Fact]
    public async Task FailureWaitsForNextMinuteAndCancellationStopsLoop()
    {
        ManualTime time = new();
        using RecordingHttp http = new();
        UsageHeartbeatClient client = new(Path.Combine(_root, "id.txt"), http, time, TestServiceConfiguration.Value.UsageHeartbeatUri);
        client.Start();
        await WaitUntilAsync(() => time.Timer is not null);
        time.Fire(); // Startup jitter.
        await WaitUntilAsync(() => http.Count == 1);
        Assert.Equal(TimeSpan.FromSeconds(60), time.Period);
        Assert.Equal(NetworkRetryPolicy.None, http.RetryPolicy);
        Assert.Equal(1, http.Count);
        time.Fire();
        await WaitUntilAsync(() => http.Count == 2);
        await client.DisposeAsync();
        time.Fire();
        Assert.Equal(2, http.Count);
        Assert.Equal("HEARTBEAT_STOPPED", client.ReasonCode);
        using JsonDocument payload = JsonDocument.Parse(http.Body!);
        Assert.Equal(2, payload.RootElement.EnumerateObject().Count());
        Assert.Equal(TestServiceConfiguration.Value.UsageHeartbeatUri, http.Uri);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (!condition()) { await Task.Delay(10, timeout.Token); }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) { Directory.Delete(_root, true); }
    }

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public ManualTimer? Timer { get; private set; }
        public TimeSpan Period { get; private set; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Period = period;
            Timer = new ManualTimer(callback, state);
            return Timer;
        }
        public void Fire() => Timer?.Fire();
    }

    private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        private bool _disposed;
        public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;
        public void Fire() { if (!_disposed) { callback(state); } }
        public void Dispose() => _disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    private sealed class RecordingHttp : IAppHttpClient
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public string? Body { get; private set; }
        public Uri? Uri { get; private set; }
        public NetworkRetryPolicy RetryPolicy { get; private set; }
        public async ValueTask<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> createRequest,
            NetworkRetryPolicy retryPolicy, HttpCompletionOption completionOption = HttpCompletionOption.ResponseHeadersRead,
            Func<HttpStatusCode, bool>? isSuccess = null, CancellationToken cancellationToken = default)
        {
            using HttpRequestMessage request = createRequest();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Uri = request.RequestUri;
            RetryPolicy = retryPolicy;
            Interlocked.Increment(ref _count);
            throw new NetworkException(NetworkReasonCodes.Unavailable, "test outage");
        }
        public void Dispose() { }
    }
}
