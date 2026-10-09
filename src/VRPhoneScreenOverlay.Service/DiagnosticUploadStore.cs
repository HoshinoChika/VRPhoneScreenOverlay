using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VRPhoneScreenOverlay.Protocols;

namespace VRPhoneScreenOverlay.Service;

public sealed record ServiceOptions(
    string DataRoot,
    long MaximumDiagnosticBytes,
    int DiagnosticRetentionDays,
    int UploadsPerHour)
{
    public bool ArchiveDiagnosticsToCloud { get; init; }
    public long MaximumStoredDiagnosticBytes { get; init; } = 2L * 1024 * 1024 * 1024;

    public static ServiceOptions FromEnvironment() => new(
        Path.GetFullPath(Environment.GetEnvironmentVariable("DATA_ROOT") ?? "/data"),
        ReadLong("MAX_DIAGNOSTIC_BYTES", 24L * 1024 * 1024),
        checked((int)ReadLong("DIAGNOSTIC_RETENTION_DAYS", 14)),
        checked((int)ReadLong("DIAGNOSTIC_UPLOADS_PER_HOUR", 8)))
    { ArchiveDiagnosticsToCloud = CloudStorageOptions.FromEnvironment().Enabled };

    private static long ReadLong(string name, long fallback) =>
        long.TryParse(Environment.GetEnvironmentVariable(name), out long value) && value > 0
            ? value
            : fallback;
}

public sealed record DiagnosticStoreResult(bool Succeeded, int StatusCode, string? Error);

public sealed class DiagnosticUploadStore
{
    internal static readonly TimeSpan GrantLifetime = TimeSpan.FromMinutes(10);
    // Capacity rejects new grants with 429; existing grants/retries keep their slot.
    // Metadata expires after the grant/hour windows and never contains raw tokens.
    private const int _maximumPendingUploads = 1024;
    private const int _maximumRateAddresses = 4096;
    private readonly object _sync = new();
    private readonly ServiceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly DiagnosticUploadStorage _storage;
    private readonly byte[] _tokenKey = RandomNumberGenerator.GetBytes(32);
    private readonly Dictionary<string, PendingUpload> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Queue<DateTimeOffset>> _rateHistory = new(StringComparer.Ordinal);

    public DiagnosticUploadStore(ServiceOptions options)
        : this(options, TimeProvider.System)
    {
    }

    internal DiagnosticUploadStore(ServiceOptions options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _options = options;
        _timeProvider = timeProvider;
        _storage = new DiagnosticUploadStorage(options, timeProvider);
        Directory.CreateDirectory(Path.Combine(options.DataRoot, "updates", "beta"));
    }

    public ValueTask<DiagnosticUploadInitResponse?> CreateAsync(
        DiagnosticUploadInitRequest request,
        string address,
        string scheme,
        string host,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.BundleSize <= 0 || request.BundleSize > _options.MaximumDiagnosticBytes ||
            !ValidHash(request.BundleSha256) || string.IsNullOrWhiteSpace(request.AppVersion) ||
            request.AppVersion.Length > 128 || address.Length > 256)
        {
            return ValueTask.FromResult<DiagnosticUploadInitResponse?>(null);
        }

