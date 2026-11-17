using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.Protocols;

namespace VRPhoneScreenOverlay.Network;

/// <summary>Only public key and nonce reach our server. Images go directly to DeepSeek over HTTPS.</summary>
public sealed class BuiltInTranslationClient(HttpClient http, ClientServiceConfiguration configuration, IIncidentRecorder? incident = null) : ITranslationModelClient, IDisposable
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly object _sync = new();
    private readonly CancellationTokenSource _lifetime = new();
    private string? _credential;
    private Task<string>? _acquiring;
    private bool _disposed;

    // One shared bounded acquisition, no queue of credential requests. A cancelled caller
    // stops waiting without cancelling another caller's acquisition. The 10s deadline remains.
    private async Task<string> GetCredentialAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Task<string> pending;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_credential is not null) { return _credential; }
            if (_acquiring is null || _acquiring.IsCompleted)
            {
                CancellationToken lifetime = _lifetime.Token;
                _acquiring = Task.Run(async () =>
                {
                    string key = await AcquireAsync(lifetime).ConfigureAwait(false);
                    lock (_sync)
                    {
                        ObjectDisposedException.ThrowIf(_disposed, this);
                        _credential = key;
                    }
                    return key;
                }, CancellationToken.None);
                _ = _acquiring.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            pending = _acquiring;
        }
        try { return await pending.WaitAsync(token).ConfigureAwait(false); }
        catch (HttpRequestException) { throw new InvalidDataException("TRANSLATION_BUILTIN_UNAVAILABLE"); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new InvalidDataException("TRANSLATION_BUILTIN_UNAVAILABLE"); }
    }

    private void Invalidate(string rejected)
    {
        lock (_sync)
        {
            // A late failure from an older generation must not erase a refreshed key.
            if (ReferenceEquals(_credential, rejected)) { _credential = null; _acquiring = null; }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) { return; }
            _disposed = true;
            _credential = null;
            _acquiring = null;
        }
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
    public async Task<TranslationRecord> AnalyzeAsync(TranslationMode mode, TranslationFrame frame, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(mode) || frame.Bytes.Length is 0 or > 8 * 1024 * 1024 || frame.MediaType is not ("image/png" or "image/jpeg"))
        { throw new InvalidDataException("TRANSLATION_IMAGE_INVALID"); }
        for (int attempt = 0; attempt < 2; attempt++)
        {
            string credential = await GetCredentialAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await new DeepSeekTranslationClient(http, () => credential, incident).AnalyzeAsync(mode, frame, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException error) when (error.Message is "TRANSLATION_KEY_INVALID" or "TRANSLATION_KEY_REJECTED")
            {
                Invalidate(credential);
                if (attempt == 1) { throw new InvalidDataException("TRANSLATION_BUILTIN_UNAVAILABLE"); }
            }
            catch (InvalidDataException error) when (error.Message == "TRANSLATION_BALANCE_REQUIRED")
            { throw new InvalidDataException("TRANSLATION_BUILTIN_UNAVAILABLE"); }
        }
        throw new InvalidDataException("TRANSLATION_BUILTIN_UNAVAILABLE");
    }

    private async Task<string> AcquireAsync(CancellationToken token)
    {
        Uri? origin = configuration.UpdateManifestUri ?? configuration.UsageHeartbeatUri;
        if (origin is null || origin.Scheme != Uri.UriSchemeHttps) { throw new InvalidDataException("TRANSLATION_BUILTIN_UNAVAILABLE"); }
        token.ThrowIfCancellationRequested();
        using var rsa = RSA.Create(3072);
        string nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, "/vrphonescreen/api/v1/translation/credential"))
        { Content = JsonContent.Create(new BuiltInCredentialRequest(1, Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()), nonce)) };
        request.Headers.CacheControl = new() { NoStore = true };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        if ((int)response.StatusCode == 429) { throw new InvalidDataException("TRANSLATION_RATE_LIMITED"); }
        if (!response.IsSuccessStatusCode) { throw new InvalidDataException("TRANSLATION_BUILTIN_UNAVAILABLE"); }
        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
        byte[] bytes = new byte[4097];
        int length = 0;
        while (length < bytes.Length)
        {
            int read = await stream.ReadAsync(bytes.AsMemory(length), deadline.Token).ConfigureAwait(false);
            if (read == 0) { break; }
            length += read;
        }
        try
        {
            if (length == bytes.Length) { throw new InvalidDataException("TRANSLATION_BUILTIN_UNAVAILABLE"); }
            var envelope = JsonSerializer.Deserialize<BuiltInCredentialResponse>(bytes.AsSpan(0, length), _json);
            if (envelope is null) { throw new InvalidDataException("TRANSLATION_BUILTIN_UNAVAILABLE"); }
            return BuiltInCredentialEnvelope.Open(envelope, rsa, nonce, DateTimeOffset.UtcNow);
        }
        catch (Exception error) when (error is JsonException or CryptographicException or FormatException)
        { throw new InvalidDataException("TRANSLATION_BUILTIN_UNAVAILABLE"); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
