using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace VRPhoneScreenOverlay.Service;

public sealed class MonthlyDiagnosticArchiver(ServiceOptions options, ICloudStorage cloud, TimeProvider time)
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    public async Task<int> ArchiveClosedMonthsAsync(CancellationToken cancellationToken)
    {
        string completed = Path.Combine(options.DataRoot, "diagnostics", "completed");
        if (!Directory.Exists(completed)) { return 0; }
        string archiveRoot = Path.Combine(options.DataRoot, "diagnostics", "monthly");
        string markers = Path.Combine(options.DataRoot, "diagnostics", "archived");
        Directory.CreateDirectory(archiveRoot); Directory.CreateDirectory(markers);
        foreach (string index in Directory.EnumerateFiles(markers, "*.index.json").Take(120))
        {
            if (new FileInfo(index).Length > 8 * 1024 * 1024) { throw new CloudTransferException("DIAGNOSTIC_INDEX_INVALID"); }
            Receipt[] saved = JsonSerializer.Deserialize<Receipt[]>(await File.ReadAllTextAsync(index, cancellationToken), _json) ?? [];
            foreach (Receipt receipt in saved)
            {
                if (!DiagnosticUploadStorage.ValidId(receipt.Id)) { throw new CloudTransferException("DIAGNOSTIC_INDEX_INVALID"); }
                string marker = Path.Combine(markers, receipt.Id + ".json");
                if (File.Exists(Path.Combine(completed, receipt.Id + ".zip")) && !File.Exists(marker))
                { await File.WriteAllTextAsync(marker, "{}", cancellationToken); }
            }
        }
        DateTimeOffset localNow = time.GetUtcNow().ToOffset(TimeSpan.FromHours(8));
        string currentMonth = localNow.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        List<Receipt> receipts = [];
        HashSet<string> receiptIds = new(StringComparer.Ordinal);
        // Capacity is enforced on admission too. Refuse an incomplete archive rather than truncate it.
        string[] files = Directory.EnumerateFiles(completed, "VD-*.json*").Take(10001).ToArray();
        if (files.Length > 10000) { throw new CloudTransferException("DIAGNOSTIC_ARCHIVE_CAPACITY"); }
        foreach (string file in files.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileInfo info = new(file);
            if (info.Length > 4096 || (info.Attributes & FileAttributes.ReparsePoint) != 0) { continue; }
            Receipt? receipt = JsonSerializer.Deserialize<Receipt>(await File.ReadAllTextAsync(file, cancellationToken), _json);
            if (receipt is null || !DiagnosticUploadStorage.ValidId(receipt.Id) || receipt.ReceivedAt.Year is < 1980 or > 2107)
            { throw new CloudTransferException("DIAGNOSTIC_RECEIPT_INVALID"); }
            if (!File.Exists(Path.Combine(completed, receipt.Id + ".zip"))) { continue; }
            string month = receipt.ReceivedAt.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM", CultureInfo.InvariantCulture);
            if (string.CompareOrdinal(month, currentMonth) >= 0) { continue; }
            DateTimeOffset receivedLocal = receipt.ReceivedAt.ToOffset(TimeSpan.FromHours(8));
            DateTimeOffset closedAt = new DateTimeOffset(receivedLocal.Year, receivedLocal.Month, 1, 0, 0, 0, TimeSpan.FromHours(8)).AddMonths(1);
            if (localNow < closedAt.AddMinutes(15)) { continue; } // Allow the ten-minute upload grants to settle.
            if (receiptIds.Add(receipt.Id)) { receipts.Add(receipt); }
        }
        int count = 0;
        foreach (IGrouping<string, Receipt> group in receipts.GroupBy(r => r.ReceivedAt.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM", CultureInfo.InvariantCulture)))
        {
            Receipt[] monthReceipts = group.OrderBy(r => r.Id, StringComparer.Ordinal).ToArray();
            if (monthReceipts.All(r => File.Exists(Path.Combine(markers, r.Id + ".json")))) { continue; }
            string archivePath = Path.Combine(archiveRoot, group.Key + ".zip");
            string temporary = archivePath + ".tmp";
            try
            {
                await using (FileStream output = new(temporary, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.Asynchronous))
                using (ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (Receipt receipt in monthReceipts)
                    {
                        string zip = Path.Combine(completed, receipt.Id + ".zip");
                        FileInfo info = new(zip);
                        if (info.Length != receipt.BundleSize || (info.Attributes & FileAttributes.ReparsePoint) != 0)
                        { throw new CloudTransferException("DIAGNOSTIC_ARCHIVE_SOURCE_CHANGED"); }
                        await using FileStream source = new(zip, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
                        string sha = Convert.ToHexString(await SHA256.HashDataAsync(source, cancellationToken));
                        if (!sha.Equals(receipt.BundleSha256, StringComparison.OrdinalIgnoreCase))
                        { throw new CloudTransferException("DIAGNOSTIC_ARCHIVE_SOURCE_CHANGED"); }
                        source.Position = 0;
                        ZipArchiveEntry entry = archive.CreateEntry(receipt.Id + ".zip", CompressionLevel.NoCompression);
                        entry.LastWriteTime = receipt.ReceivedAt;
                        await using (Stream target = await entry.OpenAsync(cancellationToken)) { await source.CopyToAsync(target, cancellationToken); }
                        ZipArchiveEntry metadata = archive.CreateEntry(receipt.Id + ".json", CompressionLevel.Optimal);
                        metadata.LastWriteTime = receipt.ReceivedAt;
                        await using Stream metadataStream = await metadata.OpenAsync(cancellationToken);
                        await JsonSerializer.SerializeAsync(metadataStream, receipt, _json, cancellationToken);
                    }
                }
                File.Move(temporary, archivePath, true);
                await cloud.UploadArchiveAsync(archivePath, $"diagnostics/{group.Key}.zip", cancellationToken);
                string indexPath = Path.Combine(markers, group.Key + ".index.json");
                await File.WriteAllTextAsync(indexPath + ".tmp", JsonSerializer.Serialize(monthReceipts, _json), cancellationToken);
                File.Move(indexPath + ".tmp", indexPath, true);
                // Only verified cloud success authorizes local retention cleanup.
                foreach (Receipt receipt in monthReceipts)
                {
                    string marker = Path.Combine(markers, receipt.Id + ".json");
                    await File.WriteAllTextAsync(marker + ".tmp", JsonSerializer.Serialize(new { month = group.Key, receipt.BundleSha256 }, _json), cancellationToken);
                    File.Move(marker + ".tmp", marker, true);
                }
                count++;
                File.Delete(archivePath);
            }
            finally { DiagnosticUploadStorage.TryDelete(temporary); }
        }
        return count;
    }

    private sealed record Receipt(string Id, DateTimeOffset ReceivedAt, string AppVersion, long BundleSize, string BundleSha256, DateTimeOffset ExpiresAt);
}

public sealed class MonthlyDiagnosticWorker(MonthlyDiagnosticArchiver archiver, CloudStorageOptions options, ILogger<MonthlyDiagnosticWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> _retry = LoggerMessage.Define(LogLevel.Warning, new EventId(4201), "DIAGNOSTIC_MONTHLY_ARCHIVE_RETRY");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled) { return; }
        using PeriodicTimer timer = new(TimeSpan.FromHours(1));
        try
        {
            do
            {
                try
                {
                    await RunAttemptAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is IOException or JsonException or CloudTransferException or HttpRequestException or OperationCanceledException)
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    _retry(logger, null);
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
    private async Task RunAttemptAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(30));
        await archiver.ArchiveClosedMonthsAsync(deadline.Token);
    }

}
