using SharpGen.Runtime;
using Vortice.MediaFoundation;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Media;

internal static class MediaFoundationVideoColor
{
    internal static VideoColorMetadata Read(IMFAttributes attributes) => new(
        ReadOptional(attributes, MediaTypeAttributeKeys.YuvMatrix),
        ReadOptional(attributes, MediaTypeAttributeKeys.VideoNominalRange),
        ReadOptional(attributes, MediaTypeAttributeKeys.TransferFunction),
        ReadOptional(attributes, MediaTypeAttributeKeys.VideoPrimaries));

    private static uint? ReadOptional(IMFAttributes attributes, Guid key)
    {
        Result result = attributes.GetUInt32(key, out uint value);
        if (result == ResultCode.Attributenotfound) { return null; }
        result.CheckError();
        return value;
    }
}
