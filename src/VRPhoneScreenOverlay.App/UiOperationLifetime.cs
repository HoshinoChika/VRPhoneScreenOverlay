namespace VRPhoneScreenOverlay.App;

/// <summary>At most 16 active UI operations, no waiting queue; closing rejects new work.</summary>
internal sealed class UiOperationLifetime
{
    private readonly object _gate = new();
    private readonly HashSet<Lease> _active = [];
    private bool _closing;
    private TaskCompletionSource? _drained;

    public bool IsClosing { get { lock (_gate) { return _closing; } } }

    public Lease? TryEnter(CancellationToken lifetime)
    {
        lock (_gate)
        {
            if (_closing || _active.Count >= 16) { return null; }
            Lease lease = new(this, lifetime);
            _active.Add(lease);
            return lease;
        }
    }

    public async Task StopAsync()
    {
        Lease[] active;
        Task drained;
        lock (_gate)
        {
            _closing = true;
            active = _active.ToArray();
            drained = active.Length == 0 ? Task.CompletedTask
                : (_drained ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
        // Signal all operations before waiting for any callback: one cancellation may
        // depend on another operation also observing cancellation. Capacity is at most 16.
        await Task.WhenAll(active.Select(CancelLeaseAsync)).ConfigureAwait(false);
        await drained.ConfigureAwait(false);
    }

    private static async Task CancelLeaseAsync(Lease lease)
    {
        try { await lease.CancelAsync().ConfigureAwait(false); }
        catch (ObjectDisposedException) { /* Operation already completed during cancellation. */ }
        catch (AggregateException) { /* Cancellation was delivered; still await rollback and lease release. */ }
    }

    public void Reopen()
    {
        lock (_gate)
        {
            if (!_closing) { return; }
            if (_active.Count != 0) { throw new InvalidOperationException("UI operations have not drained."); }
            _closing = false;
            _drained = null;
        }
    }

    private void Complete(Lease lease)
    {
        lock (_gate)
        {
            _active.Remove(lease);
            if (_active.Count == 0) { _drained?.TrySetResult(); }
        }
    }

    internal sealed class Lease(UiOperationLifetime owner, CancellationToken lifetime) : IDisposable
    {
        private readonly CancellationTokenSource _source = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        private int _disposed;
        public CancellationToken Token => _source.Token;
        public Task CancelAsync() => _source.CancelAsync();
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
            _source.Dispose();
            owner.Complete(this);
        }
    }
}
