using System.Security.Cryptography;
using System.Text.Json;

namespace VRPhoneScreenOverlay.Service;

public sealed record CloudFileLink(string Url, DateTimeOffset ExpiresAt, long Size);

public interface ICloudStorage
{
    public Task<CloudFileLink> GetLinkAsync(string path, long size, CancellationToken cancellationToken, string? userAgent = null);
    public Task UploadArchiveAsync(string source, string destination, CancellationToken cancellationToken);
}

// OpenList remains loopback-only. Its 115 credentials never reach the public API.
public sealed class OpenListCloudStorage : ICloudStorage, IDisposable
{
    public const string DownloadUserAgent = "VRPhoneScreenOverlay/update-client";
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly CloudStorageOptions _options;
    private readonly HttpClient _http;
    private readonly TimeProvider _time;

    public OpenListCloudStorage(CloudStorageOptions options, TimeProvider time)
        : this(options, time, CreateClient(options)) { }

    internal OpenListCloudStorage(CloudStorageOptions options, TimeProvider time, HttpClient http)
    {
        _options = options; _time = time; _http = http;
        if (options.Enabled && (!options.Endpoint!.IsLoopback || options.Endpoint.Scheme != "http"))
        { throw new ArgumentException("OpenList must use a loopback HTTP endpoint", nameof(options)); }
    }

    private static HttpClient CreateClient(CloudStorageOptions options)
    {
        if (options.Enabled && (!options.Endpoint!.IsLoopback || options.Endpoint.Scheme != "http"))
        { throw new ArgumentException("OpenList must use a loopback HTTP endpoint", nameof(options)); }
        SocketsHttpHandler? handler = new() { UseProxy = false, UseCookies = false };
        try
        {
            HttpClient client = new(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
            handler = null; // Ownership has transferred to HttpClient.
            return client;
        }
        finally { handler?.Dispose(); }
    }

    public async Task<CloudFileLink> GetLinkAsync(string path, long size, CancellationToken cancellationToken, string? userAgent = null)
    {
        using JsonDocument data = await CallAsync("get", new { path = FullPath(path), password = "" }, cancellationToken, userAgent: userAgent);
        JsonElement item = data.RootElement.GetProperty("data");
        string? raw = item.GetProperty("raw_url").GetString();
        if (!IsDownloadUrl(raw) || item.GetProperty("size").GetInt64() != size)
        { throw new CloudTransferException("CLOUD_LINK_INVALID"); }
        if (item.TryGetProperty("header", out JsonElement headers) && headers.ValueKind == JsonValueKind.Object &&
            headers.EnumerateObject().Any(h => !h.Name.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)))
        { throw new CloudTransferException("CLOUD_LINK_REQUIRES_PRIVATE_HEADERS"); }
        DateTimeOffset expiry = ReadExpiry(new Uri(raw!), _time.GetUtcNow());
        // Public downloads are validated by the publisher/client. Keeping this service's
        // traffic on loopback preserves its existing systemd network isolation.
        return new(raw!, expiry, size);
    }

    internal static bool IsDownloadUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
        VRPhoneScreenOverlay.Protocols.UpdateDownloadAddressPolicy.IsAllowed(uri);

