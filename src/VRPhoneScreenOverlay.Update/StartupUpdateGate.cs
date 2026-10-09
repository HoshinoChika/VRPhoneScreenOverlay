namespace VRPhoneScreenOverlay.Update;

// One delayed startup opportunity per process/window lifetime; no timer loop.
public sealed class StartupUpdateGate(TimeSpan? delay = null)
{
    private int _state;
    public void Skip() => Interlocked.Exchange(ref _state, 2);
    public async Task<bool> WaitOnceAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0) { return false; }
        await Task.Delay(delay ?? TimeSpan.FromSeconds(8), cancellationToken).ConfigureAwait(false);
        return Interlocked.CompareExchange(ref _state, 2, 1) == 1;
    }
}
