namespace VRPhoneScreenOverlay.Settings;

public sealed record MotionPreferences(float Multiplier, bool InertiaEnabled, float Strength, float Gravity, float Friction, bool ResetAll)
{
    public static MotionPreferences From(AppSettings value) => new(value.PlayspaceMultiplier, value.PlayspaceInertiaEnabled,
        value.PlayspaceFlingStrength, value.PlayspaceGravity, value.PlayspaceFriction, value.PlayspaceResetAllOffsets);
    public AppSettings Apply(AppSettings value) => value with
    {
        PlayspaceMultiplier = Multiplier,
        PlayspaceInertiaEnabled = InertiaEnabled,
        PlayspaceFlingStrength = Strength,
        PlayspaceGravity = Gravity,
        PlayspaceFriction = Friction,
        PlayspaceResetAllOffsets = ResetAll,
    };
}

// One worker and one latest-value slot; new requests replace pending values.
// Batches are at most four per second, each write has a three-second deadline.
public sealed class MotionSettingsWriter(IAppSettingsService settings)
{
    private readonly object _gate = new();
    private MotionPreferences? _pending;
    private MotionPreferences? _latest;
    private Task _worker = Task.CompletedTask;
    private bool _failed;
    public event Action<bool>? Saved;
    public long CoalescedUpdates { get; private set; }
    public bool HasPending { get { lock (_gate) { return _pending is not null || !_worker.IsCompleted; } } }

    public void Request(MotionPreferences value)
    {
        lock (_gate)
        {
            if (_pending is not null) { CoalescedUpdates++; }
            _pending = _latest = value;
            if (_worker.IsCompleted) { _worker = Task.Run(RunAsync); }
        }
    }

    private async Task RunAsync()
    {
        while (true)
        {
            await Task.Delay(250).ConfigureAwait(false);
            MotionPreferences? value;
            lock (_gate) { value = _pending; _pending = null; }
            if (value is null) { return; }
            bool succeeded = false;
            try
            {
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(3));
                await settings.SaveMotionAsync(value, timeout.Token).ConfigureAwait(false);
                succeeded = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException) { }
            lock (_gate) { _failed = !succeeded; }
            Saved?.Invoke(succeeded);
            lock (_gate)
            {
                if (_pending is null)
                {
                    // Mark idle before releasing the lock so a simultaneous request
                    // always starts a worker instead of leaving its value stranded.
                    _worker = Task.CompletedTask;
                    return;
                }
            }
        }
    }

    public async Task FlushAsync()
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            Task worker;
            lock (_gate) { worker = _worker; }
            await worker.ConfigureAwait(false);
            lock (_gate)
            {
                if (_pending is not null || !_worker.IsCompleted) { attempt--; continue; }
                if (!_failed) { return; }
                if (attempt == 0 && _latest is not null) { Request(_latest); }
            }
        }
        throw new IOException("Motion settings were not saved.");
    }
}
