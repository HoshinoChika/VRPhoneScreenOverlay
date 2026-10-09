using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.Session;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class PhoneKeyguardMonitorTests
{
    [Fact]
    public async Task DisabledMonitorDoesNotProbeAndCancellationStopsTheOnlyProbe()
    {
        Probe probe = new();
        await using Log log = new();
        PhoneKeyguardMonitor monitor = new(probe, log, TimeSpan.FromMilliseconds(1));
        using CancellationTokenSource lifetime = new();
        bool enabled = false;
        int disabledSamples = 0;
        Task running = monitor.RunAsync(null, () => enabled, state =>
        {
            if (state is null && ++disabledSamples == 3) { enabled = true; }
        }, lifetime.Token);
        await probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(3, disabledSamples);
        Assert.Equal(1, probe.Count);
        await lifetime.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.True(probe.Cancelled);
    }

    private sealed class Probe : IAndroidKeyguardStateService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Count { get; private set; }
        public bool Cancelled { get; private set; }
        public async ValueTask<AndroidKeyguardSnapshot> ProbeAsync(string? deviceKey, CancellationToken cancellationToken)
        {
            Count++;
            Started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            finally { Cancelled = true; }
            return new(AndroidKeyguardState.Unknown, AndroidReasonCodes.KeyguardUnknown, "unknown");
        }
    }

    private sealed class Log : IAndroidConnectionLogSink
    {
        public string? CurrentLogPath => null;
        public bool TryWrite(AndroidConnectionLogEntry entry) => true;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
