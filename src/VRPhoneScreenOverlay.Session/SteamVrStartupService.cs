using VRPhoneScreenOverlay.Settings;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Session;

public sealed record SteamVrStartupStatus(bool PreferenceKnown, bool? Requested, bool Pending, int Attempts,
    bool ManifestLaunchObserved, OpenVrBindingResult Registration);

/// <summary>One serialized registration operation; three attempts per preference/runtime epoch, no queue.</summary>
public sealed class SteamVrStartupService : IAsyncDisposable
{
    private readonly Func<AppSettingsSnapshot> _settings;
    private readonly Func<bool> _ready;
    private readonly Func<bool?, CancellationToken, ValueTask<OpenVrBindingResult>> _configure;
    private readonly Action<SteamVrStartupStatus>? _observe;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private Task? _worker;
    private bool _initialized;
    private bool _wasReady;
    private long _nextAttempt;
    private SteamVrStartupStatus _snapshot;

    public SteamVrStartupService(Func<AppSettingsSnapshot> settings, Func<bool> ready, bool manifestLaunchObserved,
        Action<SteamVrStartupStatus>? observe = null)
        : this(settings, ready, manifestLaunchObserved, OpenVrStartupRegistration.ConfigureAsync, observe) { }

    internal SteamVrStartupService(Func<AppSettingsSnapshot> settings, Func<bool> ready, bool manifestLaunchObserved,
        Func<bool?, CancellationToken, ValueTask<OpenVrBindingResult>> configure, Action<SteamVrStartupStatus>? observe = null)
    {
        _settings = settings; _ready = ready; _configure = configure; _observe = observe;
        _snapshot = new(false, null, true, 0, manifestLaunchObserved,
            new(false, "OPENVR_STARTUP_PENDING", "SteamVR 启动登记等待确认"));
    }

    public SteamVrStartupStatus Snapshot => Volatile.Read(ref _snapshot);
    public Task RefreshAsync(CancellationToken token) => PollAsync(Environment.TickCount64, true, token);
    public void Start() => _worker ??= RunAsync(_stop.Token);

    private async Task RunAsync(CancellationToken token)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            { await PollAsync(Environment.TickCount64, false, token).ConfigureAwait(false); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    internal async Task PollAsync(long now, bool force, CancellationToken token)
    {
        await _operation.WaitAsync(token).ConfigureAwait(false);
        try
        {
            AppSettingsSnapshot settings = _settings();
            bool? requested = settings.StartupPreferenceKnown ? settings.Value.LaunchWithSteamVr : null;
            bool ready = _ready();
            SteamVrStartupStatus current = Snapshot;
            bool changed = !_initialized || requested != current.Requested || ready && !_wasReady;
            _wasReady = ready;
            _initialized = true;
            if (force || changed)
            { current = current with { Attempts = 0, Pending = true, PreferenceKnown = settings.StartupPreferenceKnown, Requested = requested }; _nextAttempt = now; }
            if (!current.Pending || current.Attempts >= 3 || now < _nextAttempt) { return; }
            OpenVrBindingResult result = await _configure(requested, token).ConfigureAwait(false);
            // A settings save while the utility was running is a new intent;
            // do not report the old result as confirmation of the newer choice.
            AppSettingsSnapshot latest = _settings();
            bool? latestRequested = latest.StartupPreferenceKnown ? latest.Value.LaunchWithSteamVr : null;
            bool superseded = latestRequested != requested;
            current = current with { Attempts = current.Attempts + 1, Pending = !result.Succeeded || superseded, Registration = result };
            _nextAttempt = now + (current.Attempts == 1 ? 2_000 : 5_000);
            Volatile.Write(ref _snapshot, current);
            try { _observe?.Invoke(current); }
            catch (Exception) { /* Diagnostic observers cannot interrupt registration or its bounded retry policy. */ }
        }
        finally { _operation.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_worker is not null) { await _worker.ConfigureAwait(false); }
        await _operation.WaitAsync().ConfigureAwait(false);
        _operation.Release();
        _stop.Dispose();
        _operation.Dispose();
    }
}
