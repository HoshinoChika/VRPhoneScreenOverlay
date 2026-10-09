using System.Buffers;

namespace VRPhoneScreenOverlay.Media;

public enum MediaCodec
{
    H264,
    H265,
    Opus,
    Aac,
}

public readonly record struct MediaTimestamp(long Microseconds, int Epoch);

public interface IEncodedMediaPacket : IDisposable
{
    public MediaCodec Codec { get; }

    public MediaTimestamp Timestamp { get; }

    public long Sequence { get; }

    public ReadOnlyMemory<byte> Payload { get; }
}

public interface IMediaClock
{
    public MediaTimestamp Current { get; }

    public TimeSpan GetDelayUntil(MediaTimestamp timestamp);
}

public sealed record ShareVideoProfile(
    int MaximumWidth,
    int MaximumHeight,
    int TargetFramesPerSecond,
    int TargetBitrateBitsPerSecond,
    int MaximumBitrateBitsPerSecond)
{
    public static ShareVideoProfile InitialLandscape { get; } = new(
        1920,
        1080,
        60,
        6_000_000,
        8_000_000);

    public static ShareVideoProfile InitialPortrait { get; } = new(
        1080,
        1920,
        60,
        6_000_000,
        8_000_000);

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumHeight, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(TargetFramesPerSecond, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(TargetFramesPerSecond, 60);
        ArgumentOutOfRangeException.ThrowIfLessThan(TargetBitrateBitsPerSecond, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            MaximumBitrateBitsPerSecond,
            TargetBitrateBitsPerSecond);
    }
}

public interface IShareVideoEncoder : IAsyncDisposable
{
    public ShareVideoProfile Profile { get; }

    public ValueTask RequestKeyFrameAsync(CancellationToken cancellationToken);
}

public interface IAudioDecoder : IAsyncDisposable
{
    public MediaCodec Codec { get; }

    public int SampleRate { get; }

    public int Channels { get; }

    public DecodedAudioFrame Decode(
        ReadOnlySpan<byte> encodedPacket,
        long presentationTimeMicroseconds);
}

public interface IAudioSink : IAsyncDisposable
{
    public string OutputDeviceId { get; }

    public TimeSpan BufferedDuration { get; }

    public ValueTask WriteAsync(
        ReadOnlyMemory<short> interleavedPcm,
        CancellationToken cancellationToken);

    public void Clear();
}

public sealed class DecodedAudioFrame : IDisposable
{
    private IMemoryOwner<short>? _owner;

    internal DecodedAudioFrame(
        int sampleRate,
        int channels,
        int samplesPerChannel,
        long presentationTimeMicroseconds,
        IMemoryOwner<short> owner)
    {
        SampleRate = sampleRate;
        Channels = channels;
        SamplesPerChannel = samplesPerChannel;
        PresentationTimeMicroseconds = presentationTimeMicroseconds;
        _owner = owner;
    }

    public int SampleRate { get; }

    public int Channels { get; }

    public int SamplesPerChannel { get; }

    public long PresentationTimeMicroseconds { get; }

    public ReadOnlyMemory<short> Samples =>
        (_owner ?? throw new ObjectDisposedException(nameof(DecodedAudioFrame)))
        .Memory[..checked(SamplesPerChannel * Channels)];

    public TimeSpan Duration => TimeSpan.FromSeconds(SamplesPerChannel / (double)SampleRate);

    public void Dispose()
    {
        IMemoryOwner<short>? owner = Interlocked.Exchange(ref _owner, null);
        owner?.Dispose();
    }
}

internal sealed class OwnedMediaPacket : IEncodedMediaPacket
{
    private IMemoryOwner<byte>? _owner;
    private readonly int _length;

    public OwnedMediaPacket(
        MediaCodec codec,
        MediaTimestamp timestamp,
        long sequence,
        IMemoryOwner<byte> owner,
        int length)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, owner.Memory.Length);

        Codec = codec;
        Timestamp = timestamp;
        Sequence = sequence;
        _owner = owner;
        _length = length;
    }

    public MediaCodec Codec { get; }

    public MediaTimestamp Timestamp { get; }

    public long Sequence { get; }

    public ReadOnlyMemory<byte> Payload => (_owner ?? throw new ObjectDisposedException(
        nameof(OwnedMediaPacket))).Memory[.._length];

    public void Dispose()
    {
        IMemoryOwner<byte>? owner = Interlocked.Exchange(ref _owner, null);
        owner?.Dispose();
    }
}
