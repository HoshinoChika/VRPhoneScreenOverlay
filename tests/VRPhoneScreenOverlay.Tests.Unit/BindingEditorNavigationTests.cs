using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class BindingEditorNavigationTests
{
    [Fact]
    public async Task OpensTheAppBindingListOnceWithoutEnteringABindingFile()
    {
        using Socket socket = new("{\"type\":\"mailbox_registered\",\"mailbox_name\":\"bindingui/desktop\",\"message_id\":1}");
        await OpenVrBindingEditorNavigation.NavigateAsync(socket, CancellationToken.None);
        string frame = Assert.Single(socket.Sent, text => text.Contains("show_app_binding", StringComparison.Ordinal));
        Assert.StartsWith("mailbox_send bindingui/Desktop ", frame);
        JsonNode message = JsonNode.Parse(frame["mailbox_send bindingui/Desktop ".Length..])!;
        Assert.Equal(OpenVrInputManifest.ApplicationKey, message["app_key"]!.GetValue<string>());
        Assert.Null(message["controller_type"]);
        Assert.Null(message["action_set"]);
        Assert.True(socket.OpenRequestedBeforeReceive);
    }

    [Fact]
    public async Task UnavailablePageCancelsWithoutRetryingTheOpenRequest()
    {
        using Socket socket = new();
        using CancellationTokenSource stop = new(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OpenVrBindingEditorNavigation.NavigateAsync(socket, stop.Token));
        Assert.Single(socket.Sent, text => text.Contains("show_app_binding", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnrelatedMailboxRepliesCannotCompleteTheRequestOrTriggerAnotherNavigation()
    {
        using Socket socket = new("{\"type\":\"mailbox_registered\",\"mailbox_name\":\"another/page\",\"message_id\":1}",
            "{\"type\":\"mailbox_registered\",\"mailbox_name\":\"bindingui/desktop\",\"message_id\":1}");
        await OpenVrBindingEditorNavigation.NavigateAsync(socket, CancellationToken.None);
        Assert.Equal(2, socket.Received);
        Assert.Single(socket.Sent, text => text.Contains("show_app_binding", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvalidReplyFailsWithoutSendingAnotherNavigation()
    {
        using Socket socket = new(new string('x', 1024));
        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(() => OpenVrBindingEditorNavigation.NavigateAsync(socket, CancellationToken.None));
        Assert.Single(socket.Sent, text => text.Contains("show_app_binding", StringComparison.Ordinal));
    }

    private sealed class Socket(params string[] messages) : WebSocket
    {
        public List<string> Sent { get; } = [];
        public int Received { get; private set; }
        public bool OpenRequestedBeforeReceive { get; private set; }
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override void Dispose() { }
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        { Sent.Add(Encoding.UTF8.GetString(buffer.AsSpan())); return Task.CompletedTask; }
        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            OpenRequestedBeforeReceive = Sent.Any(text => text.Contains("show_app_binding", StringComparison.Ordinal));
            if (Received >= messages.Length) { await Task.Delay(Timeout.Infinite, cancellationToken); }
            byte[] bytes = Encoding.UTF8.GetBytes(messages[Received++]);
            bytes.AsSpan().CopyTo(buffer.AsSpan());
            return new(bytes.Length, WebSocketMessageType.Text, true);
        }
    }
}
