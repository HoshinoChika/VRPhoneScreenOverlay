using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VRPhoneScreenOverlay.Protocols;

namespace VRPhoneScreenOverlay.Service;

public sealed record CloudReleaseAsset(string Path, long Size, string Sha256);
public sealed record CloudReleaseRecord(SignedUpdateManifest Manifest, CloudReleaseAsset Package);

public sealed class CloudReleaseCache : IDisposable
{
    private readonly ServiceOptions _options;
    private readonly ICloudStorage _cloud;
    private readonly TimeProvider _time;
    private readonly Func<string, CancellationToken, Task<CloudReleaseRecord?>>? _catalog;
    public CloudReleaseCache(ServiceOptions options, ICloudStorage cloud, TimeProvider time) : this(options, cloud, time, null) { }
    internal CloudReleaseCache(ServiceOptions options, ICloudStorage cloud, TimeProvider time,
        Func<string, CancellationToken, Task<CloudReleaseRecord?>>? catalog)
    { _options = options; _cloud = cloud; _time = time; _catalog = catalog; }
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private static readonly Regex _version = new(@"^\d+\.\d+\.\d+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$", RegexOptions.CultureInvariant);
    // Bounded cache: 64 assets, oldest-attempt eviction, owned by this service.
    // A single refresh gate coalesces requests; each asset has a one-minute retry floor.
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private readonly Queue<DateTimeOffset> _browserRefreshes = new(); // Capacity 6; one-minute window, protected by _refresh.
    private long _refreshes;
    private long _failures;
    public long Refreshes => Interlocked.Read(ref _refreshes);
    public long Failures => Interlocked.Read(ref _failures);

    public static bool ValidVersion(string value) => value.Length <= 64 && _version.IsMatch(value);

    public async Task<string?> ActiveVersionAsync(CancellationToken cancellationToken)
    {
        string path = Path.Combine(_options.DataRoot, "updates", "beta", "active.json");
        if (!File.Exists(path)) { return null; }
        using JsonDocument pointer = JsonDocument.Parse(await ReadBoundedAsync(path, cancellationToken));
        string? version = pointer.RootElement.GetProperty("version").GetString();
        return version is not null && ValidVersion(version) ? version : null;
    }

    public async Task<CloudReleaseRecord?> ReadReleaseAsync(string version, CancellationToken cancellationToken)
    {
        if (!ValidVersion(version)) { return null; }
        if (_catalog is not null) { return await _catalog(version, cancellationToken); }
        string path = Path.Combine(_options.DataRoot, "cloud", "releases", version, "release.json");
        if (!File.Exists(path)) { return null; }
        CloudReleaseRecord release = JsonSerializer.Deserialize<CloudReleaseRecord>(await ReadBoundedAsync(path, cancellationToken), _json)
            ?? throw new CloudTransferException("CLOUD_RELEASE_INVALID");
        byte[] payload = Convert.FromBase64String(release.Manifest.Payload);
        using Stream publicKey = typeof(CloudReleaseCache).Assembly.GetManifestResourceStream("VRPhoneScreenOverlay.Service.UpdatePublicKey")!;
        using StreamReader reader = new(publicKey);
        using ECDsa verifier = ECDsa.Create();
        verifier.ImportFromPem(await reader.ReadToEndAsync(cancellationToken));
        if (!verifier.VerifyData(payload, Convert.FromBase64String(release.Manifest.Signature), HashAlgorithmName.SHA256))
        { throw new CloudTransferException("CLOUD_RELEASE_SIGNATURE_INVALID"); }
        UpdateReleasePayload descriptor = JsonSerializer.Deserialize<UpdateReleasePayload>(payload, _json)!;
        if (release.Package is null || descriptor.SchemaVersion != UpdatePackageFormat.SchemaVersion || descriptor.PackageFormat != UpdatePackageFormat.FullInstall ||
            descriptor.Version != version || descriptor.Channel != "beta" || descriptor.PackageSize != release.Package.Size ||
            descriptor.PackageSha256 != release.Package.Sha256 || release.Package.Path != $"releases/{version}/package.zip" ||
            release.Package.Size is <= 0 or > 367001600)
        { throw new CloudTransferException("CLOUD_RELEASE_INVALID"); }
        return release;
    }

