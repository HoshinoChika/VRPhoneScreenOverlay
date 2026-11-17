using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Network;

public sealed class VideoLinkResolver : IVideoLinkResolver, IDisposable
{
    private readonly HttpClient _http;
    private readonly SocketsHttpHandler _handler = CreateHandler();
    internal static SocketsHttpHandler CreateHandler() => new()
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    };
    private static readonly string[] _mediaExtensions = [".mp4", ".m3u8", ".webm", ".mov", ".m4v", ".flv"];
    private readonly HelloTikResolver _hello;
    public VideoLinkResolver()
    {
        _http = new(_handler, false)
        { Timeout = TimeSpan.FromSeconds(50) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0.0.0 Safari/537.36");
        _hello = new(_http);
    }
    public bool Supports(Uri source) => source.Scheme is "http" or "https" && string.IsNullOrEmpty(source.UserInfo) &&
        (IsDirect(source) || Host(source, "b23.tv") || Host(source, "bilibili.com") || Host(source, "douyin.com") || Host(source, "iesdouyin.com"));
    public async Task<string> ResolveAsync(Uri source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(source.UserInfo))
        { throw new InvalidDataException("VIDEO_URL_UNSUPPORTED"); }
        // A direct media URL is returned exactly as received, including signed query parameters.
        if (IsDirect(source)) { return source.OriginalString; }
        if (Host(source, "b23.tv") || Host(source, "bilibili.com"))
        {
            string url = await BilibiliAsync(source, cancellationToken).ConfigureAwait(false);
            return await _hello.CompatibleDownloadAsync(url, source.AbsoluteUri, cancellationToken).ConfigureAwait(false);
        }
        if (Host(source, "douyin.com") || Host(source, "iesdouyin.com"))
        { return await _hello.ResolveAsync(source, cancellationToken).ConfigureAwait(false); }
        throw new InvalidDataException("VIDEO_PLATFORM_UNSUPPORTED");
    }
    public static bool IsDirect(Uri uri) => _mediaExtensions
        .Any(extension => uri.AbsolutePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) ||
        Host(uri, "bilivideo.com") || Host(uri, "douyinvod.com") || uri.Host.Equals("data.hellotik.app", StringComparison.OrdinalIgnoreCase);
    internal static bool Host(Uri uri, string domain) => uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        uri.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);

    private async Task<string> BilibiliAsync(Uri source, CancellationToken token)
    {
        Uri page = source;
        if (Host(source, "b23.tv"))
        {
            for (int hop = 0; hop < 5 && Host(page, "b23.tv"); hop++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, page);
                using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                if ((int)response.StatusCode is < 300 or > 399 || response.Headers.Location is null)
                { throw new InvalidDataException("VIDEO_REDIRECT_UNSUPPORTED"); }
                page = new Uri(page, response.Headers.Location);
                if (page.Scheme is not ("http" or "https") || (!Host(page, "b23.tv") && !Host(page, "bilibili.com")))
                { throw new InvalidDataException("VIDEO_REDIRECT_UNSUPPORTED"); }
            }
        }
        if (!Host(page, "bilibili.com")) { throw new InvalidDataException("VIDEO_REDIRECT_UNSUPPORTED"); }
        string id = page.Segments.Select(item => item.Trim('/')).FirstOrDefault(item =>
            item.StartsWith("BV", StringComparison.Ordinal) && item.Length is >= 10 and <= 20 && item.All(char.IsAsciiLetterOrDigit)) ?? "";
        if (id.Length == 0) { throw new InvalidDataException("VIDEO_ID_MISSING"); }
        using JsonDocument view = await GetJsonAsync("https://api.bilibili.com/x/web-interface/view?bvid=" + id, token).ConfigureAwait(false);
        if (view.RootElement.GetProperty("code").GetInt32() != 0) { throw new InvalidDataException("VIDEO_UNAVAILABLE"); }
        JsonElement data = view.RootElement.GetProperty("data");
        long cid = data.GetProperty("cid").GetInt64();
        foreach (string part in page.Query.TrimStart('?').Split('&'))
        {
            string[] pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0] == "p" && int.TryParse(pair[1], out int index) && index > 0)
            {
                JsonElement pages = data.GetProperty("pages");
                if (index > pages.GetArrayLength()) { throw new InvalidDataException("VIDEO_PART_UNAVAILABLE"); }
                cid = pages[index - 1].GetProperty("cid").GetInt64();
            }
        }
        using JsonDocument play = await GetJsonAsync(FormattableString.Invariant($"https://api.bilibili.com/x/player/playurl?bvid={id}&cid={cid}&platform=html5&high_quality=0&qn=64&fnval=0"), token).ConfigureAwait(false);
        if (play.RootElement.GetProperty("code").GetInt32() != 0) { throw new InvalidDataException("VIDEO_UNAVAILABLE"); }
        JsonElement entries = play.RootElement.GetProperty("data").GetProperty("durl");
        if (entries.GetArrayLength() != 1) { throw new InvalidDataException("VIDEO_SINGLE_STREAM_UNAVAILABLE"); }
        string url = entries[0].GetProperty("url").GetString() ?? "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? media) || !Host(media, "bilivideo.com"))
        { throw new InvalidDataException("VIDEO_MEDIA_INVALID"); }
        return url;
    }
    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Referrer = new("https://www.bilibili.com/");
        return await VideoResolverHttp.JsonAsync(_http, request, token).ConfigureAwait(false);
    }
    public void Dispose() { _hello.Dispose(); _http.Dispose(); _handler.Dispose(); }
}

internal static class VideoResolverHttp
{
    internal static async Task<string> TextAsync(HttpClient http, HttpRequestMessage request, CancellationToken token)
    {
        using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if ((int)response.StatusCode is 538 or 539 or 541) { throw new InvalidOperationException("VIDEO_WEBSITE_USER_ACTION"); }
        if (response.StatusCode == HttpStatusCode.TooManyRequests) { throw new InvalidOperationException("VIDEO_RATE_LIMITED"); }
        if (!response.IsSuccessStatusCode) { throw new InvalidDataException("VIDEO_SERVICE_REJECTED"); }
        await response.Content.LoadIntoBufferAsync(2 * 1024 * 1024, token).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
    }
    internal static async Task<JsonDocument> JsonAsync(HttpClient http, HttpRequestMessage request, CancellationToken token) =>
        JsonDocument.Parse(await TextAsync(http, request, token).ConfigureAwait(false));
    internal static HttpRequestMessage Post(string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://www.hellotik.app" + path)
        { Content = JsonContent.Create(body) };
        request.Headers.Referrer = new("https://www.hellotik.app/zh");
        request.Headers.Add("Origin", "https://www.hellotik.app");
        return request;
    }
}
