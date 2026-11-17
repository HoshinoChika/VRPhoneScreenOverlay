using System.Buffers;
using VRPhoneScreenOverlay.Contracts;

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
    // Assigned once by the decoder before ownership leaves its serialized output
    // stage. Zero keeps compatibility with synchronous/custom decoders that do
    // not publish through concurrent return-value and notification paths.
    public long OutputSequence { get; internal set; }
    public VideoColorMetadata Color { get; internal set; }
    public VideoGeometryDiagnostic? SourceGeometry { get; init; }

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

    public event EventHandler? OutputAvailable { add { } remove { } }
    public DecodedVideoFrame? TakeOutput() => null;
    public long ReplacedOutputFrames => 0;
    public string DiagnosticState => "synchronous-compatible";
    public ValueTask<DecodedVideoFrame?> DecodePacketAsync(ReadOnlyMemory<byte> payload,
        long presentationTimeMicroseconds, bool keyFrame, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return ValueTask.FromResult(DecodePacket(payload.Span, presentationTimeMicroseconds, keyFrame));
    }
    public ValueTask<DecodedVideoFrame?> DrainAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Drain());
    }

    public DecodedVideoFrame? DecodePacket(
        ReadOnlySpan<byte> annexBPayload,
        long presentationTimeMicroseconds,
        bool keyFrame);

    public DecodedVideoFrame? Drain();
}

public enum VideoDecoderPreference { Automatic, Software }

public static class VideoDecoderFactory
{
    public static IH264VideoDecoder CreateMediaFoundationH264(
        int width,
        int height,
        int framesPerSecond,
        ReadOnlySpan<byte> codecConfiguration,
        Action<DecoderInitializationEvent>? observe = null,
        VideoDecoderPreference preference = VideoDecoderPreference.Automatic) =>
        new MediaFoundationH264Decoder(
            width,
            height,
            framesPerSecond,
            codecConfiguration, observe, preference);
}

public sealed class MediaDecoderException(string reasonCode, string message, Exception? inner = null) :
    Exception(message, inner), IVideoGeometryFailure
{
    public string ReasonCode { get; } = reasonCode;
    public VideoGeometryDiagnostic? Geometry { get; init; }
}
