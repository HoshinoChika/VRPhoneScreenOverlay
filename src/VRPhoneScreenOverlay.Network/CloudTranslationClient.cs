using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.Protocols;

namespace VRPhoneScreenOverlay.Network;

/// <summary>Only metadata reaches VPS. Image PUTs carry no installation or provider credential.</summary>
public sealed class CloudTranslationClient(HttpClient http, ClientServiceConfiguration configuration,
    Func<CancellationToken, Task<string>> getCredential) : IProgressTranslationModelClient
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public Task<TranslationRecord> AnalyzeAsync(TranslationMode mode, TranslationFrame frame, CancellationToken cancellationToken) =>
        AnalyzeAsync(mode, frame, null, cancellationToken);

    public async Task<TranslationRecord> AnalyzeAsync(TranslationMode mode, TranslationFrame frame,
        IProgress<TranslationProgress>? progress, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(mode) || frame.Bytes.Length is 0 or > 8 * 1024 * 1024 || frame.MediaType is not ("image/png" or "image/jpeg"))
        { throw new InvalidDataException("TRANSLATION_IMAGE_INVALID"); }
        Uri? origin = configuration.UpdateManifestUri ?? configuration.UsageHeartbeatUri;
        if (origin is null || origin.Scheme != "https") { throw new InvalidDataException("TRANSLATION_BUILTIN_UNAVAILABLE"); }
        Uri endpoint = new(origin, "/vrphonescreen/api/v1/translation/jobs");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(4));
        string identity = await getCredential(deadline.Token).ConfigureAwait(false);
        Guid? id = null;
        bool completed = false;
        try
        {
            // MD5 is the provider's required content fingerprint, not an authentication primitive.
#pragma warning disable CA5351 // 123 OSS requires MD5; SHA256 is separately carried and no security decision relies on MD5 alone.
            var create = new TranslationJobCreateRequest(1, Guid.NewGuid(), (int)mode, frame.MediaType, frame.Bytes.Length,
                Convert.ToHexStringLower(MD5.HashData(frame.Bytes)), Convert.ToHexStringLower(SHA256.HashData(frame.Bytes)), frame.CapturedAt);
#pragma warning restore CA5351
            progress?.Report(new(TranslationPhase.PreparingUpload));
            TranslationJobCreated upload = await CallAsync<TranslationJobCreated>(HttpMethod.Post, endpoint, identity, create, deadline.Token).ConfigureAwait(false);
            id = upload.JobId;
            if (upload.JobId == Guid.Empty || upload.Parts is null || upload.Parts.Count > 64 || upload.ExpiresAt <= DateTimeOffset.UtcNow)
            { throw new InvalidDataException("TRANSLATION_UPLOAD_INVALID"); }
            long offset = 0;
            int partNumber = 0;
            if (upload.Parts.Count > 0) { progress?.Report(new(TranslationPhase.Uploading)); }
            foreach (TranslationUploadPart part in upload.Parts)
            {
                if (part is null || part.Number != ++partNumber || part.Offset != offset || part.Length <= 0 ||
                    part.Length > frame.Bytes.Length - offset || !ValidUploadUri(part.Url))
                { throw new InvalidDataException("TRANSLATION_UPLOAD_INVALID"); }
                using var put = new HttpRequestMessage(HttpMethod.Put, part.Url)
                { Content = new ByteArrayContent(frame.Bytes, (int)part.Offset, part.Length) };
                put.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                using var uploadDeadline = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                uploadDeadline.CancelAfter(TimeSpan.FromSeconds(90));
                using HttpResponseMessage response = await http.SendAsync(put, HttpCompletionOption.ResponseHeadersRead, uploadDeadline.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) { throw new InvalidDataException("TRANSLATION_UPLOAD_FAILED"); }
                offset += part.Length;
            }
            if (upload.Parts.Count > 0 && offset != frame.Bytes.Length) { throw new InvalidDataException("TRANSLATION_UPLOAD_INVALID"); }
            Uri jobUri = new(endpoint.AbsoluteUri + "/" + upload.JobId.ToString("D"));
            progress?.Report(new(TranslationPhase.ConfirmingUpload));
            TranslationJobStatus status = await CallAsync<TranslationJobStatus>(HttpMethod.Post, new(jobUri.AbsoluteUri + "/complete"), identity, null, deadline.Token).ConfigureAwait(false);
            bool translationReported = false;
            while (true)
            {
                if (!translationReported && status.Phase is TranslationJobPhase.Uploaded or TranslationJobPhase.Recognizing)
                {
                    // Includes server admission and model work; this is a phase, not a percentage.
                    progress?.Report(new(TranslationPhase.Translating));
                    translationReported = true;
                }
                if (status.JobId != upload.JobId) { throw new InvalidDataException("TRANSLATION_RESPONSE_INVALID"); }
                if (status.Phase == TranslationJobPhase.Completed && status.Result is { } result)
                {
                    if (string.IsNullOrWhiteSpace(result.Title) || result.Title.Length > 120 || string.IsNullOrWhiteSpace(result.Text) || result.Text.Length > 16000)
                    { throw new InvalidDataException("TRANSLATION_RESPONSE_INVALID"); }
                    completed = true;
                    return new(upload.JobId, frame.CapturedAt, mode, result.Title, result.Text);
                }
                if (status.Phase is TranslationJobPhase.Failed or TranslationJobPhase.Cancelled or TranslationJobPhase.Expired)
                { throw new InvalidDataException("TRANSLATION_API_FAILED"); }
                bool uploading = status.Phase == TranslationJobPhase.Uploading;
                bool waitForResult = status.Phase is TranslationJobPhase.Uploaded or TranslationJobPhase.Recognizing && status.SupportsResultWait;
                if (!waitForResult) { await Task.Delay(TimeSpan.FromSeconds(1), deadline.Token).ConfigureAwait(false); }
                status = await CallAsync<TranslationJobStatus>(uploading ? HttpMethod.Post : HttpMethod.Get,
                    uploading ? new(jobUri.AbsoluteUri + "/complete") : waitForResult ? new(jobUri.AbsoluteUri + "?wait=true") : jobUri, identity, null, deadline.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            if (id is not null && !completed)
            {
                using var cleanupDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var cancel = new HttpRequestMessage(HttpMethod.Delete, new Uri(endpoint.AbsoluteUri + "/" + id.Value.ToString("D")));
                cancel.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity);
                try { using HttpResponseMessage response = await http.SendAsync(cancel, HttpCompletionOption.ResponseHeadersRead, cleanupDeadline.Token).ConfigureAwait(false); }
                catch (Exception error) when (error is HttpRequestException or OperationCanceledException) { /* Server expiry owns eventual cleanup. */ }
            }
        }
    }

    private async Task<T> CallAsync<T>(HttpMethod method, Uri uri, string identity, object? body, CancellationToken token)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { return await CallOnceAsync<T>(method, uri, identity, body, token).ConfigureAwait(false); }
            catch (BusyException error)
            {
                if (attempt >= 3) { throw new InvalidDataException("TRANSLATION_RATE_LIMITED"); }
                await Task.Delay(error.Delay, token).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt < 3)
            { await Task.Delay(TimeSpan.FromMilliseconds(500 * (attempt + 1)), token).ConfigureAwait(false); }
        }
    }

    private async Task<T> CallOnceAsync<T>(HttpMethod method, Uri uri, string identity, object? body, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity);
        if (body is not null) { request.Content = JsonContent.Create(body); }
        using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        if ((int)response.StatusCode is 429 or 503)
        {
            TimeSpan wait = response.Headers.RetryAfter?.Delta ??
                (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromSeconds(2);
            if (wait > TimeSpan.FromSeconds(30)) { throw new InvalidDataException("TRANSLATION_RATE_LIMITED"); }
            throw new BusyException(wait < TimeSpan.Zero ? TimeSpan.Zero : wait);
        }
        if (!response.IsSuccessStatusCode) { throw new InvalidDataException("TRANSLATION_BUILTIN_UNAVAILABLE"); }
        await using Stream stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
        byte[] bytes = new byte[128 * 1024 + 1];
        int length = 0;
        while (length < bytes.Length)
        {
            int read = await stream.ReadAsync(bytes.AsMemory(length), deadline.Token).ConfigureAwait(false);
            if (read == 0) { break; }
            length += read;
        }
        if (length == bytes.Length) { throw new InvalidDataException("TRANSLATION_RESPONSE_TOO_LARGE"); }
        try { return JsonSerializer.Deserialize<T>(bytes.AsSpan(0, length), _json) ?? throw new InvalidDataException("TRANSLATION_RESPONSE_INVALID"); }
        catch (JsonException) { throw new InvalidDataException("TRANSLATION_RESPONSE_INVALID"); }
    }

    private static bool ValidUploadUri(string url) => Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) &&
        uri.Scheme == "https" && uri.Port == 443 && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 &&
        uri.Host.EndsWith(".123624.com", StringComparison.OrdinalIgnoreCase);

    private sealed class BusyException(TimeSpan delay) : Exception
    {
        public TimeSpan Delay { get; } = delay;
    }
}
