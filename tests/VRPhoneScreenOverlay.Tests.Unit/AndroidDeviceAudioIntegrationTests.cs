using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.Media;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class AndroidDeviceAudioIntegrationTests
{
    [DeviceFact]
    [Trait("Category", "Device")]
    [Trait("Feature", "audio")]
    public async Task AuthorizedDeviceProducesAndPlaysInternalAudioWhenEnabled()
    {
        IAndroidConnectionLogSink log = AndroidConnectionFactory.CreateDefaultLog();
        IAndroidConnectionService connection = AndroidConnectionFactory.CreateDefault(log);
        try
        {
            await connection.StartAsync(CancellationToken.None);
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
            while (!connection.Snapshot.IsReady)
            {
                timeout.Token.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
            }

            Assert.True(connection.Snapshot.SelectedDevice?.Capabilities.InternalAudio.IsAvailable);
            IAndroidAudioSessionFactory sessions =
                AndroidConnectionFactory.CreateDefaultAudioSessions(connection, log);
            await using IAndroidAudioSession session = await sessions.OpenAsync(
                connection.Snapshot.SelectedDevice?.DeviceKey,
                new AndroidAudioOptions(),
                timeout.Token);
            await using IAudioDecoder decoder = AudioPipelineFactory.CreateOpusDecoder();
            await using IAudioSink sink = await AudioPipelineFactory.CreateDefaultWasapiSinkAsync();
            int decodedPackets = 0;
            while (decodedPackets < 10)
            {
                using AndroidAudioStreamItem item = await session.ReadNextAsync(timeout.Token);
                if (item.Kind != AndroidAudioStreamItemKind.Media)
                {
                    continue;
                }

                using DecodedAudioFrame frame = decoder.Decode(
                    item.Payload.Span,
                    item.PresentationTimeMicroseconds ?? 0);
                await sink.WriteAsync(frame.Samples, timeout.Token);
                decodedPackets++;
            }

            Assert.Equal(10, decodedPackets);
            Assert.True(sink.BufferedDuration > TimeSpan.Zero);
        }
        finally
        {
            await connection.DisposeAsync();
            await log.DisposeAsync();
        }
    }
}
