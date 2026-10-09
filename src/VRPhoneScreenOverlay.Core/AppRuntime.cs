using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Core;

public sealed class AppRuntimeStateChangedEventArgs : EventArgs
{
    public AppRuntimeStateChangedEventArgs(AppRuntimeSnapshot previous, AppRuntimeSnapshot current)
    {
        Previous = previous;
        Current = current;
    }

    public AppRuntimeSnapshot Previous { get; }

    public AppRuntimeSnapshot Current { get; }
}

public sealed class AppRuntime : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AppRuntimeSnapshot _snapshot = NewSnapshot(
        AppLifecycleState.Created,
        AppReasonCodes.Created,
        "工程运行时已创建");
    private bool _disposed;

    public event EventHandler<AppRuntimeStateChangedEventArgs>? StateChanged;

    public AppRuntimeSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_snapshot.State is AppLifecycleState.Ready or AppLifecycleState.Starting)
            {
                return;
            }

            EnsureState(AppLifecycleState.Created, AppLifecycleState.Stopped);
            Transition(AppLifecycleState.Starting, AppReasonCodes.Starting, "正在启动工程运行时");
            Transition(AppLifecycleState.Ready, AppReasonCodes.Ready, "工程运行时已就绪");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_snapshot.State == AppLifecycleState.Stopped)
            {
                return;
            }

            if (_snapshot.State == AppLifecycleState.Created)
            {
                Transition(AppLifecycleState.Stopped, AppReasonCodes.NotStarted, "工程运行时未启动");
                return;
            }

            EnsureState(AppLifecycleState.Starting, AppLifecycleState.Ready, AppLifecycleState.Degraded);
            Transition(AppLifecycleState.Stopping, AppReasonCodes.Stopping, "正在停止工程运行时");
            Transition(AppLifecycleState.Stopped, AppReasonCodes.Stopped, "工程运行时已停止");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _disposed = true;
        _gate.Dispose();
    }

    private static AppRuntimeSnapshot NewSnapshot(
        AppLifecycleState state,
        string reasonCode,
        string reasonMessage) => new(
            state,
            StateReason.Normal(reasonCode, reasonMessage),
            DateTimeOffset.UtcNow);

    private void Transition(AppLifecycleState state, string reasonCode, string reasonMessage)
    {
        AppRuntimeSnapshot previous = _snapshot;
        AppRuntimeSnapshot current = NewSnapshot(state, reasonCode, reasonMessage);
        Volatile.Write(ref _snapshot, current);
        StateChanged?.Invoke(this, new AppRuntimeStateChangedEventArgs(previous, current));
    }

    private void EnsureState(params AppLifecycleState[] allowed)
    {
        if (!allowed.Contains(_snapshot.State))
        {
            throw new InvalidOperationException(
                $"State {_snapshot.State} cannot perform this transition.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
