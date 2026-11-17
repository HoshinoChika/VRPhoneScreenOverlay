using VRPhoneScreenOverlay.Session;

namespace VRPhoneScreenOverlay.App;

internal sealed partial class MainForm
{
    internal void ConfigureVideoUpload(VideoUploadPreferences preferences) => _videoView.ConfigureVideoUpload(preferences);

    internal void ConfigureServerCheck(Func<CancellationToken, Task<bool>> check) => _settingsView.TranslationSettings.ConfigureServerCheck(check);
    internal void ConfigurePhotoSync(PhotoSyncPreferences service) => _settingsView.ConfigurePhotoSync(service);

    internal void ConfigureTranslation(TranslationConfigurationStore configuration, Func<string, CancellationToken, Task> validate) =>
        _settingsView.ConfigureTranslation(configuration, validate);
}
