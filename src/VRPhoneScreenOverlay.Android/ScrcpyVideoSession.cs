namespace VRPhoneScreenOverlay.Android;

internal sealed class ScrcpyVideoSession(
    string deviceKey,
    string deviceName,
    AndroidVideoCodec codec,
    ScrcpyTransportLease transport,
    ScrcpyVideoProtocolReader protocol,
    AndroidVideoClockSynchronizer? videoClock,
    IAndroidConnectionLogSink log) : IAndroidVideoSession
{
    private readonly ScrcpyTransportLease _transport = transport;
    private readonly ScrcpyVideoProtocolReader _protocol = protocol;
    private readonly AndroidVideoClockSynchronizer? _videoClock = videoClock;
    private readonly IAndroidConnectionLogSink _log = log;
    private long _packetCount;
    private long _payloadBytes;
    private int _width;
    private int _height;
    private bool _disposed;

    public string DeviceKey { get; } = deviceKey;

    public string DeviceName { get; } = deviceName;

    public AndroidVideoCodec Codec { get; } = codec;

    public async ValueTask<AndroidVideoStreamItem> ReadNextAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        AndroidVideoStreamItem item = await _protocol.ReadNextAsync(cancellationToken)
            .ConfigureAwait(false);
        if (item.Kind == AndroidVideoStreamItemKind.Session)
        {
            _width = item.Width;
            _height = item.Height;
            WriteLog(
                "video_session",
                AndroidReasonCodes.VideoSession,
                $"视频会话尺寸 {item.Width}x{item.Height}",
                width: item.Width,
                height: item.Height);
        }
        else
        {
            _packetCount++;
            _payloadBytes += item.Payload.Length;
            if (item.PresentationTimeMicroseconds is long presentationTime &&
                _videoClock is not null)
            {
                item.EstimatedCaptureTimestamp =
                    _videoClock.EstimateLocalCaptureTimestamp(presentationTime);
            }
        }

        return item;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _transport.DisposeAsync().ConfigureAwait(false);
        WriteLog(
            "video_stopped",
            AndroidReasonCodes.VideoStopped,
            "手机编码视频流已停止",
            _width,
            _height,
            _packetCount,
            _payloadBytes);
    }

    private void WriteLog(
        string eventName,
        string reasonCode,
        string message,
        int? width = null,
        int? height = null,
        long? packetCount = null,
        long? payloadBytes = null)
    {
        _log.TryWrite(new AndroidConnectionLogEntry(
            DateTimeOffset.UtcNow,
            eventName,
            reasonCode,
            message,
            DeviceKey,
            VideoWidth: width,
            VideoHeight: height,
            PacketCount: packetCount,
            PayloadBytes: payloadBytes));
    }
}
