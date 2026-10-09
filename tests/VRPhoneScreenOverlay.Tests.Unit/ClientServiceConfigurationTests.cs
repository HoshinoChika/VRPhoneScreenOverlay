using System.Text.Json;
using VRPhoneScreenOverlay.Diagnostics;
using VRPhoneScreenOverlay.Diagnostics.Usage;
using VRPhoneScreenOverlay.Network;
using VRPhoneScreenOverlay.Update;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class ClientServiceConfigurationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "service-config-" + Guid.NewGuid().ToString("N"));

    public ClientServiceConfigurationTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ConfiguredEndpointsAreLoadedAndCredentialsAreRejected()
    {
        string file = Path.Combine(_directory, "service.config.json");
        File.WriteAllText(file, JsonSerializer.Serialize(new { SchemaVersion = 1, Client = TestServiceConfiguration.Value }));
        ClientServiceConfigurationResult result = ClientServiceConfiguration.Load(file);
        Assert.Equal("SERVICE_CONFIG_LOADED", result.ReasonCode);
        Assert.Equal(TestServiceConfiguration.Value, result.Configuration);
        File.WriteAllText(file, """{"SchemaVersion":1,"Client":{"UpdateChannel":"beta"},"Deployment":{"Token":"synthetic"}}""");
        Assert.Equal("SERVICE_CONFIG_INVALID", ClientServiceConfiguration.Load(file).ReasonCode);
    }

    [Theory]
    [InlineData("{invalid")]
    [InlineData("{\"SchemaVersion\":2,\"Client\":{}}")]
    [InlineData("{\"SchemaVersion\":1,\"Client\":{\"UpdateManifestUri\":\"http://service.invalid/manifest\"}}")]
    [InlineData("{\"SchemaVersion\":1,\"Client\":{\"DiagnosticsInitUri\":\"https://user:" + "password@service.invalid/init\"}}")]
    [InlineData("{\"SchemaVersion\":1,\"Client\":{\"UsageHeartbeatUri\":\"https://service.invalid/#token\"}}")]
    [InlineData("{\"SchemaVersion\":1,\"Client\":{\"Token\":\"synthetic\"}}")]
    [InlineData("{\"SchemaVersion\":1,\"Client\":{\"UpdateManifestUri\":\"https://service.invalid/manifest?token=synthetic\"}}")]
    [InlineData("{\"SchemaVersion\":1,\"Client\":{\"UpdateManifestUri\":\"https://service.invalid/manifest?%74oken=synthetic\"}}")]
    [InlineData("{\"SchemaVersion\":1,\"Client\":{\"DiagnosticsInitUri\":\"https://service.invalid/init?API_KEY=synthetic\"}}")]
    public void InvalidConfigurationDisablesNetworkWithoutReturningPrivateValues(string json)
    {
        string file = Path.Combine(_directory, "service.config.json");
        File.WriteAllText(file, json);
        ClientServiceConfigurationResult result = ClientServiceConfiguration.Load(file);
        Assert.Equal("SERVICE_CONFIG_INVALID", result.ReasonCode);
        Assert.Equal(ClientServiceConfiguration.Disabled, result.Configuration);
    }

    [Fact]
    public async Task UnconfiguredServicesDoNotIssueAnyHttpRequest()
    {
        Assert.Equal("SERVICE_CONFIG_MISSING", ClientServiceConfiguration.Load(Path.Combine(_directory, "absent.json")).ReasonCode);
        using NoNetwork http = new();
        using HttpUpdateService update = new(UpdateServiceOptions.FromConfiguration(ClientServiceConfiguration.Disabled), http, false);
        Assert.Equal("UPDATE_SERVICE_NOT_CONFIGURED", (await update.CheckAsync("0.0.0", CancellationToken.None)).ReasonCode);
        using UsageHeartbeatClient heartbeat = new(Path.Combine(_directory, "id.txt"), http);
        heartbeat.Start();
        Assert.Equal("HEARTBEAT_NOT_CONFIGURED", heartbeat.ReasonCode);
        Assert.Null(DiagnosticsServiceOptions.FromConfiguration(ClientServiceConfiguration.Disabled).InitUri);
    }

    [Fact]
    public void OversizedConfigurationIsRejectedBeforeParsing()
    {
        string file = Path.Combine(_directory, "service.config.json");
        File.WriteAllText(file, new string(' ', 65537));
        Assert.Equal("SERVICE_CONFIG_INVALID", ClientServiceConfiguration.Load(file).ReasonCode);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class NoNetwork : IAppHttpClient
    {
        public void Dispose() { }
        public ValueTask<System.Net.Http.HttpResponseMessage> SendAsync(Func<System.Net.Http.HttpRequestMessage> createRequest,
            NetworkRetryPolicy retryPolicy, System.Net.Http.HttpCompletionOption completionOption,
            Func<System.Net.HttpStatusCode, bool>? isSuccess, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Unconfigured clients must not issue network requests.");
    }
}
