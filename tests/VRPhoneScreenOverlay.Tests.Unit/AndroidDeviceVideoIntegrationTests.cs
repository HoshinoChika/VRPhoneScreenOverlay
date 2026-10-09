using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class AndroidDeviceVideoIntegrationTests
{
    [DeviceFact]
    [Trait("Category", "Device")]
    [Trait("Feature", "video")]
    public async Task AuthorizedDeviceProducesScrcpyVideoPacketsWhenEnabled()
    {
        IAndroidConnectionLogSink log = AndroidConnectionFactory.CreateDefaultLog();
        IAndroidConnectionService connection = AndroidConnectionFactory.CreateDefault(log);
        try
        {
            await connection.StartAsync(CancellationToken.None);
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
            while (!connection.Snapshot.IsReady)
            {
                timeout.Token.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromMilliseconds(100), timeout.Token);
            }

            IAndroidVideoProbeService probe =
                AndroidConnectionFactory.CreateDefaultVideoProbe(connection, log);
            AndroidVideoProbeResult result = await probe.ProbeAsync(
                connection.Snapshot.SelectedDevice?.DeviceKey,
                timeout.Token);

            Assert.Equal(AndroidVideoCodec.H264, result.Codec);
            Assert.True(result.Width > 0);
            Assert.True(result.Height > 0);
            Assert.Equal(10, result.MediaPacketCount);
            Assert.True(result.ConfigurationReceived);
            Assert.True(result.KeyFrameReceived);
            Assert.True(result.PayloadBytes > 0);
        }
        finally
        {
            await connection.DisposeAsync();
            await log.DisposeAsync();
        }
    }
}
