using System.Text.Json;
using VRPhoneScreenOverlay.Protocols;

namespace VRPhoneScreenOverlay.Diagnostics;

public sealed partial class TroubleshootingWorkflow
{
    // Private bounded records remain available after an uncertain upload. They never replay checks or model calls.
    private async Task SaveInvestigationAsync(TroubleshootingCase item, CancellationToken token)
    {
        string directory = Path.Combine(Path.GetDirectoryName(_outbox)!, "investigations");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, item.Id + ".json");
        if (!File.Exists(path) && Directory.EnumerateFiles(directory, "*.json").Take(100).Count() >= 100)
        { throw new InvalidDataException("AI_LOCAL_HISTORY_FULL"); }
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(item, _json);
        if (data.Length > 256 * 1024) { throw new InvalidDataException("AI_LOCAL_HISTORY_LIMIT"); }
        string temporary = path + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, data, token).ConfigureAwait(false);
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); }
    }
}
