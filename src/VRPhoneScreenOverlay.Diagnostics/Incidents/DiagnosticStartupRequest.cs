using System.Globalization;

namespace VRPhoneScreenOverlay.Diagnostics;

/// <summary>One-use opt-in with a 24-hour validity window; never an always-on debugger setting.</summary>
internal static class DiagnosticStartupRequest
{
    internal static async Task SetAsync(string root, bool enabled, DateTimeOffset now, CancellationToken token)
    {
        using FileStream lease = DiagnosticCaptureStore.Lock(root);
        string path = Path.Combine(root, "startup.request");
        if (!enabled) { File.Delete(path); return; }
        string temporary = Path.Combine(root, Guid.NewGuid().ToString("N") + ".request.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, now.AddHours(24).ToString("O", CultureInfo.InvariantCulture), token).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally { IncidentStorage.TryDelete(temporary); }
    }

    internal static bool Consume(string root, DateTimeOffset now)
    {
        if (!Directory.Exists(root)) { return false; }
        using FileStream lease = DiagnosticCaptureStore.Lock(root);
        string path = Path.Combine(root, "startup.request");
        FileInfo file = new(path);
        if (!file.Exists) { return false; }
        bool valid = file.Length <= 128 && (file.Attributes & FileAttributes.ReparsePoint) == 0 &&
            DateTimeOffset.TryParseExact(File.ReadAllText(path), "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset expires) &&
            expires > now && expires <= now.AddHours(24);
        File.Delete(path);
        return valid;
    }
}
