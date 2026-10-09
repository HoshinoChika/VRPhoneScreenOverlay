using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.Session;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class PhoneAudioServiceTests
{
    [Fact]
    public async Task ExhaustedAutomaticRetriesPublishFaultedState()
    {
        AlwaysFailingAudioSessionFactory sessions = new();
        await using PhoneAudioService service = new(
            sessions,
            NullAndroidConnectionLog.Instance,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(1));
        TaskCompletionSource faulted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        service.StateChanged += (_, eventArgs) =>
        {
            if (eventArgs.Snapshot.State == PhoneAudioState.Faulted)
            {
                faulted.TrySetResult();
            }
        };

        await service.StartAsync("device", CancellationToken.None);
        await faulted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(PhoneAudioState.Faulted, service.Snapshot.State);
        Assert.Equal("AUDIO_RECOVERY_EXHAUSTED", service.Snapshot.ReasonCode);
        Assert.Equal(3, sessions.OpenCount);
    }

    [Fact]
    public async Task StartupTimeoutCancelsBackgroundWorker()
    {
        BlockingAudioSessionFactory sessions = new();
        await using PhoneAudioService service = new(
            sessions,
            NullAndroidConnectionLog.Instance,
            TimeSpan.FromMilliseconds(80));

        await service.StartAsync(null, CancellationToken.None);
        await sessions.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(PhoneAudioState.Degraded, service.Snapshot.State);
        Assert.Equal("AUDIO_START_TIMEOUT", service.Snapshot.ReasonCode);
        Assert.Equal(1, sessions.OpenCount);

        await service.StopAsync(CancellationToken.None);
        Assert.Equal(PhoneAudioState.Stopped, service.Snapshot.State);
    }

    [Fact]
    public async Task CallerCancellationReportsStartCancelledRatherThanStopped()
    {
        BlockingAudioSessionFactory sessions = new();
        await using PhoneAudioService service = new(
            sessions,
            NullAndroidConnectionLog.Instance,
            TimeSpan.FromSeconds(5));
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(80));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await service.StartAsync(null, cancellation.Token));
        await sessions.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(PhoneAudioState.Stopped, service.Snapshot.State);

        // The state is Stopped either way; the code says why. Audio that was cancelled during
        // start never played, so reporting it as AUDIO_STOPPED would mislead a diagnostic bundle.
        Assert.Equal("AUDIO_START_CANCELLED", service.Snapshot.ReasonCode);
    }

    [Fact]
    public async Task ConcurrentStopsDuringStartupDisposeWorkerOnlyOnceAndLeaveStoppedState()
    {
        BlockingAudioSessionFactory sessions = new();
        await using PhoneAudioService service = new(
            sessions,
            NullAndroidConnectionLog.Instance,
            TimeSpan.FromSeconds(5));

        Task startTask = service.StartAsync(null, CancellationToken.None).AsTask();
        await sessions.Opened.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Task firstStop = service.StopAsync(CancellationToken.None).AsTask();
        Task secondStop = service.StopAsync(CancellationToken.None).AsTask();
        await Task.WhenAll(firstStop, secondStop).WaitAsync(TimeSpan.FromSeconds(1));
        await startTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(PhoneAudioState.Stopped, service.Snapshot.State);
        Assert.Equal("AUDIO_STOPPED", service.Snapshot.ReasonCode);
        Assert.Equal(1, sessions.OpenCount);
    }

    private sealed class BlockingAudioSessionFactory : IAndroidAudioSessionFactory
    {
        private int _openCount;

        public int OpenCount => Volatile.Read(ref _openCount);

        public TaskCompletionSource Opened { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<IAndroidAudioSession> OpenAsync(
            string? deviceKey,
            AndroidAudioOptions options,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _openCount);
            Opened.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved.TrySetResult();
                throw;
            }

            throw new InvalidOperationException("The blocking test session unexpectedly completed.");
        }
    }

    private sealed class AlwaysFailingAudioSessionFactory : IAndroidAudioSessionFactory
    {
        private int _openCount;

        public int OpenCount => Volatile.Read(ref _openCount);

        public ValueTask<IAndroidAudioSession> OpenAsync(
            string? deviceKey,
            AndroidAudioOptions options,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _openCount);
            throw new AndroidConnectionException(
                "ANDROID_AUDIO_DEVICE_NOT_READY",
                "test device unavailable");
        }
    }
}
