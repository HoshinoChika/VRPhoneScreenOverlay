using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class LatestAndroidVideoFrameReaderTests
{
    [Fact]
    public async Task PreservesContiguousEncodedFramesAfterFirstKeyFrame()
    {
        using AndroidVideoStreamItem sessionItem = CreateSession(1080, 1920);
        using AndroidVideoStreamItem configurationItem = CreatePacket(
            AndroidVideoStreamItemKind.Configuration,
            [7, 8],
            keyFrame: false);
        using AndroidVideoStreamItem firstFrame = CreatePacket(
            AndroidVideoStreamItemKind.Media,
            [1],
            keyFrame: true);
        using AndroidVideoStreamItem secondFrame = CreatePacket(
            AndroidVideoStreamItemKind.Media,
            [2],
            keyFrame: false);
        using AndroidVideoStreamItem thirdFrame = CreatePacket(
            AndroidVideoStreamItemKind.Media,
            [3],
            keyFrame: false);
        await using FakeVideoSession session = new([
            sessionItem,
            configurationItem,
            firstFrame,
            secondFrame,
            thirdFrame,
        ]);
        await using LatestAndroidVideoFrameReader reader = new(session, CancellationToken.None);
        await Task.Delay(50);

        using AndroidLatestVideoFrame first = Assert.IsType<AndroidLatestVideoFrame>(
            await reader.ReadLatestAsync(CancellationToken.None));
        using AndroidLatestVideoFrame second = Assert.IsType<AndroidLatestVideoFrame>(
            await reader.ReadLatestAsync(CancellationToken.None));
        using AndroidLatestVideoFrame third = Assert.IsType<AndroidLatestVideoFrame>(
            await reader.ReadLatestAsync(CancellationToken.None));

        Assert.True(first.IsKeyFrame);
        Assert.Equal([1], first.Payload.ToArray());
        Assert.Equal([2], second.Payload.ToArray());
        Assert.Equal([3], third.Payload.ToArray());
        Assert.Equal(0, reader.DroppedFrames);
        Assert.Equal(3, reader.EncodedPayloadBytes);
    }

    [Fact]
    public async Task OverflowDropsDamagedGroupAndResumesAtNextKeyFrame()
    {
#pragma warning disable CA2000 // Ownership moves to FakeVideoSession and then to the reader.
        List<AndroidVideoStreamItem> items =
        [
            CreateSession(1080, 2400),
            CreatePacket(AndroidVideoStreamItemKind.Configuration, [7, 8], keyFrame: false),
            CreatePacket(AndroidVideoStreamItemKind.Media, [1], keyFrame: true),
        ];
        for (byte value = 2; value <= 34; value++)
        {
            items.Add(CreatePacket(AndroidVideoStreamItemKind.Media, [value], keyFrame: false));
        }

        items.Add(CreatePacket(AndroidVideoStreamItemKind.Media, [99], keyFrame: true));
#pragma warning restore CA2000
        await using FakeVideoSession session = new(items);
        await using LatestAndroidVideoFrameReader reader = new(session, CancellationToken.None);
        await Task.Delay(50);

        using AndroidLatestVideoFrame recovered = Assert.IsType<AndroidLatestVideoFrame>(
            await reader.ReadLatestAsync(CancellationToken.None));

        Assert.True(recovered.IsKeyFrame);
        Assert.Equal([99], recovered.Payload.ToArray());
        Assert.True(reader.DroppedFrames >= 33);
        Assert.Equal(35, reader.EncodedPayloadBytes);
    }

    [Fact]
    public async Task OverflowingKeyFrameRecoversImmediatelyWithoutWaitingForAnotherGroup()
    {
#pragma warning disable CA2000 // Test packets are transferred to the session and reader.
        List<AndroidVideoStreamItem> items =
        [
            CreateSession(1080, 2400),
            CreatePacket(AndroidVideoStreamItemKind.Configuration, [7, 8], keyFrame: false),
            CreatePacket(AndroidVideoStreamItemKind.Media, [1], keyFrame: true),
        ];
        for (byte value = 2; value <= 32; value++)
        {
            items.Add(CreatePacket(AndroidVideoStreamItemKind.Media, [value], keyFrame: false));
        }
        items.Add(CreatePacket(AndroidVideoStreamItemKind.Media, [99], keyFrame: true));
        items.Add(CreatePacket(AndroidVideoStreamItemKind.Media, [100], keyFrame: false));
#pragma warning restore CA2000
        await using FakeVideoSession session = new(items);
        await using LatestAndroidVideoFrameReader reader = new(session, CancellationToken.None);
        await session.Drained.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(1));
        using AndroidLatestVideoFrame recovered = Assert.IsType<AndroidLatestVideoFrame>(
            await reader.ReadLatestAsync(timeout.Token));
        using AndroidLatestVideoFrame following = Assert.IsType<AndroidLatestVideoFrame>(
            await reader.ReadLatestAsync(timeout.Token));
        Assert.True(recovered.IsKeyFrame);
        Assert.Equal([99], recovered.Payload.ToArray());
        Assert.Equal([100], following.Payload.ToArray());
        Assert.Equal(2, recovered.Generation);
        Assert.Equal(recovered.Generation, following.Generation);
        Assert.Equal(32, reader.DroppedFrames);
    }

    [Fact]
    public async Task RepeatedBurstsKeepPacketOwnershipBoundedAndReleaseEverythingOnStop()
    {
        OwnershipCounter counter = new();
#pragma warning disable CA2000 // Packet ownership is transferred to the fake session, then the reader.
        List<AndroidVideoStreamItem> items =
        [
            CreateSession(1080, 2400),
            CreatePacket(AndroidVideoStreamItemKind.Configuration, [7, 8], keyFrame: false),
        ];
        byte[] payload = [1];
        for (int index = 0; index < 3201; index++)
        {
            items.Add(new AndroidVideoStreamItem(AndroidVideoStreamItemKind.Media, 0, 0, false,
                index, index % 32 == 0, payload, new CountedOwner(counter)));
        }
#pragma warning restore CA2000
        await using FakeVideoSession session = new(items);
        await using LatestAndroidVideoFrameReader reader = new(session, CancellationToken.None);
        await session.Drained.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(3200, reader.DroppedFrames);
        Assert.Equal(3200, counter.Disposed);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(1));
        using (AndroidLatestVideoFrame frame = Assert.IsType<AndroidLatestVideoFrame>(
            await reader.ReadLatestAsync(timeout.Token)))
        {
            Assert.True(frame.IsKeyFrame);
            Assert.Equal(101, frame.Generation);
        }
        await reader.DisposeAsync();
        Assert.Equal(3201, counter.Disposed);
    }

    private sealed class OwnershipCounter
    {
        public int Disposed;
    }

    private sealed class CountedOwner(OwnershipCounter counter) : IDisposable
    {
        public void Dispose() => Interlocked.Increment(ref counter.Disposed);
    }

    private static AndroidVideoStreamItem CreateSession(int width, int height) => new(
        AndroidVideoStreamItemKind.Session,
        width,
        height,
        false,
        null,
        false,
        ReadOnlyMemory<byte>.Empty,
        null);

    private static AndroidVideoStreamItem CreatePacket(
        AndroidVideoStreamItemKind kind,
        byte[] bytes,
        bool keyFrame)
        => new(
            kind,
            0,
            0,
            false,
            kind == AndroidVideoStreamItemKind.Media ? 1 : null,
            keyFrame,
            bytes,
            null);

    private sealed class FakeVideoSession(IReadOnlyList<AndroidVideoStreamItem> items) :
        IAndroidVideoSession
    {
        private readonly IReadOnlyList<AndroidVideoStreamItem> _items = items;
        private int _index;
        public TaskCompletionSource Drained { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string DeviceKey => "test";

        public string DeviceName => "test";

        public AndroidVideoCodec Codec => AndroidVideoCodec.H264;

        public async ValueTask<AndroidVideoStreamItem> ReadNextAsync(
            CancellationToken cancellationToken)
        {
            int index = Interlocked.Increment(ref _index) - 1;
            if (index < _items.Count)
            {
                return _items[index];
            }

            Drained.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The cancellation wait unexpectedly completed.");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
