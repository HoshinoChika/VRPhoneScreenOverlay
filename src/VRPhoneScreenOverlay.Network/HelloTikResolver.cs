using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VRPhoneScreenOverlay.Network;

/// <summary>Normal website ticket flow. No paid confirmation, challenge bypass, or automatic retry.</summary>
internal sealed partial class HelloTikResolver(HttpClient http) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Usage? _usage;
    private JsonElement? _profile;
    private DateTimeOffset _profileTime;
    private readonly string _usagePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VRPhoneScreenOverlay", "video-resolver-usage.json");

    public async Task<string> CompatibleDownloadAsync(string media, string source, CancellationToken token)
    {
        using var request = VideoResolverHttp.Post("/api/download-proxy-url", new { url = media, sourceUrl = source, type = "video" });
        using JsonDocument result = await VideoResolverHttp.JsonAsync(http, request, token).ConfigureAwait(false);
        string url = result.RootElement.GetProperty("url").GetString() ?? "";
        return ValidateMedia(url, "hellotik.app");
    }

    public async Task<string> ResolveAsync(Uri source, CancellationToken token)
    {
        if (!await _gate.WaitAsync(0, token).ConfigureAwait(false)) { throw new InvalidOperationException("VIDEO_RESOLVER_BUSY"); }
        try
        {
            await LoadUsageAsync(token).ConfigureAwait(false);
            Usage usage = _usage!;
            int limit = usage.Total < 30 ? 10 : usage.Total < 80 ? 6 : 5;
            if (usage.Daily >= limit) { throw new InvalidOperationException("VIDEO_WEBSITE_QUOTA"); }
            JsonElement profile = await ProfileAsync(token).ConfigureAwait(false);
            var payload = new Dictionary<string, object?>
            {
                ["requestURL"] = source.AbsoluteUri,
                ["isMobile"] = "false",
                ["isoCode"] = "CN",
                ["adType"] = "adsense",
                ["uwx_id"] = usage.Id,
                ["pagePath"] = "/zh",
                ["externalRequestId"] = Guid.NewGuid().ToString(),
                ["successCount"] = usage.Daily.ToString(CultureInfo.InvariantCulture),
                ["totalSuccessCount"] = usage.Total.ToString(CultureInfo.InvariantCulture),
                ["firstSuccessDate"] = usage.FirstDate,
                ["geoipIp"] = "",
                ["isBatch"] = false,
                ["browserQuota"] = new { rewardRequired = false, dailyUsed = usage.Daily, dailyLimit = limit, totalSuccessCount = usage.Total },
            };
            using (var quoteRequest = VideoResolverHttp.Post("/api/parse-entitlement-status", payload))
            using (JsonDocument quote = await VideoResolverHttp.JsonAsync(http, quoteRequest, token).ConfigureAwait(false))
            {
                JsonElement data = quote.RootElement.GetProperty("data");
                string tier = data.GetProperty("defaultTier").GetString() ?? "hd";
                if (data.GetProperty("showQuote").GetBoolean() || data.GetProperty("qualitySelectable").GetBoolean() ||
                    data.GetProperty("tiers").GetProperty(tier).GetProperty("chargeCost").GetDecimal() != 0)
                { throw new InvalidOperationException("VIDEO_WEBSITE_USER_ACTION"); }
                payload["quoteToken"] = quote.RootElement.GetProperty("quoteToken").GetString();
                payload["qualityTier"] = tier;
            }
            string route = profile.GetProperty("authRoute").GetString() ?? "";
            if (!SafeField(route)) { throw new InvalidDataException("VIDEO_WEBSITE_CHANGED"); }
            using var ticketRequest = VideoResolverHttp.Post("/api/" + route, new { requestURL = source.AbsoluteUri, isBatch = false, mode = "single" });
            using JsonDocument ticketDocument = await VideoResolverHttp.JsonAsync(http, ticketRequest, token).ConfigureAwait(false);
            JsonElement fields = profile.GetProperty("ticketResponseFields");
            string ticket = ticketDocument.RootElement.GetProperty(Field(fields, "key")).GetString() ?? "";
            string seed = ticketDocument.RootElement.GetProperty(Field(fields, "seed")).GetString() ?? "";
            byte[] key = SHA256.HashData(Encoding.UTF8.GetBytes(ticket + ":" + seed));
            byte[] nonce = RandomNumberGenerator.GetBytes(12);
            byte[] clear = JsonSerializer.SerializeToUtf8Bytes(payload);
            byte[] cipher = new byte[clear.Length];
            byte[] tag = new byte[16];
            try { using var aes = new AesGcm(key, 16); aes.Encrypt(nonce, clear, cipher, tag); }
            finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(clear); }
            byte[] combined = new byte[cipher.Length + tag.Length];
            cipher.CopyTo(combined, 0); tag.CopyTo(combined, cipher.Length);
            JsonElement requestFields = profile.GetProperty("parseRequestFields");
            var body = new Dictionary<string, object>
            {
                [Field(requestFields, "key")] = ticket,
                [Field(requestFields, "payload")] = Convert.ToBase64String(combined),
                [Field(requestFields, "iv")] = Convert.ToBase64String(nonce),
                [Field(requestFields, "version")] = 1,
            };
            using var parseRequest = VideoResolverHttp.Post("/api/parse", body);
            using JsonDocument response = await VideoResolverHttp.JsonAsync(http, parseRequest, token).ConfigureAwait(false);
            JsonElement result = response.RootElement;
            if (!result.TryGetProperty("status", out JsonElement status) || status.GetInt32() != 0)
            { throw new InvalidDataException("VIDEO_PARSE_FAILED"); }
            _usage = usage with { Daily = usage.Daily + 1, Total = usage.Total + 1, FirstDate = usage.FirstDate ?? usage.Date };
            await SaveUsageAsync(token).ConfigureAwait(false);
            using JsonDocument decoded = result.TryGetProperty("encrypt", out JsonElement encrypted) && encrypted.GetBoolean()
                ? DecodeResponse(result.GetProperty("data").GetString()!, result.GetProperty("key").GetString()!)
                : JsonDocument.Parse(result.GetProperty("data").GetRawText());
            return ValidateMedia(decoded.RootElement.GetProperty("url").GetString() ?? "", "douyinvod.com");
        }
        catch { _profile = null; throw; }
        finally { _gate.Release(); }
    }

    private async Task<JsonElement> ProfileAsync(CancellationToken token)
    {
        if (_profile is { } cached && DateTimeOffset.UtcNow - _profileTime < TimeSpan.FromMinutes(20)) { return cached; }
        using var pageRequest = new HttpRequestMessage(HttpMethod.Get, "https://www.hellotik.app/zh");
        string page = await VideoResolverHttp.TextAsync(http, pageRequest, token).ConfigureAwait(false);
        foreach (Match match in ScriptPattern().Matches(page).Take(24))
        {
            string path = match.Groups[1].Value;
            using var scriptRequest = new HttpRequestMessage(HttpMethod.Get, "https://www.hellotik.app" + path);
            string script = await VideoResolverHttp.TextAsync(http, scriptRequest, token).ConfigureAwait(false);
            const string marker = "JSON.parse('{\"activeProfileId\"";
            int start = script.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) { continue; }
            start += "JSON.parse('".Length;
            int end = script.IndexOf("')", start, StringComparison.Ordinal);
            if (end < start || end - start > 65536) { throw new InvalidDataException("VIDEO_WEBSITE_CHANGED"); }
            using JsonDocument config = JsonDocument.Parse(script[start..end]);
            string? active = config.RootElement.GetProperty("activeProfileId").GetString();
            foreach (JsonElement candidate in config.RootElement.GetProperty("profiles").EnumerateArray())
            {
                if (candidate.GetProperty("id").GetString() != active) { continue; }
                _profile = candidate.Clone(); _profileTime = DateTimeOffset.UtcNow; return _profile.Value;
            }
        }
        throw new InvalidDataException("VIDEO_WEBSITE_CHANGED");
    }

    internal static JsonDocument DecodeResponse(string data, string iv)
    {
        using Aes aes = Aes.Create();
        // Public website response format, not an account credential.
        aes.Key = Encoding.UTF8.GetBytes("93838338562359368888868323563256");
        byte[] clear = aes.DecryptCbc(Unpack(data), Unpack(iv));
        try { return JsonDocument.Parse(Encoding.UTF8.GetString(clear)); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    private static byte[] Unpack(string value)
    {
        byte[] bytes = Convert.FromBase64String(value);
        for (int i = 0; i < bytes.Length; i++) { bytes[i] ^= 90; }
        for (int i = 0; i < bytes.Length; i += 8) { Array.Reverse(bytes, i, Math.Min(8, bytes.Length - i)); }
        const string from = "ZYXABCDEFGHIJKLMNOPQRSTUVWzyxabcdefghijklmnopqrstuvw9876543210-_";
        const string to = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        for (int i = 0; i < bytes.Length; i++)
        {
            int index = from.IndexOf((char)bytes[i], StringComparison.Ordinal);
            if (index >= 0) { bytes[i] = (byte)to[index]; }
        }
        return Convert.FromBase64String(Encoding.ASCII.GetString(bytes));
    }
    private static string ValidateMedia(string url, string domain)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != "https" ||
            !string.IsNullOrEmpty(uri.UserInfo) || !VideoLinkResolver.Host(uri, domain))
        { throw new InvalidDataException("VIDEO_MEDIA_INVALID"); }
        return url;
    }
    private static bool SafeField(string text) => text.Length is > 0 and < 80 && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    private static string Field(JsonElement fields, string name)
    {
        string value = fields.GetProperty(name).GetString() ?? "";
        return SafeField(value) ? value : throw new InvalidDataException("VIDEO_WEBSITE_CHANGED");
    }
    private async Task LoadUsageAsync(CancellationToken token)
    {
        string today = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (_usage is null)
        {
            _usage = File.Exists(_usagePath) ? JsonSerializer.Deserialize<Usage>(await File.ReadAllTextAsync(_usagePath, token).ConfigureAwait(false))
                ?? throw new InvalidDataException("VIDEO_USAGE_INVALID") : new(Guid.NewGuid().ToString(), today, 0, 0, null);
        }
        if (_usage.Daily < 0 || _usage.Total < 0) { throw new InvalidDataException("VIDEO_USAGE_INVALID"); }
        if (_usage.Date != today) { _usage = _usage with { Date = today, Daily = 0 }; }
    }
    private async Task SaveUsageAsync(CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_usagePath)!);
        string temp = _usagePath + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(_usage), token).ConfigureAwait(false);
        File.Move(temp, _usagePath, true);
    }
    private sealed record Usage(string Id, string Date, int Daily, int Total, string? FirstDate);
    [GeneratedRegex("<script[^>]+src=\"(/_next/static/chunks/[^\"]+\\.js)\"", RegexOptions.None, 100)]
    private static partial Regex ScriptPattern();
    public void Dispose() => _gate.Dispose();
}