    public async Task<UpdateDownloadLease?> GetAsync(string version, bool force, CancellationToken cancellationToken, string? userAgent = null)
    {
        string agent = userAgent ?? OpenListCloudStorage.DownloadUserAgent;
        if (agent.Length > 512 || agent.Any(char.IsControl)) { return null; }
        bool browser = agent != OpenListCloudStorage.DownloadUserAgent;
        string key = version + "/package/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(agent)));
        DateTimeOffset now = _time.GetUtcNow();
        if (_entries.TryGetValue(key, out Entry? cached) && !force && cached.Link is { } ready && ready.ExpiresAt > now.AddMinutes(2))
        { return ready; }
        await _refresh.WaitAsync(cancellationToken);
        try
        {
            now = _time.GetUtcNow();
            _entries.TryGetValue(key, out cached);
            if (cached is not null && (cached.AttemptedAt > now.AddMinutes(-1) ||
                (!force && cached.Link is { } fresh && fresh.ExpiresAt > now.AddMinutes(2))))
            { return cached.Link is { } existing && existing.ExpiresAt > now.AddSeconds(30) ? existing : null; }
            CloudReleaseRecord? release = await ReadReleaseAsync(version, cancellationToken);
            if (release is null) { return null; }
            CloudReleaseAsset asset = release.Package;
            if (browser)
            {
                while (_browserRefreshes.TryPeek(out DateTimeOffset oldest) && oldest <= now.AddMinutes(-1)) { _browserRefreshes.Dequeue(); }
                if (_browserRefreshes.Count >= 6) { return cached?.Link is { } fallback && fallback.ExpiresAt > now.AddSeconds(30) ? fallback : null; }
                _browserRefreshes.Enqueue(now);
            }
            if (_entries.Count >= 64 && !_entries.ContainsKey(key))
            {
                string? victim = _entries.Where(pair => pair.Value.Browser).OrderBy(pair => pair.Value.AttemptedAt).FirstOrDefault().Key;
                _entries.TryRemove(victim ?? _entries.MinBy(pair => pair.Value.AttemptedAt).Key, out _);
            }
            _entries[key] = new(now, cached?.Link, browser);
            try
            {
                using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(25));
                CloudFileLink link = await _cloud.GetLinkAsync(asset.Path, asset.Size, deadline.Token, agent);
                UpdateDownloadLease lease = new(version, link.Url, link.ExpiresAt);
                _entries[key] = new(now, lease, browser);
                Interlocked.Increment(ref _refreshes);
                await PersistAsync(cancellationToken);
                return lease;
            }
            catch (Exception exception) when (exception is CloudTransferException or HttpRequestException or IOException or JsonException or OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref _failures);
                _entries[key] = new(now, cached?.Link, browser, exception is CloudTransferException failure ? failure.ReasonCode : "CLOUD_TRANSFER_FAILED");
                try { await PersistAsync(cancellationToken); } catch (IOException) { }
                return cached?.Link is { } previous && previous.ExpiresAt > now.AddSeconds(30) ? previous : null;
            }
        }
        finally { _refresh.Release(); }
    }

    public async Task RefreshCurrentAsync(CancellationToken cancellationToken)
    {
        string? version = await ActiveVersionAsync(cancellationToken);
        if (version is null) { return; }
        await GetAsync(version, false, cancellationToken);
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        string directory = Path.Combine(_options.DataRoot, "cloud"); Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "download-cache.json");
        await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(_entries, _json), cancellationToken);
        File.Move(path + ".tmp", path, true);
    }

    private static async Task<string> ReadBoundedAsync(string path, CancellationToken cancellationToken)
    {
        FileInfo file = new(path);
        if (file.Length > 1024 * 1024 || (file.Attributes & FileAttributes.ReparsePoint) != 0)
        { throw new CloudTransferException("CLOUD_RELEASE_INVALID"); }
        return await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
    }

    public void Dispose() => _refresh.Dispose();
    private sealed record Entry(DateTimeOffset AttemptedAt, UpdateDownloadLease? Link, bool Browser = false, string? Failure = null);
}

public sealed class CloudReleaseWorker(CloudReleaseCache cache, CloudStorageOptions options, ILogger<CloudReleaseWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> _failed = LoggerMessage.Define(LogLevel.Warning, new EventId(4200), "CLOUD_RELEASE_REFRESH_FAILED");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled) { return; }
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(60));
        try
        {
            do
            {
                try { await cache.RefreshCurrentAsync(stoppingToken); }
                catch (Exception exception) when (exception is IOException or JsonException or CloudTransferException or CryptographicException or FormatException)
                { _failed(logger, null); }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
