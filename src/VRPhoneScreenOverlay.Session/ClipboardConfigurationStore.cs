using System.Text.Json;

namespace VRPhoneScreenOverlay.Session;

public sealed record ClipboardPreferences(bool AutoCopy = false, bool AutoParse = false);

public sealed class ClipboardConfigurationStore(string path)
{
    private ClipboardPreferences _preferences = new();
    public ClipboardPreferences Preferences => Volatile.Read(ref _preferences);
    public async Task LoadAsync(CancellationToken token)
    {
        if (File.Exists(path))
        {
            var value = JsonSerializer.Deserialize<ClipboardPreferences>(await File.ReadAllTextAsync(path, token).ConfigureAwait(false));
            if (value is not null) { Volatile.Write(ref _preferences, value); }
        }
    }
    // Settings UI serializes saves by disabling both toggles until the accepted save completes.
    public async Task SaveAsync(ClipboardPreferences value, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value), token).ConfigureAwait(false);
            File.Move(temporary, path, true);
            Volatile.Write(ref _preferences, value);
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
    }
}
