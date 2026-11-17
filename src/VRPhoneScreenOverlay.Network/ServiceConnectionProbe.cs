using System.Text.Json;

namespace VRPhoneScreenOverlay.Network;

/// <summary>Checks server reachability only; does not claim AI availability.</summary>
public sealed class ServiceConnectionProbe : IDisposable
{
    private readonly HttpClient _http;
    private readonly HttpMessageHandler _handler;
    private readonly Uri? _endpoint;

    public ServiceConnectionProbe(ClientServiceConfiguration configuration, HttpMessageHandler? handler = null)
    {
        _handler = handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(2) };
        _http = new HttpClient(_handler, disposeHandler: false);
        Uri? source = configuration.UpdateManifestUri ?? configuration.UsageHeartbeatUri;
        _endpoint = source is null ? null : new Uri(source, "/vrphonescreen/api/v1/health");
    }

    public async Task<bool> CheckAsync(CancellationToken token)
    {
        if (_endpoint is null) { return false; }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var response = await _http.GetAsync(_endpoint, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) { return false; }
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            byte[] bytes = new byte[4097];
            int length = 0;
            while (length < bytes.Length)
            {
                int read = await stream.ReadAsync(bytes.AsMemory(length), deadline.Token).ConfigureAwait(false);
                if (read == 0) { break; }
                length += read;
            }
            if (length > 4096) { return false; }
            using var json = JsonDocument.Parse(bytes.AsMemory(0, length));
            return json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or OperationCanceledException)
        { return false; }
    }

    public void Dispose() { _http.Dispose(); _handler.Dispose(); }
}
