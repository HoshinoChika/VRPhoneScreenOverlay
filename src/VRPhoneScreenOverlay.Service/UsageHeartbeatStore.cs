using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VRPhoneScreenOverlay.Protocols;

namespace VRPhoneScreenOverlay.Service;

/// <summary>Bounded latest-seen index; atomic minute snapshots, no event/IP/device history.</summary>
public sealed class UsageHeartbeatStore : IDisposable
{
    public const int MaximumInstallations = 250_000;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly Dictionary<string, UsageSeen> _users = new(StringComparer.Ordinal);
    private readonly string _path;
    private readonly TimeProvider _time;
    private long _generation;
    private long _savedGeneration;
    private readonly UsageRegistrationGate _registrations;
    public bool IsAvailable { get; private set; } = true;
    public string ReasonCode { get; private set; } = "USAGE_READY";
    public int RegisteredCount { get { lock (_sync) { return _users.Count; } } }

    public UsageHeartbeatStore(ServiceOptions options) : this(options, TimeProvider.System) { }

    internal UsageHeartbeatStore(ServiceOptions options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _path = Path.Combine(options.DataRoot, "usage", "installations.json");
        _time = timeProvider;
        _registrations = new UsageRegistrationGate(timeProvider);
        try { Load(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            _users.Clear();
            IsAvailable = false;
            ReasonCode = "USAGE_STORE_UNAVAILABLE";
        }
    }

    private void Load()
    {
        // Refuse a corrupt/oversized store instead of silently resetting lifetime totals.
        if (!File.Exists(_path)) { return; }
        if (new FileInfo(_path).Length > 64 * 1024 * 1024)
        { throw new InvalidDataException("Usage store exceeds its limit."); }
        using FileStream input = File.OpenRead(_path);
        UsageSeen[] entries = JsonSerializer.Deserialize<UsageSeen[]>(input)
            ?? throw new InvalidDataException("Invalid usage store.");
        if (entries.Length > MaximumInstallations) { throw new InvalidDataException("Too many usage identities."); }
        foreach (UsageSeen entry in entries)
        {
            if (entry is null || string.IsNullOrEmpty(entry.Key) || entry.Key.Length != 64 || !entry.Key.All(char.IsAsciiHexDigit) ||
                entry.FirstSeen > entry.LastSeen || !_users.TryAdd(entry.Key, entry))
            { throw new InvalidDataException("Invalid usage entry."); }
        }
    }

    public int Record(UsageHeartbeatRequest request, string address = "local")
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsAvailable) { return StatusCodes.Status503ServiceUnavailable; }
        if (request.SchemaVersion != 1 ||
            !Guid.TryParseExact(request.InstallationId, "N", out Guid id) || id == Guid.Empty)
        { return StatusCodes.Status400BadRequest; }
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id.ToString("N"))));
        DateTimeOffset now = _time.GetUtcNow();
        lock (_sync)
        {
            if (_users.TryGetValue(key, out UsageSeen? existing))
            {
                if (now <= existing.LastSeen) { return StatusCodes.Status204NoContent; }
                _users[key] = existing with { LastSeen = now };
            }
            else
            {
                if (_users.Count >= MaximumInstallations) { return StatusCodes.Status503ServiceUnavailable; }
                if (!_registrations.TryRegister(address)) { return StatusCodes.Status429TooManyRequests; }
                _users.Add(key, new UsageSeen(key, now, now));
            }
            _generation++;
        }
        return StatusCodes.Status204NoContent;
    }

    public UsageStatistics GetStatistics()
    {
        DateTimeOffset now = _time.GetUtcNow();
        lock (_sync)
        {
            return new UsageStatistics(_users.Count,
                _users.Values.Count(item => item.LastSeen > now.AddDays(-1)),
                _users.Values.Count(item => item.LastSeen > now.AddDays(-7)),
                _users.Values.Count(item => item.LastSeen > now.AddDays(-30)),
                _users.Values.Count(item => item.LastSeen > now.AddSeconds(-180)), now, 180);
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (!IsAvailable) { return; }
        await _saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        string temporary = _path + ".tmp";
        try
        {
            UsageSeen[] snapshot;
            long generation;
            lock (_sync)
            {
                generation = _generation;
                if (generation == _savedGeneration) { return; }
                snapshot = _users.Values.ToArray();
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await using (FileStream output = new(temporary, FileMode.Create, FileAccess.Write,
                FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(output, snapshot, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, _path, overwrite: true);
            lock (_sync) { _savedGeneration = generation; }
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public void Dispose() => _saveLock.Dispose();

    private sealed record UsageSeen(string Key, DateTimeOffset FirstSeen, DateTimeOffset LastSeen);
}

public sealed partial class UsageHeartbeatWorker(UsageHeartbeatStore store, ILogger<UsageHeartbeatWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!store.IsAvailable) { LogUnavailable(logger); return; }
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(60));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                if (store.RegisteredCount >= UsageHeartbeatStore.MaximumInstallations * 9 / 10)
                { LogCapacity(logger, store.RegisteredCount); }
                await SaveSafelyAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        await SaveSafelyAsync(cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "USAGE_STORE_UNAVAILABLE: statistics disabled; other endpoints remain available")]
    private static partial void LogUnavailable(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "USAGE_CAPACITY_WARNING count={Count}")]
    private static partial void LogCapacity(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "USAGE_STORE_WRITE_FAILED")]
    private static partial void LogWriteFailed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "USAGE_STORE_ACCESS_DENIED")]
    private static partial void LogAccessDenied(ILogger logger);

    private async Task SaveSafelyAsync(CancellationToken cancellationToken)
    {
        try { await store.SaveAsync(cancellationToken).ConfigureAwait(false); }
        catch (IOException) { LogWriteFailed(logger); }
        catch (UnauthorizedAccessException) { LogAccessDenied(logger); }
    }
}
