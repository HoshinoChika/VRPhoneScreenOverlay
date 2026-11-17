namespace VRPhoneScreenOverlay.App;

// One pending callback per state stream; at most 32 streams. Replace-oldest on
// overflow. Owner's UI thread drains; handle recreation invalidates old posts.
internal sealed class UiNotificationDispatcher : IDisposable
{
    private readonly Control _owner;
    private readonly int _thread = Environment.CurrentManagedThreadId;
    private readonly object _gate = new();
    private readonly Dictionary<string, (long Sequence, Action Callback)> _pending = new(StringComparer.Ordinal);
    private readonly System.Windows.Forms.Timer _drainTimer = new() { Interval = 16 };
    private bool _scheduled;
    private bool _disposed;
    private long _generation;
    private long _sequence;
    private string? _replayStream;
    internal long DroppedStreams { get; private set; }

    internal UiNotificationDispatcher(Control owner)
    {
        _owner = owner;
        _drainTimer.Tick += (_, _) => Drain(_generation);
        owner.HandleCreated += (_, _) => Schedule();
        owner.HandleDestroyed += (_, _) => { if (!_disposed) { _drainTimer.Stop(); } lock (_gate) { _generation++; _scheduled = false; } };
        owner.Disposed += (_, _) => Dispose();
    }

    // False means execute inline on the owning UI thread. Never use
    // InvokeRequired before handle creation: it can falsely return false there.
    internal bool Dispatch(string stream, Action callback)
    {
        if (_disposed || _owner.IsDisposed || _owner.Disposing) { return true; }
        if (Environment.CurrentManagedThreadId == _thread && _owner.IsHandleCreated)
        {
            // Consume once at the replayed handler's entry. A producer may have
            // queued a newer value after Drain removed this callback; keep it.
            if (_replayStream == stream) { _replayStream = null; return false; }
            lock (_gate) { _pending.Remove(stream); }
            return false;
        }
        lock (_gate)
        {
            if (_disposed || _owner.IsDisposed || _owner.Disposing) { return true; }
            if (!_pending.ContainsKey(stream) && _pending.Count >= 32)
            { _pending.Remove(_pending.MinBy(item => item.Value.Sequence).Key); DroppedStreams++; }
            _pending[stream] = (++_sequence, callback);
        }
        Schedule();
        return true;
    }

    private void Schedule()
    {
        long generation;
        lock (_gate)
        {
            if (_disposed || _scheduled || _pending.Count == 0 || _owner.IsDisposed || _owner.Disposing || !_owner.IsHandleCreated) { return; }
            _scheduled = true;
            generation = _generation;
        }
        try { _owner.BeginInvoke(() => StartDrainTimer(generation)); }
        catch (InvalidOperationException)
        {
            lock (_gate)
            {
                if (_generation == generation) { _scheduled = false; }
                if (_disposed || _generation != generation || _owner.IsDisposed || _owner.Disposing || !_owner.IsHandleCreated) { return; }
            }
            throw;
        }
    }

    private void StartDrainTimer(long generation)
    {
        lock (_gate)
        {
            if (_disposed || _generation != generation || _owner.IsDisposed || _owner.Disposing || !_owner.IsHandleCreated) { return; }
            if (_pending.Count == 0) { _scheduled = false; return; }
            _drainTimer.Start();
        }
    }

    private void Drain(long generation)
    {
        try
        {
            // A bounded batch per timer tick allows close/paint messages through
            // even when background state producers continuously refill the slots.
            for (int processed = 0; processed < 32; processed++)
            {
                Action callback;
                string stream;
                lock (_gate)
                {
                    if (_disposed || _generation != generation || _owner.IsDisposed || _owner.Disposing || !_owner.IsHandleCreated || _pending.Count == 0) { return; }
                    var item = _pending.MinBy(entry => entry.Value.Sequence);
                    callback = item.Value.Callback;
                    stream = item.Key;
                    _pending.Remove(item.Key);
                }
                _replayStream = stream;
                try { callback(); } // Business exceptions belong to the existing UI exception boundary.
                finally { _replayStream = null; }
            }
        }
        finally
        {
            lock (_gate)
            {
                if (!_disposed && _generation == generation && !_owner.IsDisposed && !_owner.Disposing && _pending.Count == 0)
                { _drainTimer.Stop(); _scheduled = false; }
            }
        }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) { return; }
            _disposed = true; _pending.Clear(); _generation++; _scheduled = false;
        }
        _drainTimer.Dispose();
    }

}