        request = request with { BundleSha256 = request.BundleSha256.ToLowerInvariant() };
        string addressKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(address)));
        string identity = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            addressKey,
            request,
        })));
        DateTimeOffset now = _timeProvider.GetUtcNow();
        lock (_sync)
        {
            RemoveExpiredMetadata(now);
            PendingUpload? pending = _pending.Values.FirstOrDefault(
                item => item.Identity == identity && item.ExpiresAt > now);
            if (pending is null)
            {
                if (!_storage.HasCapacity(request.BundleSize + _pending.Values.Sum(p => p.Request.BundleSize))) { return ValueTask.FromResult<DiagnosticUploadInitResponse?>(null); }
                if (_pending.Count >= _maximumPendingUploads || !AllowRate(addressKey, now))
                {
                    return ValueTask.FromResult<DiagnosticUploadInitResponse?>(null);
                }

                string id = $"VD-{now:yyyyMMdd}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}";
                pending = new PendingUpload(id, identity, request, now + GrantLifetime);
                _pending.Add(id, pending);
            }

            string uploadUrl = $"{scheme}://{host}/vrphonescreen/api/v1/diagnostics/upload/{pending.Id}";
            return ValueTask.FromResult<DiagnosticUploadInitResponse?>(new DiagnosticUploadInitResponse(
                pending.Id, uploadUrl, UploadToken(pending.Id),
                _options.MaximumDiagnosticBytes, pending.ExpiresAt));
        }
    }

    public async ValueTask<DiagnosticStoreResult> UploadAsync(
        string id,
        string token,
        Stream body,
        long? contentLength,
        CancellationToken cancellationToken)
    {
        PendingUpload pending;
        lock (_sync)
        {
            if (!_pending.TryGetValue(id, out PendingUpload? candidate) ||
                candidate.ExpiresAt <= _timeProvider.GetUtcNow() ||
                !TokenMatches(UploadToken(id), token) ||
                contentLength != candidate.Request.BundleSize ||
                contentLength > _options.MaximumDiagnosticBytes)
            {
                return new DiagnosticStoreResult(false, StatusCodes.Status401Unauthorized, "Upload authorization failed");
            }

            if (candidate.Uploading || candidate.UploadedSha256 is not null)
            {
                return new DiagnosticStoreResult(false, StatusCodes.Status409Conflict, "Upload already started or accepted");
            }

            pending = candidate;
            pending.Uploading = true;
        }

        bool created = false;
        bool accepted = false;
        string temporaryPath = _storage.PendingPath(id);
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        TimeSpan remaining = pending.ExpiresAt - _timeProvider.GetUtcNow();
        deadline.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
        try
        {
            using IncrementalHash digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[128 * 1024];
            long received = 0;
            await using (FileStream output = new(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                created = true;
                while (true)
                {
                    int read = await body.ReadAsync(buffer, deadline.Token);
                    if (read == 0)
                    {
                        break;
                    }

                    received = checked(received + read);
                    if (received > pending.Request.BundleSize || received > _options.MaximumDiagnosticBytes)
                    {
                        throw new InvalidDataException("Upload exceeds declared size.");
                    }

                    digest.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), deadline.Token);
                }

                await output.FlushAsync(deadline.Token);
            }

            string hash = Convert.ToHexStringLower(digest.GetHashAndReset());
            if (received != pending.Request.BundleSize || hash != pending.Request.BundleSha256)
            {
                throw new InvalidDataException("Upload hash does not match declaration.");
            }

            using (ZipArchive archive = await ZipFile.OpenReadAsync(temporaryPath, deadline.Token))
            {
                if (archive.Entries.Count == 0)
                {
                    throw new InvalidDataException("Diagnostic ZIP is empty.");
                }
            }

            lock (_sync)
            {
                pending.UploadedSha256 = hash;
                accepted = true;
            }

            return new DiagnosticStoreResult(true, StatusCodes.Status201Created, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DiagnosticStoreResult(false, StatusCodes.Status401Unauthorized, "Upload authorization expired");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new DiagnosticStoreResult(false, StatusCodes.Status400BadRequest, "Diagnostic package is invalid");
        }
        finally
        {
            if (created && !accepted)
            {
                DiagnosticUploadStorage.TryDelete(temporaryPath);
            }

            lock (_sync)
            {
                pending.Uploading = false;
            }
        }
    }

    public async ValueTask<DiagnosticUploadCompleteResponse?> CompleteAsync(
        DiagnosticUploadCompleteRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!DiagnosticUploadStorage.ValidId(request.UploadId) || !ValidHash(request.BundleSha256))
        {
            return null;
        }

        request = request with { BundleSha256 = request.BundleSha256.ToLowerInvariant() };
        DiagnosticUploadCompleteResponse? completed = await _storage.ReadCompletedAsync(request, cancellationToken);
        if (completed is not null)
        {
            return completed;
        }

        Task<DiagnosticUploadCompleteResponse> completion;
        lock (_sync)
        {
            if (!_pending.TryGetValue(request.UploadId, out PendingUpload? pending) ||
                pending.ExpiresAt <= _timeProvider.GetUtcNow() || pending.Uploading ||
                pending.UploadedSha256 != request.BundleSha256)
            {
                return null;
            }

            if (pending.Completion is { IsFaulted: true } or { IsCanceled: true })
            {
                pending.Completion = null;
            }

            // One bounded commit per grant continues through a disconnected caller.
            // Other callers await the same result; no token/address is written to disk.
            completion = pending.Completion ??= _storage.CommitAsync(
                pending.Id, pending.Request, CancellationToken.None);
        }

        return await completion.WaitAsync(cancellationToken);
    }

    public void CleanupExpired()
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        HashSet<string> activeIds;
        lock (_sync)
        {
            RemoveExpiredMetadata(now);
            activeIds = new HashSet<string>(_pending.Keys, StringComparer.Ordinal);
        }

        _storage.CleanupExpired(activeIds, now);
    }

    private void RemoveExpiredMetadata(DateTimeOffset now)
    {
        foreach ((string id, PendingUpload pending) in _pending.ToArray())
        {
            if (pending.ExpiresAt <= now && !pending.Uploading &&
                pending.Completion is not { IsCompleted: false })
            {
                _pending.Remove(id);
            }
        }

        foreach ((string address, Queue<DateTimeOffset> queue) in _rateHistory.ToArray())
        {
            while (queue.TryPeek(out DateTimeOffset item) && item <= now.AddHours(-1))
            {
                queue.Dequeue();
            }

            if (queue.Count == 0)
            {
                _rateHistory.Remove(address);
            }
        }
    }

    private bool AllowRate(string addressKey, DateTimeOffset now)
    {
        if (!_rateHistory.TryGetValue(addressKey, out Queue<DateTimeOffset>? queue))
        {
            if (_rateHistory.Count >= _maximumRateAddresses)
            {
                return false;
            }

            queue = new Queue<DateTimeOffset>();
            _rateHistory.Add(addressKey, queue);
        }

        if (queue.Count >= _options.UploadsPerHour)
        {
            return false;
        }

        queue.Enqueue(now);
        return true;
    }

    private string UploadToken(string id) =>
        Convert.ToBase64String(HMACSHA256.HashData(_tokenKey, Encoding.UTF8.GetBytes(id)));

    private static bool TokenMatches(string expected, string token) =>
        token is { Length: 44 } && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(token));

    private static bool ValidHash(string hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

    private sealed class PendingUpload(
        string id,
        string identity,
        DiagnosticUploadInitRequest request,
        DateTimeOffset expiresAt)
    {
        public string Id { get; } = id;
        public string Identity { get; } = identity;
        public DiagnosticUploadInitRequest Request { get; } = request;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public bool Uploading { get; set; }
        public string? UploadedSha256 { get; set; }
        public Task<DiagnosticUploadCompleteResponse>? Completion { get; set; }
    }
}

public sealed class RetentionWorker(DiagnosticUploadStore store) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            store.CleanupExpired();
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
