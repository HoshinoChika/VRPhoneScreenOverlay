using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Session;

public enum BindingSynchronizationState { Waiting, Saved, Applying, Applied, Failed }
public sealed record BindingSynchronizationSnapshot(BindingSynchronizationState State, string ReasonCode, string Message);
public sealed class BindingSynchronizationChangedEventArgs(BindingSynchronizationSnapshot snapshot) : EventArgs
{
    public BindingSynchronizationSnapshot Snapshot { get; } = snapshot;
}

/// <summary>Coalesces saved edits and serializes their live application with default restoration.</summary>
public sealed class PhoneBindingSynchronizationService : IAsyncDisposable
{
    private readonly ISavedBindingPreparation _saved;
    private readonly IBindingLiveApplication _live;
    private readonly PhoneBindingDefaultsService _defaults;
    private readonly Func<bool> _ready;
    private readonly Func<string?> _accepted;
    private readonly SemaphoreSlim _operation = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _worker;
    // One pending latest-content slot; polling never queues operations behind a reset.
    private string? _pending;
    private string? _applied;
    private string? _failed;
    private int _retryRequested;
    private bool _disposed;
    private BindingSynchronizationSnapshot _snapshot = new(BindingSynchronizationState.Waiting, "BINDING_SYNC_WAITING", "等待绑定修改");

    public PhoneBindingSynchronizationService(Func<bool> ready) : this(new OpenVrSavedBindingPreparation(), new OpenVrLiveBindingApplication(),
        new PhoneBindingDefaultsService(), ready, () => OpenVrBindingRecovery.DiagnosticSnapshot.Revision)
    { }

    internal PhoneBindingSynchronizationService(ISavedBindingPreparation saved, IBindingLiveApplication live, PhoneBindingDefaultsService defaults,
        Func<bool> ready, Func<string?> accepted)
    { _saved = saved; _live = live; _defaults = defaults; _ready = ready; _accepted = accepted; }

    public BindingSynchronizationSnapshot Snapshot => Volatile.Read(ref _snapshot);
    public event EventHandler<BindingSynchronizationChangedEventArgs>? Changed;
    public void RequestRetry()
    {
        if (Snapshot.State == BindingSynchronizationState.Failed) { Interlocked.Exchange(ref _retryRequested, 1); }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _worker ??= RunAsync(_lifetime.Token);
    }

