using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace VRPhoneScreenOverlay.SteamVR;

internal interface IOpenVrBindingSelectionChannel : IAsyncDisposable, IDisposable
{
    public ValueTask SelectAsync(string controllerType, string uri, CancellationToken cancellationToken);
}

internal sealed class OpenVrBindingSelectionChannel(HttpClient client, WebSocket socket) : IOpenVrBindingSelectionChannel
{
    internal static async ValueTask<IOpenVrBindingSelectionChannel> ConnectAsync(HttpClient client, CancellationToken token)
    {
        ClientWebSocket? socket = new();
        try
        {
            socket.Options.Proxy = null;
            socket.Options.SetRequestHeader("Origin", "http://localhost:27062");
            await socket.ConnectAsync(new Uri("ws://127.0.0.1:27062/"), token).ConfigureAwait(false);
            await socket.SendAsync(Encoding.UTF8.GetBytes("input_open").AsMemory(), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
            OpenVrBindingSelectionChannel channel = new(client, socket);
            socket = null; // Ownership transferred to the control channel.
            return channel;
        }
        finally { socket?.Dispose(); }
    }

    public async ValueTask SelectAsync(string controllerType, string uri, CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        JsonObject selection = new()
        { ["app_key"] = OpenVrInputManifest.ApplicationKey, ["controller_type"] = controllerType, ["url"] = uri };
        using StringContent content = new(selection.ToJsonString(), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync("input/selectconfig.action", content, deadline.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        // The editor also waits for select_config_complete. HTTP 200 alone does
        // not prove that SteamVR accepted and loaded the binding.
        while (true)
        {
            JsonObject message = await ReceiveAsync(deadline.Token).ConfigureAwait(false);
            IEnumerable<JsonNode?> notifications = message["notifications"] switch
            {
                JsonArray values => values,
                JsonObject values => values.Select(item => item.Value),
                _ => [],
            };
            foreach (JsonNode? notification in notifications)
            {
                if (notification?["type"]?.GetValue<string>() != "select_config_complete") { continue; }
                string? app = notification["app_key"]?.GetValue<string>();
                if (app is not null && app != OpenVrInputManifest.ApplicationKey) { continue; }
                string? type = notification["controller_type"]?.GetValue<string>();
                if (type is not null && type != controllerType) { continue; }
                if (!string.IsNullOrEmpty(notification["error_to_show"]?.GetValue<string>()) || notification["success"]?.GetValue<bool>() == false)
                { throw new InvalidDataException("SteamVR rejected the binding selection."); }
                return;
            }
        }
    }

    private async Task<JsonObject> ReceiveAsync(CancellationToken token)
    {
        using MemoryStream message = new();
        byte[] buffer = new byte[4096];
        while (true)
        {
            ValueWebSocketReceiveResult result = await socket.ReceiveAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (result.MessageType != WebSocketMessageType.Text || message.Length + result.Count > 1_048_576)
            { throw new InvalidDataException("Invalid SteamVR binding notification."); }
            await message.WriteAsync(buffer.AsMemory(0, result.Count), token).ConfigureAwait(false);
            if (result.EndOfMessage) { break; }
        }
        return JsonNode.Parse(message.ToArray()) as JsonObject ?? throw new InvalidDataException("Invalid binding notification JSON.");
    }

    public ValueTask DisposeAsync()
    {
        // This is a short-lived control connection, never a per-frame worker.
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => socket.Dispose();
}
