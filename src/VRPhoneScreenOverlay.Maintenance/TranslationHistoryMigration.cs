using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.Update;

namespace VRPhoneScreenOverlay.Maintenance;

internal static class TranslationHistoryMigration
{
    public static void Copy(string sourceInstall, string destinationInstall)
    {
        string source = HistoryDirectory(sourceInstall);
        if (!Directory.Exists(source)) { return; }
        string destination = HistoryDirectory(destinationInstall);
        List<FileInfo> files = ReadBoundedFiles(source);
        Dictionary<string, long> merged = ReadBoundedFiles(destination).ToDictionary(file => file.Name, file => file.Length, StringComparer.OrdinalIgnoreCase);
        foreach (FileInfo file in files) { merged[file.Name] = file.Length; }
        if (merged.Count > TranslationHistoryLimits.MaximumRecords || merged.Values.Sum() > TranslationHistoryLimits.MaximumDirectoryBytes)
        { throw Capacity(); }
        // All metadata budgets are checked before copying. Refuse oversized legacy
        // history rather than silently dropping it during an update.
        Directory.CreateDirectory(destination);
        byte[] buffer = new byte[64 * 1024];
        foreach (FileInfo file in files)
        {
            ValidatePath(file.FullName);
            string target = Path.Combine(destination, file.Name);
            ValidatePath(target);
            CopyFile(file.FullName, target, buffer);
            File.SetLastWriteTimeUtc(target, file.LastWriteTimeUtc);
        }
    }

    private static void CopyFile(string source, string target, byte[] buffer)
    {
        using FileStream input = File.OpenRead(source);
        if (input.Length > TranslationHistoryLimits.MaximumRecordBytes) { throw Capacity(); }
        using FileStream output = new(target, FileMode.Create, FileAccess.Write, FileShare.None);
        int written = 0;
        int read;
        while ((read = input.Read(buffer)) != 0)
        {
            if (read > TranslationHistoryLimits.MaximumRecordBytes - written) { throw Capacity(); }
            output.Write(buffer, 0, read);
            written += read;
        }
    }

    private static List<FileInfo> ReadBoundedFiles(string directory)
    {
        List<FileInfo> files = [];
        if (!Directory.Exists(directory)) { return files; }
        long total = 0;
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(directory, "*.json"))
        {
            if (!TranslationHistoryLimits.IsRecordName(Path.GetFileName(path))) { continue; }
            ValidatePath(path);
            FileInfo file = new(path);
            if (files.Count >= TranslationHistoryLimits.MaximumRecords || file.Length > TranslationHistoryLimits.MaximumRecordBytes ||
                file.Length > TranslationHistoryLimits.MaximumDirectoryBytes - total || !names.Add(file.Name)) { throw Capacity(); }
            total += file.Length;
            files.Add(file);
        }
        return files;
    }

    private static UpdateException Capacity() => new(UpdateReasonCodes.HistoryCapacityExceeded, "翻译历史超过更新迁移预算，原历史已保留");

    private static string HistoryDirectory(string root)
    {
        string current = Path.GetFullPath(root);
        for (string? ancestor = current; ancestor is not null; ancestor = Path.GetDirectoryName(ancestor)) { ValidatePath(ancestor); }
        foreach (string segment in new[] { "app", "data", "translation-history" })
        { current = Path.Combine(current, segment); ValidatePath(current); }
        return current;
    }
    private static void ValidatePath(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) { throw new IOException("Translation history must not contain links."); }
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }
}
