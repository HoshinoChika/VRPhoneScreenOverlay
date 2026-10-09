using System.Text.Json;
using VRPhoneScreenOverlay.Protocols;

namespace VRPhoneScreenOverlay.Service;

internal sealed class DiagnosticUploadStorage
{
    private const long _maximumReceiptBytes = 4096;
    private static readonly string[] _completedSuffixes = [".json.tmp", ".json", ".zip"];
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private readonly ServiceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly string _pendingDirectory;
    private readonly string _completedDirectory;

    public DiagnosticUploadStorage(ServiceOptions options, TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
        _pendingDirectory = Path.Combine(options.DataRoot, "diagnostics", "pending");
        _completedDirectory = Path.Combine(options.DataRoot, "diagnostics", "completed");
        Directory.CreateDirectory(_pendingDirectory);
        Directory.CreateDirectory(_completedDirectory);
    }

    internal bool HasCapacity(long additionalBytes)
    {
        long total = additionalBytes;
        int count = 0;
        foreach (FileInfo file in new DirectoryInfo(_completedDirectory).EnumerateFiles())
        {
            if (++count > 20000 || (file.Attributes & FileAttributes.ReparsePoint) != 0) { return false; }
            total += file.Length;
            if (total > _options.MaximumStoredDiagnosticBytes) { return false; }
        }
        try
        {
            // Reserve enough free space for a monthly staging ZIP as well as a safety margin.
            long free = new DriveInfo(Path.GetPathRoot(_options.DataRoot)!).AvailableFreeSpace;
            return total <= _options.MaximumStoredDiagnosticBytes && free > total + 256L * 1024 * 1024;
        }
        catch (IOException) { return false; }
    }

    public string PendingPath(string id) => Path.Combine(_pendingDirectory, id + ".upload");

    public async Task<DiagnosticUploadCompleteResponse> CommitAsync(
        string id,
        DiagnosticUploadInitRequest request,
        CancellationToken cancellationToken)
    {
        DateTimeOffset receivedAt = _timeProvider.GetUtcNow();
        CompletedUpload receipt = new(id, receivedAt, request.AppVersion, request.BundleSize,
            request.BundleSha256, receivedAt.AddDays(_options.DiagnosticRetentionDays));
        string metadata = Path.Combine(_completedDirectory, id + ".json");
        string temporary = metadata + ".tmp";
        // The receipt is prepared before moving the verified bundle. If the process
        // stops between the two renames, a retry recovers the receipt beside the ZIP.
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(receipt, _jsonOptions), cancellationToken);
        File.Move(PendingPath(id), Path.Combine(_completedDirectory, id + ".zip"));
        File.Move(temporary, metadata, overwrite: true);
        return new DiagnosticUploadCompleteResponse(true, receivedAt);
    }

    public async ValueTask<DiagnosticUploadCompleteResponse?> ReadCompletedAsync(
        DiagnosticUploadCompleteRequest request,
        CancellationToken cancellationToken)
    {
        string metadata = Path.Combine(_completedDirectory, request.UploadId + ".json");
        string path = File.Exists(metadata) ? metadata : metadata + ".tmp";
        FileInfo file = new(path);
        if (!file.Exists || file.Length > _maximumReceiptBytes ||
            (file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            return null;
        }

        try
        {
            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous);
            CompletedUpload? receipt = await JsonSerializer.DeserializeAsync<CompletedUpload>(
                stream, _jsonOptions, cancellationToken);
            if (receipt is null || receipt.Id != request.UploadId ||
                receipt.BundleSha256 != request.BundleSha256 ||
                receipt.ExpiresAt <= _timeProvider.GetUtcNow())
            {
                return null;
            }

            FileInfo zip = new(Path.Combine(_completedDirectory, request.UploadId + ".zip"));
            if (!zip.Exists || zip.Length != receipt.BundleSize ||
                (zip.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return null;
            }

            // Reading the prepared receipt is sufficient for a retry. The retention
            // pass covers both suffixes, so concurrent retries need no rename race.
            return new DiagnosticUploadCompleteResponse(true, receipt.ReceivedAt);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return null;
        }
    }

    public void CleanupExpired(IReadOnlySet<string> activeIds, DateTimeOffset now)
    {
        CleanupDirectory(_pendingDirectory, now - DiagnosticUploadStore.GrantLifetime, activeIds, pending: true);
        CleanupDirectory(_completedDirectory, now.AddDays(-_options.DiagnosticRetentionDays), activeIds, pending: false);
    }

    private void CleanupDirectory(
        string directory,
        DateTimeOffset cutoff,
        IReadOnlySet<string> activeIds,
        bool pending)
    {
        foreach (FileInfo file in new DirectoryInfo(directory).EnumerateFiles("VD-*", SearchOption.TopDirectoryOnly))
        {
            string? suffix = pending
                ? file.Name.EndsWith(".upload", StringComparison.Ordinal) ? ".upload" : null
                : _completedSuffixes.FirstOrDefault(
                    item => file.Name.EndsWith(item, StringComparison.Ordinal));
            if (suffix is null)
            {
                continue;
            }

            string id = file.Name[..^suffix.Length];
            if (ValidId(id) && !activeIds.Contains(id) && file.LastWriteTimeUtc <= cutoff.UtcDateTime &&
                (file.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                if (!pending && _options.ArchiveDiagnosticsToCloud &&
                    !File.Exists(Path.Combine(_options.DataRoot, "diagnostics", "archived", id + ".json"))) { continue; }
                TryDelete(file.FullName);
                if (!pending && !_completedSuffixes.Any(part => File.Exists(Path.Combine(directory, id + part))))
                { TryDelete(Path.Combine(_options.DataRoot, "diagnostics", "archived", id + ".json")); }
            }
        }
    }

    internal static bool ValidId(string id) =>
        id is { Length: 24 or 44 } && id.StartsWith("VD-", StringComparison.Ordinal) && id[11] == '-' &&
        id[3..11].All(char.IsAsciiDigit) && id[12..].All(Uri.IsHexDigit);

    internal static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Locked files remain scoped to these directories and are retried by
            // the next retention pass; a single file must not stop the worker.
        }
    }

    private sealed record CompletedUpload(
        string Id,
        DateTimeOffset ReceivedAt,
        string AppVersion,
        long BundleSize,
        string BundleSha256,
        DateTimeOffset ExpiresAt);
}