    private async Task RunAsync(CancellationToken token)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(500));
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            { await PollAsync(token).ConfigureAwait(false); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    internal async Task PollAsync(CancellationToken token)
    {
        if (!_ready() || !await _operation.WaitAsync(0, token).ConfigureAwait(false)) { return; }
        try
        {
            if (Interlocked.Exchange(ref _retryRequested, 0) != 0) { _failed = null; _pending = null; }
            string revision = _saved.ReadRevision();
            if (revision == (_applied ?? _accepted())) { _pending = null; _failed = null; return; }
            if (_pending != revision)
            {
                _pending = revision;
                _failed = null;
                Publish(BindingSynchronizationState.Saved, "BINDING_SYNC_SAVED", "绑定已保存，等待写入完成…");
                return;
            }
            if (_failed == revision) { return; }
            await ApplyAsync(revision, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        { _pending = null; }
        finally { _operation.Release(); }
    }

    private async Task ApplyAsync(string revision, CancellationToken token)
    {
        IBindingDefaultsTransaction? files = null;
        IBindingLiveTransaction? application = null;
        Publish(BindingSynchronizationState.Applying, "BINDING_SYNC_APPLYING", "正在应用新绑定…");
        try
        {
            files = await _saved.PrepareAsync(revision, token).ConfigureAwait(false);
            application = await _live.BeginAsync(token).ConfigureAwait(false);
            await application.ApplySavedAsync(files.RuntimeBindings, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            application.Commit(revision);
            files.Commit();
            _applied = revision;
            _pending = null;
            bool latest = false;
            try { latest = _saved.ReadRevision() == revision; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { }
            Publish(latest ? BindingSynchronizationState.Applied : BindingSynchronizationState.Saved,
                latest ? "BINDING_SYNC_APPLIED" : "BINDING_SYNC_SAVED", latest ? "新绑定已生效" : "又有新绑定保存，正在继续同步…");
        }
        catch (Exception failure)
        {
            bool incompleteSave = files is null && application is null && failure is InvalidDataException;
            List<Exception> recovery = [];
            if (files is not null)
            {
                try { await files.DisposeAsync().ConfigureAwait(false); }
                catch (Exception error) { recovery.Add(error); }
                files = null;
            }
            if (application is not null)
            {
                try { await application.DisposeAsync().ConfigureAwait(false); }
                catch (Exception error) { recovery.Add(error); }
                application = null;
            }
            if (failure is OperationCanceledException && token.IsCancellationRequested) { throw; }
            if (incompleteSave && recovery.Count == 0)
            {
                _pending = null;
                Publish(BindingSynchronizationState.Saved, "BINDING_SYNC_SAVE_PENDING", "绑定保存尚未完整，原绑定保持不变");
                return;
            }
            _failed = revision;
            Publish(BindingSynchronizationState.Failed, recovery.Count == 0 ? "BINDING_SYNC_ROLLED_BACK" : "BINDING_SYNC_RECOVERY_FAILED",
                recovery.Count == 0 ? "新绑定已保存，但应用失败；原绑定已保留，请再次保存后重试" : "绑定应用及恢复未完成，请重新打开手柄绑定检查");
        }
        finally
        {
            if (files is not null) { await files.DisposeAsync().ConfigureAwait(false); }
            if (application is not null) { await application.DisposeAsync().ConfigureAwait(false); }
        }
    }

    public async Task RestoreDefaultsAsync(ControllerDiscoverySnapshot? controller, string? selectedKey, CancellationToken token)
    {
        await _operation.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await _defaults.RestoreAsync(controller, selectedKey, token).ConfigureAwait(false);
            _applied = null;
            _pending = null;
            _failed = null;
            Publish(BindingSynchronizationState.Applied, "BINDING_DEFAULTS_APPLIED", "默认绑定已生效");
        }
        finally { _operation.Release(); }
    }

    public async Task<bool> SynchronizeNowAsync(CancellationToken token)
    {
        await _operation.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!_ready()) { return false; }
            string revision = _saved.ReadRevision();
            if (revision == (_applied ?? _accepted())) { return true; }
            await Task.Delay(150, token).ConfigureAwait(false);
            if (_saved.ReadRevision() != revision) { throw new SettingsApplyException("BINDING_SYNC_SAVE_PENDING", "绑定正在保存，请稍后再打开浮窗"); }
            await ApplyAsync(revision, token).ConfigureAwait(false);
            if (_applied != revision) { throw new SettingsApplyException(Snapshot.ReasonCode, Snapshot.Message); }
            return true;
        }
        finally { _operation.Release(); }
    }

    public async ValueTask PrepareClosedSessionAsync(Func<ValueTask> prepare, CancellationToken token)
    {
        await _operation.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await prepare().ConfigureAwait(false);
            _applied = null;
            _pending = null;
            _failed = null;
        }
        finally { _operation.Release(); }
    }

    private void Publish(BindingSynchronizationState state, string code, string message)
    {
        BindingSynchronizationSnapshot next = new(state, code, message);
        if (next == Snapshot) { return; }
        Volatile.Write(ref _snapshot, next);
        Changed?.Invoke(this, new(next));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) { return; }
        _disposed = true;
        await _lifetime.CancelAsync().ConfigureAwait(false);
        if (_worker is not null) { await _worker.ConfigureAwait(false); }
        await _operation.WaitAsync().ConfigureAwait(false);
        _operation.Release();
        _operation.Dispose();
        _lifetime.Dispose();
    }
}
