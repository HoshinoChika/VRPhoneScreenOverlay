using System.Text;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Android;

// Called only by the logger's writer. Readers can keep the previous file open
// while a rotation replaces its directory entry.
internal sealed class BoundedDiagnosticLogFiles(string path)
{
    internal const long FileLimit = 2 * 1024 * 1024;
    private readonly string _path = Path.GetFullPath(path);
    private long _nextCleanup;
    private DateTime _hour = DateTime.UtcNow;
    internal long CleanupFailures { get; private set; }

    internal async Task AppendAsync(IReadOnlyList<string> lines, CancellationToken token)
    {
        if (lines.Count == 0) { return; }
        string directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        { throw new IOException("Diagnostic directory must not be a link."); }
        foreach (string line in lines)
        {
            int bytes = Encoding.UTF8.GetByteCount(line) + 1;
            if (bytes > FileLimit) { throw new IOException("Diagnostic event exceeds file budget."); }
            if (File.Exists(_path))
            {
                if ((File.GetAttributes(_path) & FileAttributes.ReparsePoint) != 0)
                { throw new IOException("Diagnostic file must not be a link."); }
                DateTime now = DateTime.UtcNow;
                if (new FileInfo(_path).Length + bytes > FileLimit || now.Date != _hour.Date || now.Hour != _hour.Hour)
                {
                    string previous = Path.ChangeExtension(_path, $".{DateTime.UtcNow:yyyyMMddTHHmmssfffffff}-{Guid.NewGuid():N}.jsonl");
                    File.Move(_path, previous);
                    _hour = now;
                }
            }
            await using FileStream stream = new(_path, FileMode.Append, FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
            await stream.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"), token).ConfigureAwait(false);
        }
        if (Environment.TickCount64 < _nextCleanup) { return; }
        _nextCleanup = Environment.TickCount64 + 60000;
        FileInfo[] files = new DirectoryInfo(directory).GetFiles("android-*.jsonl")
            .Where(file => (file.Attributes & FileAttributes.ReparsePoint) == 0).ToArray();
        DateTime cutoff = DateTime.UtcNow - DiagnosticRetentionPolicy.LocalWindow;
        foreach (FileInfo file in files.Where(file => file.LastWriteTimeUtc < cutoff))
        {
            if (file.FullName.Equals(_path, StringComparison.OrdinalIgnoreCase)) { continue; }
            try { file.Delete(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { CleanupFailures++; }
        }
    }
}
