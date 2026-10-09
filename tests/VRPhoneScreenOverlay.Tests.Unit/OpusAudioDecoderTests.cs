using Concentus;
using Concentus.Enums;
using VRPhoneScreenOverlay.Media;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OpusAudioDecoderTests
{
    [Fact]
    public async Task DecodesStereoOpusPacketIntoPcm()
    {
        const int samplesPerChannel = 960;
        short[] silence = new short[samplesPerChannel * 2];
        byte[] encoded = new byte[4000];
        using IOpusEncoder encoder = OpusCodecFactory.CreateEncoder(
            48_000,
            2,
            OpusApplication.OPUS_APPLICATION_AUDIO,
            null);
        int encodedLength = encoder.Encode(silence, samplesPerChannel, encoded, encoded.Length);
        await using IAudioDecoder decoder = AudioPipelineFactory.CreateOpusDecoder();

        using DecodedAudioFrame frame = decoder.Decode(encoded.AsSpan(0, encodedLength), 42);

        Assert.Equal(48_000, frame.SampleRate);
        Assert.Equal(2, frame.Channels);
        Assert.Equal(samplesPerChannel, frame.SamplesPerChannel);
        Assert.Equal(samplesPerChannel * 2, frame.Samples.Length);
        Assert.Equal(42, frame.PresentationTimeMicroseconds);
    }
}
