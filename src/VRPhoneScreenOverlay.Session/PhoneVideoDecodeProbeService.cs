using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.Media;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Session;

public interface IPhoneVideoDecodeProbeService
{
    public ValueTask<PhoneVideoDecodeProbeResult> ProbeAsync(
        string? deviceKey,
        AndroidVideoOptions options,
        CancellationToken cancellationToken);
}

public sealed record PhoneVideoDecodeProbeResult(
    AndroidVideoCodec Codec,
    int Width,
    int Height,
    VideoPixelFormat PixelFormat,
    int Stride,
    int EncodedPacketCount,
    long EncodedPayloadBytes,
    int DecodedFrameBytes,
    string DecoderBackend,
    string GpuBackend,
    int TextureSlot);

public sealed class PhoneVideoDecodeProbeService(
    IAndroidVideoSessionFactory videoSessions,
    IAndroidConnectionLogSink log, VideoDecoderPreference decoderPreference = VideoDecoderPreference.Automatic) : IPhoneVideoDecodeProbeService
{
    private readonly IAndroidVideoSessionFactory _videoSessions = videoSessions;
    private readonly IAndroidConnectionLogSink _log = log;

    public async ValueTask<PhoneVideoDecodeProbeResult> ProbeAsync(
        string? deviceKey,
        AndroidVideoOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        using CancellationTokenSource timeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            await using IAndroidVideoSession session = await _videoSessions.OpenAsync(
                deviceKey,
                options,
                timeout.Token);
            if (session.Codec != AndroidVideoCodec.H264)
            {
                throw new PhoneVideoProbeException(
                    PhoneReasonCodes.VideoDecodeCodecUnsupported,
                    $"当前视频编码 {session.Codec} 尚未接入解码器");
            }

            await using LatestAndroidVideoFrameReader encoded = new(session, timeout.Token);
            await using LatestDecodedVideoFrameReader decoded = new(encoded,
                packet => VideoDecoderFactory.CreateMediaFoundationH264(packet.Width, packet.Height,
                    options.MaximumFramesPerSecond, packet.Configuration.Span, preference: decoderPreference),
                timeout.Token, maximumPackets: 120);
            using DecodedStreamFrame? received = await decoded.ReadAsync(timeout.Token).ConfigureAwait(false);
            if (received is null)
            { throw new PhoneVideoProbeException(PhoneReasonCodes.VideoDecodeFrameLimit, "收到 120 个编码包后仍未得到解码画面"); }
            DecodedVideoFrame frame = received.Frame;
            using IGpuVideoFramePresenter presenter = GpuVideoFramePresenterFactory.CreateD3D11();
            GpuVideoFrame gpuFrame = presenter.Present(frame);
            PhoneVideoDecodeProbeResult result = new(session.Codec, frame.VisibleWidth, frame.VisibleHeight,
                frame.PixelFormat, frame.Stride, checked((int)decoded.SubmittedPackets), encoded.EncodedPayloadBytes,
                frame.Pixels.Length, received.Backend, presenter.AdapterBackend, gpuFrame.TextureSlot);
            _log.TryWrite(new(DateTimeOffset.UtcNow, "video_decode_probe", PhoneReasonCodes.VideoDecodeReady,
                "手机视频编码与解码链路正常", session.DeviceKey, VideoWidth: frame.VisibleWidth, VideoHeight: frame.VisibleHeight,
                PacketCount: decoded.SubmittedPackets, PayloadBytes: encoded.EncodedPayloadBytes));
            return result;
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new PhoneVideoProbeException(
                PhoneReasonCodes.VideoDecodeProbeTimeout,
                "30 秒内没有得到解码画面，请确认手机屏幕已点亮");
        }
        catch (MediaDecoderException exception)
        {
            throw new PhoneVideoProbeException(exception.ReasonCode, exception.Message, exception);
        }
        catch (GpuVideoPresenterException exception)
        {
            throw new PhoneVideoProbeException(exception.ReasonCode, exception.Message, exception);
        }
    }
}

public sealed class PhoneVideoProbeException(
    string reasonCode,
    string message,
    Exception? inner = null) : Exception(message, inner)
{
    public string ReasonCode { get; } = reasonCode;
}