    internal static DateTimeOffset ReadExpiry(Uri uri, DateTimeOffset now)
    {
        foreach (string field in uri.Query.TrimStart('?').Split('&'))
        {
            string[] pair = field.Split('=', 2);
            if (pair.Length == 2 && pair[0] == "t" && long.TryParse(pair[1], out long seconds) && seconds is > 0 and < 253402300799)
            {
                DateTimeOffset expiry = DateTimeOffset.FromUnixTimeSeconds(seconds);
                if (expiry > now.AddMinutes(2)) { return expiry < now.AddHours(24) ? expiry : now.AddHours(24); }
                throw new CloudTransferException("CLOUD_LINK_EXPIRED");
            }
        }
        // Unknown provider lifetime: revalidate conservatively, never assume a permanent link.
        return now.AddMinutes(15);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350", Justification = "115 exposes SHA1 for non-security upload integrity; signed software updates and diagnostic contents remain verified with SHA256. See docs/plans/CLOUD_RELEASES.md.")]
    public async Task UploadArchiveAsync(string source, string destination, CancellationToken cancellationToken)
    {
        string path = FullPath(destination);
        string[] pieces = destination.Split('/');
        for (int index = 1; index < pieces.Length; index++)
        {
            using JsonDocument ignored = await CallAsync("mkdir", new { path = FullPath(string.Join('/', pieces.Take(index))) }, cancellationToken);
        }
        FileInfo file = new(source);
        await using FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        byte[] digest = await SHA1.HashDataAsync(input, cancellationToken); // Provider exposes SHA1 for upload integrity, not authentication.
        string sha1 = Convert.ToHexString(digest); input.Position = 0;
        if (await MatchesAsync(destination, file.Length, sha1, cancellationToken)) { return; }
        using HttpRequestMessage request = await RequestAsync("put", cancellationToken);
        request.Method = HttpMethod.Put;
        request.Headers.Add("File-Path", Uri.EscapeDataString(path));
        request.Headers.Add("As-Task", "false"); request.Headers.Add("Overwrite", "false");
        request.Headers.Add("X-File-Sha1", sha1);
        request.Content = new StreamContent(input); request.Content.Headers.ContentLength = file.Length;
        using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        using JsonDocument uploaded = await ReadAsync(response, cancellationToken);
        using JsonDocument refreshed = await CallAsync("list", new { path = FullPath(string.Join('/', pieces.SkipLast(1))), page = 1, per_page = 1, refresh = true }, cancellationToken);
        if (!await MatchesAsync(destination, file.Length, sha1, cancellationToken))
        { throw new CloudTransferException("CLOUD_ARCHIVE_VERIFY_FAILED"); }
    }

    private async Task<bool> MatchesAsync(string path, long size, string sha1, CancellationToken cancellationToken)
    {
        using JsonDocument result = await CallAsync("get", new { path = FullPath(path), password = "" }, cancellationToken, allowMissing: true);
        if (result.RootElement.GetProperty("code").GetInt32() != 200) { return false; }
        JsonElement data = result.RootElement.GetProperty("data");
        bool match = data.GetProperty("size").GetInt64() == size && MatchesProviderHash(data, sha1);
        if (!match) { throw new CloudTransferException("CLOUD_ARCHIVE_CONFLICT_OR_HASH_UNAVAILABLE"); }
        return true;
    }

    internal static bool MatchesProviderHash(JsonElement data, string sha1)
    {
        if (!data.TryGetProperty("hashinfo", out JsonElement hashes)) { return false; }
        if (hashes.ValueKind == JsonValueKind.String)
        {
            string encoded = hashes.GetString()!;
            if (encoded.Length > 2048) { return false; }
            using JsonDocument parsed = JsonDocument.Parse(encoded);
            return HashObjectMatches(parsed.RootElement, sha1);
        }
        return HashObjectMatches(hashes, sha1);
    }

    private static bool HashObjectMatches(JsonElement hashes, string sha1) => hashes.ValueKind == JsonValueKind.Object &&
        hashes.EnumerateObject().Any(p => p.Name.Equals("sha1", StringComparison.OrdinalIgnoreCase) &&
            p.Value.ValueKind == JsonValueKind.String && string.Equals(p.Value.GetString(), sha1, StringComparison.OrdinalIgnoreCase));

    private string FullPath(string relative)
    {
        if (relative.Length is 0 or > 256 || relative.Split('/').Any(p => p.Length == 0 || p is "." or ".." || p.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-' and not '_')))
        { throw new CloudTransferException("CLOUD_PATH_INVALID"); }
        return _options.MountPath.TrimEnd('/') + "/" + relative;
    }

    private async Task<HttpRequestMessage> RequestAsync(string method, CancellationToken cancellationToken, string? userAgent = null)
    {
        if (!_options.Enabled) { throw new CloudTransferException("CLOUD_NOT_CONFIGURED"); }
        HttpRequestMessage request = new(HttpMethod.Post, new Uri(_options.Endpoint!, "api/fs/" + method));
        try
        {
            string token = (await File.ReadAllTextAsync(_options.TokenFile!, cancellationToken)).Trim();
            request.Headers.Add("Authorization", token);
            string agent = userAgent ?? DownloadUserAgent;
            if (agent.Length > 512 || agent.Any(char.IsControl)) { throw new CloudTransferException("CLOUD_AGENT_INVALID"); }
            if (agent.Length > 0) { request.Headers.TryAddWithoutValidation("User-Agent", agent); }
            return request;
        }
        catch { request.Dispose(); throw; }
    }

    private async Task<JsonDocument> CallAsync(string method, object body, CancellationToken cancellationToken, bool allowMissing = false, string? userAgent = null)
    {
        using HttpRequestMessage request = await RequestAsync(method, cancellationToken, userAgent);
        request.Content = JsonContent.Create(body, options: _json);
        using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return await ReadAsync(response, cancellationToken, allowMissing);
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken, bool allowMissing = false)
    {
        if (!response.IsSuccessStatusCode) { throw new CloudTransferException("CLOUD_HTTP_FAILED"); }
        await response.Content.LoadIntoBufferAsync(256 * 1024, cancellationToken);
        JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancellationToken));
        int code = document.RootElement.GetProperty("code").GetInt32();
        if (code != 200 && !(allowMissing && code is 404 or 500))
        { document.Dispose(); throw new CloudTransferException("CLOUD_API_FAILED"); }
        return document;
    }

    public void Dispose() => _http.Dispose();
}
