using System.IO.Compression;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Update;

internal sealed record UpdateExtractionLimits(int Entries, long EntryBytes, long ExpandedBytes,
    int CompressionRatio, int RatioMinimumBytes)
{
    internal static UpdateExtractionLimits Default { get; } = new(UpdatePackageLimits.MaximumEntries,
        UpdatePackageLimits.MaximumEntryBytes, UpdatePackageLimits.MaximumExpandedBytes,
        UpdatePackageLimits.MaximumCompressionRatio, UpdatePackageLimits.RatioCheckMinimumBytes);
}

internal static class UpdateArchiveExtractor
{
    internal static async Task ExtractAsync(string package, string stage, CancellationToken token,
        UpdateExtractionLimits? limits = null)
    {
        limits ??= UpdateExtractionLimits.Default;
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(UpdatePackageLimits.ExtractionTimeoutSeconds));
        try { await ExtractCoreAsync(package, stage, limits, deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        { throw new UpdateException(UpdateReasonCodes.PackageExtractionTimeout, "更新包解压超时", error); }
        catch (InvalidDataException error)
        { throw new UpdateException(UpdateReasonCodes.ArchiveInvalid, "更新包压缩结构无效", error); }
    }

    private static async Task ExtractCoreAsync(string package, string stage, UpdateExtractionLimits limits, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string prefix = Path.GetFullPath(stage).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using ZipArchive archive = await ZipFile.OpenReadAsync(package, token).ConfigureAwait(false);
        if (archive.Entries.Count is 0 || archive.Entries.Count > limits.Entries) { throw Limit(); }
        long declared = 0;
        HashSet<string> destinations = new(StringComparer.OrdinalIgnoreCase);
        // Validate the whole directory before writing any file.
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            string destination = Destination(entry, stage, prefix);
            if (!destinations.Add(destination.TrimEnd(Path.DirectorySeparatorChar))) { throw InvalidPath(); }
            if (entry.Length < 0 || entry.Length > limits.EntryBytes || entry.Length > limits.ExpandedBytes - declared ||
                (entry.Length > limits.RatioMinimumBytes && entry.Length / (double)Math.Max(1, entry.CompressedLength) > limits.CompressionRatio))
            { throw Limit(); }
            declared += entry.Length;
        }
        byte[] buffer = new byte[128 * 1024]; // One reusable extraction buffer, not one per entry.
        long expanded = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            string destination = Destination(entry, stage, prefix);
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(destination); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using Stream input = await entry.OpenAsync(token).ConfigureAwait(false);
            await using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                buffer.Length, FileOptions.Asynchronous | FileOptions.SequentialScan);
            long written = 0;
            while (true)
            {
                int read = await input.ReadAsync(buffer, token).ConfigureAwait(false);
                if (read == 0) { break; }
                if (read > entry.Length - written || read > limits.EntryBytes - written || read > limits.ExpandedBytes - expanded)
                { throw Limit(); }
                await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                written += read;
                expanded += read;
            }
            if (written != entry.Length) { throw new UpdateException(UpdateReasonCodes.ArchiveContentTruncated, "更新包解压内容不完整"); }
        }
    }

    private static string Destination(ZipArchiveEntry entry, string stage, string prefix)
    {
        string name = entry.FullName;
        if (name.Length is 0 || name.Length > UpdatePackageLimits.MaximumEntryPathCharacters ||
            name.Contains('\\') || name.Contains(':') || Path.IsPathRooted(name) ||
            name.TrimEnd('/').Split('/').Any(InvalidSegment) ||
            ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000 ||
            (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
        { throw InvalidPath(); }
        string destination;
        try { destination = Path.GetFullPath(Path.Combine(stage, name)); }
        catch (ArgumentException) { throw InvalidPath(); }
        if (!destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { throw InvalidPath(); }
        return destination;
    }

    private static bool InvalidSegment(string part)
    {
        if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
            part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { return true; }
        string stem = part.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] is >= '1' and <= '9');
    }

    private static UpdateException Limit() => new(UpdateReasonCodes.ArchiveExpansionTooLarge, "更新包解压内容超过允许预算");
    private static UpdateException InvalidPath() => new(UpdateReasonCodes.PackagePathInvalid, "更新包包含无效路径");
}
