using VRPhoneScreenOverlay.Settings;

namespace VRPhoneScreenOverlay.Session;

public sealed class PhotoSyncPreferences(IAppSettingsService settings, VrcPhotoSyncService service)
{
    public bool Enabled => settings.Snapshot.Value.VrcPhotoSyncEnabled;
    public string Status => service.Status;
    public Task RestoreAsync(CancellationToken token) => service.SetEnabledAsync(Enabled, token);
    public async Task SetEnabledAsync(bool enabled, CancellationToken token)
    {
        await settings.SavePhotoSyncAsync(enabled, token).ConfigureAwait(false);
        await service.SetEnabledAsync(enabled, token).ConfigureAwait(false);
    }
}
