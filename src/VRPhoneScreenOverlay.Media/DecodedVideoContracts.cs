using System.Buffers;

namespace VRPhoneScreenOverlay.Media;

public enum VideoPixelFormat
{
    Nv12,
}

public sealed class DecodedVideoFrame : IDisposable
{
    private IMemoryOwner<byte>? _owner;
    private readonly int _length;

    internal DecodedVideoFrame(
        int width,
        int height,
        int stride,
        int visibleWidth,
        int visibleHeight,
        VideoPixelFormat pixelFormat,
        long presentationTimeMicroseconds,
        IMemoryOwner<byte> owner,
        int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, width);
        ArgumentOutOfRangeException.ThrowIfLessThan(visibleWidth, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(visibleWidth, width);
        ArgumentOutOfRangeException.ThrowIfLessThan(visibleHeight, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(visibleHeight, height);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, owner.Memory.Length);

        Width = width;
        Height = height;
        Stride = stride;
        VisibleWidth = visibleWidth;
        VisibleHeight = visibleHeight;
        PixelFormat = pixelFormat;
        PresentationTimeMicroseconds = presentationTimeMicroseconds;
        _owner = owner;
        _length = length;
    }

    public int Width { get; }

    public int Height { get; }

    public int Stride { get; }

    public int VisibleWidth { get; }

    public int VisibleHeight { get; }

    public VideoPixelFormat PixelFormat { get; }

    public long PresentationTimeMicroseconds { get; }

    public ReadOnlyMemory<byte> Pixels => (_owner ?? throw new ObjectDisposedException(
        nameof(DecodedVideoFrame))).Memory[.._length];

    public void Dispose()
    {
        IMemoryOwner<byte>? owner = Interlocked.Exchange(ref _owner, null);
        owner?.Dispose();
    }
}

public interface IH264VideoDecoder : IDisposable
{
    public string BackendName { get; }

    public DecodedVideoFrame? DecodePacket(
        ReadOnlySpan<byte> annexBPayload,
        long presentationTimeMicroseconds,
        bool keyFrame);

    public DecodedVideoFrame? Drain();
}

public static class VideoDecoderFactory
{
    public static IH264VideoDecoder CreateMediaFoundationH264(
        int width,
        int height,
        int framesPerSecond,
        ReadOnlySpan<byte> codecConfiguration) =>
        new MediaFoundationH264Decoder(
            width,
            height,
            framesPerSecond,
            codecConfiguration);
}

public sealed class MediaDecoderException(string reasonCode, string message, Exception? inner = null) :
    Exception(message, inner)
{
    public string ReasonCode { get; } = reasonCode;
}
