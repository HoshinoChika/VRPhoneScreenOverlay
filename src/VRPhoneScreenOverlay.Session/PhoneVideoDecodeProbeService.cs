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
    IAndroidConnectionLogSink log) : IPhoneVideoDecodeProbeService
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

            int width = 0;
            int height = 0;
            int encodedPackets = 0;
            long encodedBytes = 0;
            byte[]? configuration = null;
            IH264VideoDecoder? decoder = null;
            IGpuVideoFramePresenter? presenter = null;
            try
            {
                while (encodedPackets < 120)
                {
                    using AndroidVideoStreamItem item = await session.ReadNextAsync(timeout.Token);
                    if (item.Kind == AndroidVideoStreamItemKind.Session)
                    {
                        width = item.Width;
                        height = item.Height;
                        continue;
                    }

                    if (item.Kind == AndroidVideoStreamItemKind.Configuration)
                    {
                        configuration = item.Payload.ToArray();
                        continue;
                    }

                    encodedPackets++;
                    encodedBytes += item.Payload.Length;
                    if (decoder is null && width > 0 && height > 0 && configuration is not null)
                    {
                        decoder = VideoDecoderFactory.CreateMediaFoundationH264(
                            width,
                            height,
                            options.MaximumFramesPerSecond,
                            configuration);
                        presenter = GpuVideoFramePresenterFactory.CreateD3D11();
                    }

                    if (decoder is null)
                    {
                        continue;
                    }

                    using DecodedVideoFrame? frame = decoder.DecodePacket(
                        item.Payload.Span,
                        item.PresentationTimeMicroseconds ?? 0,
                        item.IsKeyFrame);
                    if (frame is null)
                    {
                        continue;
                    }

                    GpuVideoFrame gpuFrame = presenter!.Present(frame);

                    PhoneVideoDecodeProbeResult result = new(
                        session.Codec,
                        frame.VisibleWidth,
                        frame.VisibleHeight,
                        frame.PixelFormat,
                        frame.Stride,
                        encodedPackets,
                        encodedBytes,
                        frame.Pixels.Length,
                        decoder.BackendName,
                        presenter.AdapterBackend,
                        gpuFrame.TextureSlot);
                    _log.TryWrite(new AndroidConnectionLogEntry(
                        DateTimeOffset.UtcNow,
                        "video_decode_probe",
                        PhoneReasonCodes.VideoDecodeReady,
                        "手机视频编码与解码链路正常",
                        session.DeviceKey,
                        VideoWidth: frame.VisibleWidth,
                        VideoHeight: frame.VisibleHeight,
                        PacketCount: encodedPackets,
                        PayloadBytes: encodedBytes));
                    return result;
                }

                throw new PhoneVideoProbeException(
                    PhoneReasonCodes.VideoDecodeFrameLimit,
                    "收到 120 个编码包后仍未得到解码画面");
            }
            finally
            {
                presenter?.Dispose();
                decoder?.Dispose();
            }
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
