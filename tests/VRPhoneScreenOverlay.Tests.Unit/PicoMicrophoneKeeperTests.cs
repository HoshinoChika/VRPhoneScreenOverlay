using VRPhoneScreenOverlay.Media;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class PicoMicrophoneKeeperTests
{
    [Fact]
    public async Task DisabledDoesNotOpenDeviceAndStopReleasesSession()
    {
        int opens = 0;
        using FakeSession session = new();
        using PicoMicrophoneKeeper keeper = new(() => { Interlocked.Increment(ref opens); return session; });
        Assert.Equal(0, opens);
        Assert.Equal(PicoMicrophoneState.Disabled, keeper.State);
        keeper.Configure(true);
        await WaitForAsync(() => keeper.State == PicoMicrophoneState.Draining);
        Assert.Equal(480, keeper.ConsumedFrames);
        keeper.Configure(true);
        Assert.Equal(1, opens);
        keeper.Configure(false);
        await WaitForAsync(() => keeper.State == PicoMicrophoneState.Disabled);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task RapidToggleNeverOpensOverlappingConsumers()
    {
        int active = 0;
        int peak = 0;
        using PicoMicrophoneKeeper keeper = new(() => new FakeSession(() =>
        {
            int current = Interlocked.Increment(ref active);
            peak = Math.Max(peak, current);
        }, () => Interlocked.Decrement(ref active)));
        keeper.Configure(true);
        await WaitForAsync(() => keeper.State == PicoMicrophoneState.Draining);
        for (int index = 0; index < 20; index++) { keeper.Configure(false); keeper.Configure(true); }
        await WaitForAsync(() => keeper.State == PicoMicrophoneState.Draining);
        keeper.Dispose();
        Assert.Equal(1, peak);
        Assert.Equal(0, active);
    }

    [Fact]
    public async Task MissingDeviceRetriesAndFindsItWithoutChangingSetting()
    {
        int attempts = 0;
        using PicoMicrophoneKeeper keeper = new(() => Interlocked.Increment(ref attempts) == 1 ? null : new FakeSession());
        keeper.Configure(true);
        await WaitForAsync(() => keeper.State == PicoMicrophoneState.Draining);
        Assert.True(attempts >= 2);
    }

    [Fact]
    public async Task CaptureFailureReleasesSessionThenRecovers()
    {
        int attempts = 0;
        using FakeSession failed = new(() => throw new InvalidOperationException());
        using PicoMicrophoneKeeper keeper = new(() => Interlocked.Increment(ref attempts) == 1 ? failed : new FakeSession());
        keeper.Configure(true);
        await WaitForAsync(() => keeper.State == PicoMicrophoneState.Retrying);
        Assert.True(failed.Disposed);
        await WaitForAsync(() => keeper.State == PicoMicrophoneState.Draining);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(8));
        while (!condition()) { await Task.Delay(10, timeout.Token); }
    }

    private sealed class FakeSession(Action? start = null, Action? stop = null) : IPicoMicrophoneSession
    {
        public bool Disposed { get; private set; }
        public void Drain(Action<int> consumed, CancellationToken cancellationToken)
        {
            start?.Invoke();
            try
            {
                consumed(480);
                cancellationToken.WaitHandle.WaitOne();
            }
            finally { stop?.Invoke(); }
        }
        public void Dispose() => Disposed = true;
    }
}
