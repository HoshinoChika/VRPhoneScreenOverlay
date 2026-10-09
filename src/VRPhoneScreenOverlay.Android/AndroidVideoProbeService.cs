namespace VRPhoneScreenOverlay.Android;

internal sealed class AndroidVideoProbeService(IAndroidVideoSessionFactory sessions) :
    IAndroidVideoProbeService
{
    private readonly IAndroidVideoSessionFactory _sessions = sessions;

    public async ValueTask<AndroidVideoProbeResult> ProbeAsync(
        string? deviceKey,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource timeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        try
        {
            await using IAndroidVideoSession session = await _sessions.OpenAsync(
                deviceKey,
                new AndroidVideoOptions { PowerOnDevice = false },
                timeout.Token);

            int width = 0;
            int height = 0;
            int mediaPackets = 0;
            long payloadBytes = 0;
            bool configurationReceived = false;
            bool keyFrameReceived = false;
            while (mediaPackets < 10)
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
                    configurationReceived = item.Payload.Length > 0;
                    continue;
                }

                mediaPackets++;
                payloadBytes += item.Payload.Length;
                keyFrameReceived |= item.IsKeyFrame;
            }

            return new AndroidVideoProbeResult(
                session.Codec,
                width,
                height,
                mediaPackets,
                payloadBytes,
                configurationReceived,
                keyFrameReceived);
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new AndroidConnectionException(
                AndroidReasonCodes.VideoProbeTimeout,
                "视频自检在 20 秒内没有收到足够的数据");
        }
    }
}
