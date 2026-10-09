using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Android;

internal sealed class ScrcpyAudioSessionFactory(
    AndroidConnectionService connection,
    ScrcpySessionLauncher launcher,
    IAndroidConnectionLogSink log) : IAndroidAudioSessionFactory
{
    private static readonly TimeSpan _startupTimeout = TimeSpan.FromSeconds(15);
    private readonly AndroidConnectionService _connection = connection;
    private readonly ScrcpySessionLauncher _launcher = launcher;
    private readonly IAndroidConnectionLogSink _log = log;

    public async ValueTask<IAndroidAudioSession> OpenAsync(
        string? deviceKey,
        AndroidAudioOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Codec != AndroidAudioCodec.Opus)
        {
            throw new AndroidConnectionException(
                AndroidReasonCodes.AudioCodecUnsupported,
                "当前版本只启用低延迟 Opus 音频");
        }

        if (!_connection.TryResolveReadyDevice(deviceKey, out ResolvedAndroidDevice device))
        {
            throw new AndroidConnectionException(
                AndroidReasonCodes.AudioDeviceNotReady,
                "没有已授权且可用的安卓手机");
        }

        AndroidDeviceDetails? details = _connection.Snapshot.SelectedDevice;
        AndroidCapability audioCapability = details?.Capabilities.InternalAudio ??
            AndroidDeviceCapabilities.Unknown.InternalAudio;
        if (audioCapability.State == AndroidCapabilityState.Unavailable)
        {
            throw new AndroidConnectionException(audioCapability.ReasonCode, audioCapability.Message);
        }

        OperationDeadline deadline = OperationDeadline.Start(_startupTimeout);
        ScrcpyTransportLease transport = await _launcher.LaunchAsync(
                new ScrcpySessionLaunchRequest(
                    device,
                    ScrcpySessionKind.Audio,
                    scid => BuildServerArguments(device.Serial, scid, options),
                    deadline),
                cancellationToken)
            .ConfigureAwait(false);
        try
        {
            ScrcpyAudioProtocolReader protocol = new(transport.Stream);
            ScrcpyAudioHandshake handshake = await ReadHandshakeAsync(
                    protocol,
                    deadline,
                    cancellationToken)
                .ConfigureAwait(false);
            WriteLog(
                "audio_connected",
                AndroidReasonCodes.AudioConnected,
                $"手机内部音频流已连接，编码 {handshake.Codec}",
                device.DeviceKey);
            return new ScrcpyAudioSession(
                device.DeviceKey,
                handshake.DeviceName,
                handshake.Codec,
                transport,
                protocol,
                _log);
        }
        catch
        {
            await transport.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async ValueTask<ScrcpyAudioHandshake> ReadHandshakeAsync(
        ScrcpyAudioProtocolReader protocol,
        OperationDeadline deadline,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadlineSource =
            deadline.CreateCancellationSource(cancellationToken);
        try
        {
            return await protocol.ReadHandshakeAsync(deadlineSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AndroidConnectionException(
                AndroidReasonCodes.AudioHandshakeTimeout,
                "等待手机音频协议握手超时");
        }
    }

    private static string[] BuildServerArguments(
        string serial,
        int scid,
        AndroidAudioOptions options)
    {
        List<string> arguments =
        [
            "-s",
            serial,
            "shell",
            $"CLASSPATH={ScrcpyProtocolConstants.DeviceServerPath}",
            "app_process",
            "/",
            "com.genymobile.scrcpy.Server",
            ScrcpyProtocolConstants.ServerVersion,
            $"scid={scid:x8}",
            "log_level=info",
            "video=false",
            "audio=true",
            "audio_codec=opus",
            "audio_source=output",
            "control=false",
            "clipboard_autosync=false",
            "tunnel_forward=true",
        ];
        if (!options.PowerOnDevice)
        {
            arguments.Add("power_on=false");
        }

        return [.. arguments];
    }

    private void WriteLog(
        string eventName,
        string reasonCode,
        string message,
        string deviceKey)
    {
        _log.TryWrite(new AndroidConnectionLogEntry(
            DateTimeOffset.UtcNow,
            eventName,
            reasonCode,
            message,
            deviceKey));
    }
}
