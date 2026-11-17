using Vortice.Direct3D11;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.SteamVR;

internal static class D3D11VideoColorPolicy
{
    internal static VideoProcessorColorSpace Input(VideoColorMetadata color)
    {
        // MFVideoTransferMatrix: Unknown=0, BT709=1, BT601=2.
        // MFNominalRange: Unknown=0, full=1, limited=2. Preserve the legacy
        // D3D defaults for omitted/unknown attributes, without calling them known.
        if (color.YuvMatrix is > 2 || color.NominalRange is > 2)
        {
            throw new GpuVideoPresenterException(OpenVrReasonCodes.D3d11VideoPixelFormatUnsupported,
                "当前视频色彩矩阵或范围不受转换器支持");
        }
        return new VideoProcessorColorSpace
        {
            YCbCr_Matrix = color.YuvMatrix == 1 ? 1U : 0U,
            // MF and D3D reverse the numeric values for full and limited range.
            Nominal_Range = (uint)(color.NominalRange switch
            {
                1 => VideoProcessorNominalRange.Range_0_255,
                2 => VideoProcessorNominalRange.Range_16_235,
                _ => VideoProcessorNominalRange.Undefined
            }),
        };
    }
}
