using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.Settings;

namespace VRPhoneScreenOverlay.Session;

public sealed class VideoUploadPreferences(IAppSettingsService settings, string? environmentOverride)
{
    public VideoUploadSelection Selection => VideoUploadSelection.Resolve(settings.Snapshot.Value.VideoUploadMode, environmentOverride);
    public ValueTask SaveAsync(string mode, CancellationToken token) => settings.SaveVideoUploadModeAsync(mode, token);
}
