using VRPhoneScreenOverlay.Media;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Session;

public interface IPhoneVideoPipelineFactory
{
    public IOpenVrSceneOverlay CreateOverlay();
    public IH264VideoDecoder CreateDecoder(int width, int height, ReadOnlySpan<byte> configuration);
    public IGpuVideoFramePresenter CreatePresenter(long graphicsAdapterLuid);
}

public sealed class PhoneVideoPipelineFactory(OpenVrVideoSettingsChannel? videoSettings = null) : IPhoneVideoPipelineFactory
{
    public IOpenVrSceneOverlay CreateOverlay() => OpenVrSceneOverlayFactory.Create(videoSettings: videoSettings);
    public IH264VideoDecoder CreateDecoder(int width, int height, ReadOnlySpan<byte> configuration) =>
        VideoDecoderFactory.CreateMediaFoundationH264(width, height, 60, configuration);
    public IGpuVideoFramePresenter CreatePresenter(long graphicsAdapterLuid) =>
        GpuVideoFramePresenterFactory.CreateD3D11(graphicsAdapterLuid);
}
