using System.Buffers;
using System.Text.Json;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Session;

/// <summary>Single-owner history store. Callers serialize load/save operations.</summary>
public sealed class TranslationHistoryStore(string directory) : ITranslationHistory
{
    private readonly string _directory = Path.GetFullPath(directory);
    private sealed record Stored(TranslationRecord Record, string Path, long Bytes);

    // Directory metadata enumeration is synchronous. One worker per user operation
    // keeps large legacy directories off the UI thread; never one task per record.
    public Task<IReadOnlyList<TranslationRecord>> LoadAsync(CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            ValidateDirectory();
            if (!Directory.Exists(_directory)) { return (IReadOnlyList<TranslationRecord>)Array.Empty<TranslationRecord>(); }
            IReadOnlyList<Stored> selected = await SelectAsync(TranslationHistoryLimits.MaximumRecords, cancellationToken).ConfigureAwait(false);
            return selected.Select(item => item.Record).ToArray();
        }, cancellationToken);

    public Task SaveAsync(TranslationRecord record, int retentionLimit, CancellationToken cancellationToken) =>
        Task.Run(() => SaveCoreAsync(record, retentionLimit, cancellationToken), cancellationToken);

    private async Task SaveCoreAsync(TranslationRecord record, int retentionLimit, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentOutOfRangeException.ThrowIfLessThan(retentionLimit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(retentionLimit, TranslationHistoryLimits.MaximumRecords);
        if (record.Title is null || record.Text is null) { throw new InvalidDataException("TRANSLATION_RECORD_INVALID"); }
        long characters = (long)record.Title.Length + record.Text.Length + (record.FailureCode?.Length ?? 0) + (record.PersistenceFailureCode?.Length ?? 0);
        if (characters > TranslationHistoryLimits.MaximumRecordBytes) { throw new InvalidDataException("TRANSLATION_RECORD_TOO_LARGE"); }
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(record);
        if (bytes.Length > TranslationHistoryLimits.MaximumRecordBytes) { throw new InvalidDataException("TRANSLATION_RECORD_TOO_LARGE"); }
        ValidateDirectory();
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, record.Id.ToString("N") + ".json");
        ValidateFile(path);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous))
            { await output.WriteAsync(bytes, token).ConfigureAwait(false); }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }

        IReadOnlyList<Stored> selected = await SelectAsync(retentionLimit, token).ConfigureAwait(false);
        HashSet<string> retained = selected.Select(item => item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(_directory))
        {
            token.ThrowIfCancellationRequested();
            string name = Path.GetFileName(file);
            if ((TranslationHistoryLimits.IsRecordName(name) && !retained.Contains(file)) || TranslationHistoryLimits.IsTemporaryName(name))
            { ValidateFile(file); File.Delete(file); }
        }
        // Fail explicitly if an external writer or an undeletable record prevents
        // the quota. Do not report a successful bounded save while over budget.
        long total = 0;
        int count = 0;
        foreach (string file in Directory.EnumerateFiles(_directory))
        {
            token.ThrowIfCancellationRequested();
            if (!TranslationHistoryLimits.IsRecordName(Path.GetFileName(file))) { continue; }
            ValidateFile(file);
            long length = new FileInfo(file).Length;
            if (++count > retentionLimit || length > TranslationHistoryLimits.MaximumRecordBytes ||
                length > TranslationHistoryLimits.MaximumDirectoryBytes - total)
            { throw new IOException("TRANSLATION_HISTORY_CAPACITY_EXCEEDED"); }
            total += length;
        }
    }

    private async Task<IReadOnlyList<Stored>> SelectAsync(int limit, CancellationToken token)
    {
        // Capacity <=1000 valid records and <=128MiB serialized data, oldest first.
        PriorityQueue<Stored, (long Created, Guid Id)> newest = new();
        long retainedBytes = 0;
        byte[] bytes = ArrayPool<byte>.Shared.Rent(TranslationHistoryLimits.MaximumRecordBytes + 1);
        try
        {
            foreach (string path in Directory.EnumerateFiles(_directory, "*.json"))
            {
                token.ThrowIfCancellationRequested();
                if (!TranslationHistoryLimits.IsRecordName(Path.GetFileName(path))) { continue; }
                ValidateFile(path);
                try
                {
                    await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                        4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    if (stream.Length > TranslationHistoryLimits.MaximumRecordBytes) { continue; }
                    int count = await stream.ReadAtLeastAsync(bytes.AsMemory(0, TranslationHistoryLimits.MaximumRecordBytes + 1),
                        TranslationHistoryLimits.MaximumRecordBytes + 1, throwOnEndOfStream: false, token).ConfigureAwait(false);
                    if (count > TranslationHistoryLimits.MaximumRecordBytes) { continue; }
                    int start = count >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
                    TranslationRecord? record = JsonSerializer.Deserialize<TranslationRecord>(bytes.AsSpan(start, count - start));
                    if (record?.Title is null || record.Text is null ||
                        !string.Equals(record.Id.ToString("N") + ".json", Path.GetFileName(path), StringComparison.OrdinalIgnoreCase)) { continue; }
                    newest.Enqueue(new(record, path, count), (record.CreatedAt.UtcTicks, record.Id));
                    retainedBytes += count;
                    while (newest.Count > limit || retainedBytes > TranslationHistoryLimits.MaximumDirectoryBytes)
                    { retainedBytes -= newest.Dequeue().Bytes; }
                }
                catch (JsonException) { /* A damaged owned record is excluded, then pruned on save. */ }
                catch (FileNotFoundException) { /* An external atomic replacement/removal won the race. */ }
            }
            return newest.UnorderedItems.Select(item => item.Element).OrderByDescending(item => item.Record.CreatedAt)
                .ThenByDescending(item => item.Record.Id).ToArray();
        }
        finally { ArrayPool<byte>.Shared.Return(bytes, clearArray: true); }
    }

    private void ValidateDirectory()
    {
        for (string? current = _directory; current is not null; current = Path.GetDirectoryName(current)) { ValidateFile(current); }
    }
    private static void ValidateFile(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) { throw new IOException("TRANSLATION_HISTORY_LINK_REJECTED"); }
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }
}
