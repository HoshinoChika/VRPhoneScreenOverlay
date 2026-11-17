using SharpGen.Runtime;

namespace VRPhoneScreenOverlay.Media;

internal static class MftStreamIdentifiers
{
    internal static (int Input, int Output) Resolve(int inputCount, int outputCount, Action<int[], int[]> query)
    {
        // This decoder owns one H.264 input and one NV12 output. Do not silently
        // select stream zero when a transform requires additional streams.
        if (inputCount != 1 || outputCount != 1)
        {
            throw new MediaDecoderException(MediaReasonCodes.H264InitializationFailed,
                "Windows H.264 解码器需要不支持的多流配置");
        }
        int[] inputs = new int[1];
        int[] outputs = new int[1];
        try { query(inputs, outputs); }
        catch (SharpGenException error) when (error.ResultCode.Code == unchecked((int)0x80004001))
        {
            // IMFTransform::GetStreamIDs permits E_NOTIMPL only for fixed,
            // consecutive identifiers starting at zero. Ignore any partial writes.
            return (0, 0);
        }
        return (inputs[0], outputs[0]);
    }
}
