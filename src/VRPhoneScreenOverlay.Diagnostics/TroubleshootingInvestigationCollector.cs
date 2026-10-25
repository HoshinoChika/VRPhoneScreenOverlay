using System.Text.Json;
using VRPhoneScreenOverlay.Protocols;

namespace VRPhoneScreenOverlay.Diagnostics;

internal static class TroubleshootingInvestigationCollector
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    internal static async Task<object> ReadAsync(string? directory, TimeSpan window, CancellationToken token)
    {
        List<TroubleshootingCase> records = new(3);
        bool incomplete = false;
        try
        {
            if (directory is not null && Directory.Exists(directory))
            {
                IncidentStorage.ValidateDirectory(directory);
                FileInfo[] files = new DirectoryInfo(directory).EnumerateFiles("*.json").Take(101).ToArray();
                incomplete = files.Length > 100;
                foreach (FileInfo file in files.Where(f => f.LastWriteTimeUtc >= DateTime.UtcNow - window)
                    .OrderByDescending(f => f.LastWriteTimeUtc).Take(3))
                {
                    if (file.Length > 256 * 1024 || (file.Attributes & FileAttributes.ReparsePoint) != 0) { incomplete = true; continue; }
                    await using FileStream input = new(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous);
                    TroubleshootingCase? item = await JsonSerializer.DeserializeAsync<TroubleshootingCase>(input, _json, token).ConfigureAwait(false);
                    if (item is not null) { records.Add(item); }
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { incomplete = true; }
        return new { capacity = 3, incomplete, records };
    }
}
