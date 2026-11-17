namespace VRPhoneScreenOverlay.SteamVR;

// Uses the runtime API monitor only to reserve/release an async helper operation.
// Never holds a thread-affine monitor across await; busy callers do not queue.
internal sealed class OpenVrRegistrationGate
{
    internal object SyncRoot { get; } = new();
    internal bool Active { get; private set; }

    internal IDisposable? TryReserve()
    {
        if (!Monitor.TryEnter(SyncRoot)) { return null; }
        try
        {
            if (Active) { return null; }
            Active = true;
            return new Reservation(this);
        }
        finally { Monitor.Exit(SyncRoot); }
    }

    private sealed class Reservation(OpenVrRegistrationGate owner) : IDisposable
    {
        private OpenVrRegistrationGate? _owner = owner;
        public void Dispose()
        {
            OpenVrRegistrationGate? current = Interlocked.Exchange(ref _owner, null);
            if (current is null) { return; }
            lock (current.SyncRoot) { current.Active = false; }
        }
    }
}
