using VRPhoneScreenOverlay.App;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class UiOperationLifetimeTests
{
    [Fact]
    public async Task EveryOperationIsCancelledBeforeWaitingForDependentCallbacks()
    {
        UiOperationLifetime lifetime = new();
        using UiOperationLifetime.Lease first = lifetime.TryEnter(CancellationToken.None)!;
        using UiOperationLifetime.Lease second = lifetime.TryEnter(CancellationToken.None)!;
        using ManualResetEventSlim secondCancelled = new();
        TaskCompletionSource<bool> observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration firstCallback = first.Token.Register(() =>
            observed.SetResult(secondCancelled.Wait(TimeSpan.FromSeconds(3))));
        using CancellationTokenRegistration secondCallback = second.Token.Register(secondCancelled.Set);
        Task stop = lifetime.StopAsync();
        Assert.True(await observed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        first.Dispose();
        second.Dispose();
        await stop.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task ShutdownCancelsButWaitsForRollbackAndRejectsNewOperations()
    {
        UiOperationLifetime lifetime = new();
        using UiOperationLifetime.Lease lease = lifetime.TryEnter(CancellationToken.None)!;
        Task stop = lifetime.StopAsync();
        Assert.True(lease.Token.IsCancellationRequested);
        Assert.False(stop.IsCompleted);
        Assert.Null(lifetime.TryEnter(CancellationToken.None));
        Assert.Throws<InvalidOperationException>(lifetime.Reopen);
        lease.Dispose(); // Represents operation AND compensating rollback completion.
        await stop.WaitAsync(TimeSpan.FromSeconds(3));
        lifetime.Reopen();
        using UiOperationLifetime.Lease next = lifetime.TryEnter(CancellationToken.None)!;
        Assert.NotNull(next);
        Assert.False(next.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task CancellationCallbackFailureCannotSkipDrainingOtherOperations()
    {
        UiOperationLifetime lifetime = new();
        using UiOperationLifetime.Lease first = lifetime.TryEnter(CancellationToken.None)!;
        using UiOperationLifetime.Lease second = lifetime.TryEnter(CancellationToken.None)!;
        TaskCompletionSource observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration callback = first.Token.Register(() =>
        { observed.SetResult(); throw new InvalidOperationException("synthetic"); });
        Task stop = lifetime.StopAsync();
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        first.Dispose();
        Assert.False(stop.IsCompleted);
        second.Dispose();
        await stop.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task CompletedOperationAndRepeatedStopDoNotLeakCapacity()
    {
        UiOperationLifetime lifetime = new();
        for (int index = 0; index < 100; index++)
        {
            using UiOperationLifetime.Lease lease = lifetime.TryEnter(CancellationToken.None)!;
            Assert.NotNull(lease);
        }
        await Task.WhenAll(lifetime.StopAsync(), lifetime.StopAsync());
        Assert.True(lifetime.IsClosing);
    }
}
