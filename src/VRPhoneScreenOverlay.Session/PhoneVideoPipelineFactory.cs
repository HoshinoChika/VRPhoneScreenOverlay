using VRPhoneScreenOverlay.Media;
using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Session;

public interface IPhoneVideoPipelineFactory
{
    public IOpenVrSceneOverlay CreateOverlay();
    public IH264VideoDecoder CreateDecoder(int width, int height, ReadOnlySpan<byte> configuration);
    public IH264VideoDecoder CreateDecoder(int width, int height, ReadOnlySpan<byte> configuration,
        Action<DecoderInitializationEvent> observe) => CreateDecoder(width, height, configuration);
    public IGpuVideoFramePresenter CreatePresenter(long graphicsAdapterLuid);
}

public sealed class PhoneVideoPipelineFactory(OpenVrVideoSettingsChannel? videoSettings = null,
    VideoDecoderPreference decoderPreference = VideoDecoderPreference.Automatic, OpenVrTranslationChannel? translation = null,
    OpenVrClipboardChannel? clipboard = null, Func<IOpenVrSceneOverlay>? acquireOverlay = null, Func<VideoUploadSelection>? uploadSelection = null) : IPhoneVideoPipelineFactory
{
    private VideoUploadSelection _sessionUpload = VideoUploadSelection.Resolve("auto", null);
    public IOpenVrSceneOverlay CreateOverlay()
    {
        _sessionUpload = uploadSelection?.Invoke() ?? VideoUploadSelection.Resolve("auto", null);
        if (acquireOverlay is not null) { return acquireOverlay(); }
        return OpenVrSceneOverlayFactory.Create(videoSettings: videoSettings, translation: translation, clipboard: clipboard);
    }
    public IH264VideoDecoder CreateDecoder(int width, int height, ReadOnlySpan<byte> configuration) =>
        VideoDecoderFactory.CreateMediaFoundationH264(width, height, 60, configuration, preference: decoderPreference);
    public IH264VideoDecoder CreateDecoder(int width, int height, ReadOnlySpan<byte> configuration,
        Action<DecoderInitializationEvent> observe) =>
        VideoDecoderFactory.CreateMediaFoundationH264(width, height, 60, configuration, observe, decoderPreference);
    public IGpuVideoFramePresenter CreatePresenter(long graphicsAdapterLuid) =>
        GpuVideoFramePresenterFactory.CreateD3D11(graphicsAdapterLuid, _sessionUpload);
}
