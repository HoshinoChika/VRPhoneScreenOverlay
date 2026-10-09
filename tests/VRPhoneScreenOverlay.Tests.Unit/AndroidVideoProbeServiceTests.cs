using System.Diagnostics.CodeAnalysis;
using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class AndroidVideoProbeServiceTests
{
    [Fact]
    public async Task ReturnsOnlyBoundedVideoSummaryToPresentationLayer()
    {
        await using FakeVideoSession session = new();
        AndroidVideoProbeService probe = new(new FakeVideoSessionFactory(session));

        AndroidVideoProbeResult result = await probe.ProbeAsync(
            "test-device",
            CancellationToken.None);

        Assert.Equal(AndroidVideoCodec.H264, result.Codec);
        Assert.Equal(1080, result.Width);
        Assert.Equal(2400, result.Height);
        Assert.Equal(10, result.MediaPacketCount);
        Assert.Equal(60, result.PayloadBytes);
        Assert.True(result.ConfigurationReceived);
        Assert.True(result.KeyFrameReceived);
        Assert.True(session.IsDisposed);
    }

    private sealed class FakeVideoSessionFactory(FakeVideoSession session) :
        IAndroidVideoSessionFactory
    {
        private readonly FakeVideoSession _session = session;

        public ValueTask<IAndroidVideoSession> OpenAsync(
            string? deviceKey,
            AndroidVideoOptions options,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IAndroidVideoSession>(_session);
    }

    private sealed class FakeVideoSession : IAndroidVideoSession
    {
        private int _itemIndex;

        public string DeviceKey => "test-device";

        public string DeviceName => "Test Phone";

        public AndroidVideoCodec Codec => AndroidVideoCodec.H264;

        public bool IsDisposed { get; private set; }

        [SuppressMessage(
            "Reliability",
            "CA2000:Dispose objects before losing scope",
            Justification = "The fake source transfers each disposable packet to the service under test, which disposes it.")]
        public ValueTask<AndroidVideoStreamItem> ReadNextAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AndroidVideoStreamItem item = _itemIndex switch
            {
                0 => new AndroidVideoStreamItem(
                    AndroidVideoStreamItemKind.Session,
                    1080,
                    2400,
                    false,
                    null,
                    false,
                    ReadOnlyMemory<byte>.Empty,
                    null),
                1 => CreatePacket(AndroidVideoStreamItemKind.Configuration, 4, false),
                _ => CreatePacket(AndroidVideoStreamItemKind.Media, 6, _itemIndex == 2),
            };
            _itemIndex++;
            return ValueTask.FromResult(item);
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }

        private static AndroidVideoStreamItem CreatePacket(
            AndroidVideoStreamItemKind kind,
            int length,
            bool keyFrame) =>
            new(
                kind,
                0,
                0,
                false,
                kind == AndroidVideoStreamItemKind.Media ? 1 : null,
                keyFrame,
                new byte[length],
                null);
    }
}
