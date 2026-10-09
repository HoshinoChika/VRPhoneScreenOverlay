using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class BindingSelectionChannelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectionWaitsForItsCompletionNotification(bool objectNotifications)
    {
        using FakeHttp http = new();
        using HttpClient client = new(http, disposeHandler: false) { BaseAddress = new Uri("http://localhost:27062/") };
        using FakeSocket socket = new(
            "{\"notifications\":[{\"type\":\"select_config_complete\",\"app_key\":\"another.app\"}]}",
            objectNotifications
                ? "{\"notifications\":{\"1\":{\"type\":\"select_config_complete\",\"controller_type\":\"pico_controller\"}}}"
                : "{\"notifications\":[{\"type\":\"action_bindings_reloaded\"},{\"type\":\"select_config_complete\"}]}"
        );
        await using OpenVrBindingSelectionChannel channel = new(client, socket);
        await channel.SelectAsync("pico_controller", "file:///test/default.json", CancellationToken.None);
        Assert.Equal(2, socket.Received);
        Assert.Equal(OpenVrInputManifest.ApplicationKey, http.Selection!["app_key"]!.GetValue<string>());
        Assert.Equal("pico_controller", http.Selection["controller_type"]!.GetValue<string>());
        Assert.Equal("file:///test/default.json", http.Selection["url"]!.GetValue<string>());
    }

    [Fact]
    public async Task HttpSuccessCannotHideSteamVrRejection()
    {
        using FakeHttp http = new();
        using HttpClient client = new(http, disposeHandler: false) { BaseAddress = new Uri("http://localhost:27062/") };
        using FakeSocket socket = new("{\"notifications\":[{\"type\":\"select_config_complete\",\"error_to_show\":\"failed\"}]}");
        await using OpenVrBindingSelectionChannel channel = new(client, socket);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await channel.SelectAsync("pico_controller", "file:///test/default.json", CancellationToken.None));
    }

    [Fact]
    public async Task MissingCompletionCanBeCancelled()
    {
        using FakeHttp http = new();
        using HttpClient client = new(http, disposeHandler: false) { BaseAddress = new Uri("http://localhost:27062/") };
        using FakeSocket socket = new();
        await using OpenVrBindingSelectionChannel channel = new(client, socket);
        using CancellationTokenSource stop = new(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await channel.SelectAsync("pico_controller", "file:///test/default.json", stop.Token));
    }

    private sealed class FakeHttp : HttpMessageHandler
    {
        public JsonNode? Selection { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/input/selectconfig.action", request.RequestUri!.AbsolutePath);
            Selection = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }

    private sealed class FakeSocket(params string[] messages) : WebSocket
    {
        public int Received { get; private set; }
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override void Dispose() { }
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) => Task.CompletedTask;
        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            if (Received >= messages.Length) { await Task.Delay(Timeout.Infinite, cancellationToken); }
            byte[] bytes = Encoding.UTF8.GetBytes(messages[Received++]);
            bytes.AsSpan().CopyTo(buffer.AsSpan());
            return new WebSocketReceiveResult(bytes.Length, WebSocketMessageType.Text, true);
        }
    }
}
