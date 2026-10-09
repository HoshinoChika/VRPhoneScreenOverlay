using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace VRPhoneScreenOverlay.SteamVR;

/// <summary>Opens this app's binding list with one desktop mailbox request.</summary>
public static class OpenVrBindingEditorNavigation
{
    public static async ValueTask<OpenVrBindingResult> NavigateAsync(CancellationToken token)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using ClientWebSocket socket = new();
        socket.Options.Proxy = null;
        socket.Options.SetRequestHeader("Origin", "http://localhost:27062");
        try
        {
            await socket.ConnectAsync(new Uri("ws://127.0.0.1:27062/"), deadline.Token).ConfigureAwait(false);
            await NavigateAsync(socket, deadline.Token).ConfigureAwait(false);
            return new(true, OpenVrReasonCodes.BindingUiOpened, "已请求打开 VRPhoneScreen Overlay 的手柄绑定选择页面");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is OperationCanceledException or WebSocketException or IOException or InvalidOperationException or System.Text.Json.JsonException)
        { return new(false, OpenVrReasonCodes.BindingUiFailed, "SteamVR 手柄绑定页面打开失败，请检查 SteamVR 状态后重试"); }
    }

    internal static async Task NavigateAsync(WebSocket socket, CancellationToken token)
    {
        string mailbox = "vrphonescreenoverlay.binding-editor/" + Guid.NewGuid().ToString("N");
        await SendAsync(socket, "mailbox_open " + mailbox, token).ConfigureAwait(false);
        JsonObject registration = new()
        {
            ["type"] = "request_mailbox_registration_notification",
            ["mailbox_name"] = "bindingui/Desktop",
            ["returnAddress"] = mailbox,
            ["message_id"] = 1,
        };
        await SendAsync(socket, "mailbox_send web_server_mailbox " + registration.ToJsonString(), token).ConfigureAwait(false);

        // SteamVR starts the desktop web helper when this message targets a closed
        // mailbox. A controller_type would instead enter EditCurrentBinding, and
        // a preceding native OpenBindingUI would cause a second page transition.
        JsonObject target = new()
        {
            ["type"] = "show_app_binding",
            ["app_key"] = OpenVrInputManifest.ApplicationKey,
        };
        await SendAsync(socket, "mailbox_send bindingui/Desktop " + target.ToJsonString(), token).ConfigureAwait(false);
        for (int messages = 0; messages < 32; messages++)
        {
            JsonObject reply = await ReceiveAsync(socket, token).ConfigureAwait(false);
            if (reply["type"]?.GetValue<string>() == "mailbox_registered" && reply["message_id"]?.GetValue<int>() == 1 &&
                string.Equals(reply["mailbox_name"]?.GetValue<string>(), "bindingui/Desktop", StringComparison.OrdinalIgnoreCase)) { return; }
        }
        throw new InvalidDataException("Desktop binding mailbox did not become available.");
    }

    private static async Task SendAsync(WebSocket socket, string text, CancellationToken token) =>
        await socket.SendAsync(Encoding.UTF8.GetBytes(text).AsMemory(), WebSocketMessageType.Text, true, token).ConfigureAwait(false);

    private static async Task<JsonObject> ReceiveAsync(WebSocket socket, CancellationToken token)
    {
        using MemoryStream data = new();
        byte[] buffer = new byte[1024];
        while (true)
        {
            ValueWebSocketReceiveResult result = await socket.ReceiveAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (result.MessageType != WebSocketMessageType.Text || data.Length + result.Count > 4096)
            { throw new InvalidDataException("Invalid desktop binding mailbox reply."); }
            await data.WriteAsync(buffer.AsMemory(0, result.Count), token).ConfigureAwait(false);
            if (result.EndOfMessage) { break; }
        }
        return JsonNode.Parse(data.ToArray()) as JsonObject ?? throw new InvalidDataException("Invalid desktop binding mailbox reply.");
    }
}
