namespace VRPhoneScreenOverlay.Android;

internal sealed class ScrcpyAudioSession(
    string deviceKey,
    string deviceName,
    AndroidAudioCodec codec,
    ScrcpyTransportLease transport,
    ScrcpyAudioProtocolReader protocol,
    IAndroidConnectionLogSink log) : IAndroidAudioSession
{
    private readonly ScrcpyTransportLease _transport = transport;
    private readonly ScrcpyAudioProtocolReader _protocol = protocol;
    private readonly IAndroidConnectionLogSink _log = log;
    private long _packetCount;
    private long _payloadBytes;
    private bool _disposed;

    public string DeviceKey { get; } = deviceKey;

    public string DeviceName { get; } = deviceName;

    public AndroidAudioCodec Codec { get; } = codec;

    public async ValueTask<AndroidAudioStreamItem> ReadNextAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        AndroidAudioStreamItem item = await _protocol.ReadNextAsync(cancellationToken)
            .ConfigureAwait(false);
        _packetCount++;
        _payloadBytes += item.Payload.Length;
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
        _log.TryWrite(new AndroidConnectionLogEntry(
            DateTimeOffset.UtcNow,
            "audio_stopped",
            AndroidReasonCodes.AudioStopped,
            "手机内部音频流已停止",
            DeviceKey,
            PacketCount: _packetCount,
            PayloadBytes: _payloadBytes));
    }
}
