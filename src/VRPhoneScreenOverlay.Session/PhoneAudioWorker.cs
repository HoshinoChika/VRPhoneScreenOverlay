namespace VRPhoneScreenOverlay.Session;

public sealed class PhoneAudioWorker : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private Task? _stopTask;
    private int _stopRequested;
    private int _lifetimeDisposed;

    public PhoneAudioWorker(Func<CancellationToken, Task> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        RunTask = Task.Run(() => run(_lifetime.Token), CancellationToken.None);
    }

    public Task RunTask { get; }

    public bool IsStopRequested => Volatile.Read(ref _stopRequested) != 0;

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            if (Interlocked.Exchange(ref _lifetimeDisposed, 1) == 0)
            {
                _lifetime.Dispose();
            }
        }
    }

    private ValueTask StopAsync()
    {
        Task stopTask;
        lock (_gate)
        {
            _stopTask ??= StopCoreAsync();
            stopTask = _stopTask;
        }

        return new ValueTask(stopTask);
    }

    private async Task StopCoreAsync()
    {
        Interlocked.Exchange(ref _stopRequested, 1);
        await _lifetime.CancelAsync().ConfigureAwait(false);
        try
        {
            await RunTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }
}
