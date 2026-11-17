namespace VRPhoneScreenOverlay.Android;

// One worker per video session, no queue and no per-frame work. Stop after success.
internal sealed class AndroidVideoClockRecovery : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private AndroidVideoClockSynchronizer? _current;

    internal AndroidVideoClockRecovery(AndroidVideoClockSynchronizer? initial,
        Func<CancellationToken, ValueTask<AndroidVideoClockSynchronizer?>> synchronize,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _current = initial;
        _worker = initial is null ? RecoverAsync(synchronize, delay ?? Task.Delay) : Task.CompletedTask;
    }

    internal AndroidVideoClockSynchronizer? Current => Volatile.Read(ref _current);

    private async Task RecoverAsync(Func<CancellationToken, ValueTask<AndroidVideoClockSynchronizer?>> synchronize,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        int attempt = 0;
        try
        {
            while (true)
            {
                TimeSpan pause = TimeSpan.FromSeconds(attempt switch { 0 => 2, 1 => 10, 2 => 30, _ => 60 });
                await delay(pause, _stop.Token).ConfigureAwait(false);
                _stop.Token.ThrowIfCancellationRequested();
                AndroidVideoClockSynchronizer? clock = await synchronize(_stop.Token).ConfigureAwait(false);
                _stop.Token.ThrowIfCancellationRequested();
                if (clock is not null)
                {
                    Volatile.Write(ref _current, clock);
                    return;
                }
                attempt = Math.Min(attempt + 1, 3);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        try { await _worker.ConfigureAwait(false); }
        finally { _stop.Dispose(); }
    }
}
