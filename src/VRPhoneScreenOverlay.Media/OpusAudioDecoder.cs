using System.Buffers;
using Concentus;

namespace VRPhoneScreenOverlay.Media;

internal sealed class OpusAudioDecoder : IAudioDecoder
{
    private const int _maximumSamplesPerChannel = 5760;
    private readonly IOpusDecoder _decoder;
    private bool _disposed;

    public OpusAudioDecoder(int sampleRate = 48_000, int channels = 2)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(sampleRate, 48_000);
        if (channels is not 1 and not 2)
        {
            throw new ArgumentOutOfRangeException(nameof(channels));
        }

        SampleRate = sampleRate;
        Channels = channels;
        _decoder = OpusCodecFactory.CreateDecoder(sampleRate, channels, null);
    }

    public MediaCodec Codec => MediaCodec.Opus;

    public int SampleRate { get; }

    public int Channels { get; }

    public DecodedAudioFrame Decode(
        ReadOnlySpan<byte> encodedPacket,
        long presentationTimeMicroseconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (encodedPacket.IsEmpty)
        {
            throw new ArgumentException("Opus packet must not be empty.", nameof(encodedPacket));
        }

        IMemoryOwner<short> owner = MemoryPool<short>.Shared.Rent(
            checked(_maximumSamplesPerChannel * Channels));
        try
        {
            int samplesPerChannel = _decoder.Decode(
                encodedPacket,
                owner.Memory.Span,
                _maximumSamplesPerChannel,
                false);
            DecodedAudioFrame frame = new(
                SampleRate,
                Channels,
                samplesPerChannel,
                presentationTimeMicroseconds,
                owner);
            owner = null!;
            return frame;
        }
        catch (OpusException exception)
        {
            throw new AudioPipelineException(
                MediaReasonCodes.AudioOpusDecodeFailed,
                "Opus 音频包解码失败",
                exception);
        }
        finally
        {
            owner?.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _decoder.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
