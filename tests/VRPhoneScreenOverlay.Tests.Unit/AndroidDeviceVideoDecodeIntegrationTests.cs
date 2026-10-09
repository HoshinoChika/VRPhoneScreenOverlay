using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.Media;
using Xunit.Abstractions;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class AndroidDeviceVideoDecodeIntegrationTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [DeviceFact]
    [Trait("Category", "Device")]
    [Trait("Feature", "video")]
    public async Task AuthorizedDeviceProducesDecodedNv12FramesWhenEnabled()
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

            IAndroidVideoSessionFactory factory =
                AndroidConnectionFactory.CreateDefaultVideoSessions(connection, log);
            await using IAndroidVideoSession session = await factory.OpenAsync(
                connection.Snapshot.SelectedDevice?.DeviceKey,
                new AndroidVideoOptions { PowerOnDevice = false },
                timeout.Token);

            int width = 0;
            int height = 0;
            byte[]? configuration = null;
            IH264VideoDecoder? decoder = null;
            int decodedFrames = 0;
            int mediaPackets = 0;
            try
            {
                while (decodedFrames < 1)
                {
                    using AndroidVideoStreamItem item = await session.ReadNextAsync(timeout.Token);
                    if (item.Kind == AndroidVideoStreamItemKind.Session)
                    {
                        width = item.Width;
                        height = item.Height;
                        _output.WriteLine($"Session: {width}x{height}");
                        continue;
                    }

                    if (item.Kind == AndroidVideoStreamItemKind.Configuration)
                    {
                        configuration = item.Payload.ToArray();
                        _output.WriteLine($"Configuration: {configuration.Length} bytes");
                        continue;
                    }

                    if (decoder is null && width > 0 && height > 0 && configuration is not null)
                    {
                        decoder = VideoDecoderFactory.CreateMediaFoundationH264(
                            width,
                            height,
                            60,
                            configuration);
                        _output.WriteLine($"Decoder: {decoder.BackendName}");
                    }

                    if (decoder is null)
                    {
                        continue;
                    }

                    using DecodedVideoFrame? frame = decoder.DecodePacket(
                        item.Payload.Span,
                        item.PresentationTimeMicroseconds ?? 0,
                        item.IsKeyFrame);
                    mediaPackets++;
                    _output.WriteLine(
                        $"Media packet {mediaPackets}: {item.Payload.Length} bytes, key={item.IsKeyFrame}, decoded={frame is not null}");
                    if (frame is null)
                    {
                        continue;
                    }

                    Assert.Equal(VideoPixelFormat.Nv12, frame.PixelFormat);
                    Assert.Equal(width, frame.Width);
                    Assert.Equal(height, frame.Height);
                    Assert.True(frame.Stride >= width);
                    Assert.True(frame.Pixels.Length >= width * height * 3 / 2);
                    decodedFrames++;
                }
            }
            finally
            {
                decoder?.Dispose();
            }

            Assert.Equal(1, decodedFrames);
        }
        finally
        {
            await connection.DisposeAsync();
            await log.DisposeAsync();
        }
    }
}
