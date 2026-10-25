using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Android;

// The dispatcher is the sole caller of SendAsync. Metadata reads and cancellation
// packets therefore share the same ordered writer, including an in-flight Down.
internal sealed class ControlGeometryWriter(AndroidDisplaySize initial,
    Func<CancellationToken, ValueTask<AndroidDisplaySize>> readSize,
    Func<PhoneInputCommand, CancellationToken, ValueTask> write)
{
    private readonly object _gate = new();
    private AndroidDisplaySize _native = initial;
    private PhoneInputCommand? _active;
    private int _videoWidth, _videoHeight;
    private long _revision, _refreshes, _failures, _cancelAttempts, _cancelWrites, _suppressed;
    private bool _waiting;
    private DateTimeOffset? _verified = DateTimeOffset.UtcNow;
    private string _reason = "startup-size";
    private int? _lastKnownRotation = initial.Rotation;
    private int _consecutiveTimeouts;
    private bool _unverified;

    internal ControlGeometryDiagnostic Snapshot
    {
        get
        {
            lock (_gate)
            {
                return new(_native.Width, _native.Height, _videoWidth, _videoHeight, _revision,
                _refreshes, _failures, _cancelAttempts, _cancelWrites, _suppressed, _waiting, _verified, _reason)
                {
                    Rotation = _native.Rotation,
                    RotationStatus = _native.RotationStatus,
                    ConsecutiveRefreshTimeouts = _consecutiveTimeouts,
                    NativeSizeUnverified = _unverified
                };
            }
        }
    }

    internal async ValueTask<PhoneInputWriteResult> SendAsync(PhoneInputCommand command, CancellationToken token)
    {
        if (command.Kind == PhoneInputCommandKind.SynchronizeDisplayGeometry)
        { return new(false, await RefreshAsync(command.Sequence, token).ConfigureAwait(false)); }
        if (IsStale(command)) { return SuppressStale(false); }
        bool positioned = command.Kind is PhoneInputCommandKind.PointerDown or PhoneInputCommandKind.PointerMove or
            PhoneInputCommandKind.PointerUp or PhoneInputCommandKind.PointerCancel or PhoneInputCommandKind.Scroll;
        lock (_gate)
        {
            if (_unverified && positioned)
            { _suppressed++; return new(false, false, "PHONE_CONTROL_GEOMETRY_UNVERIFIED"); }
        }
        bool terminated = false;
        bool sourceChanged;
        lock (_gate)
        {
            sourceChanged = positioned && _videoWidth > 0 &&
            (command.ScreenWidth != _videoWidth || command.ScreenHeight != _videoHeight);
        }
        // An explicit terminal always closes the old gesture using its old mapping.
        if (sourceChanged && command.Kind != PhoneInputCommandKind.PointerCancel)
        {
            terminated = await CancelActiveAsync(command.Sequence, "video-size-changed", token).ConfigureAwait(false);
            terminated |= await RefreshAsync(command.Sequence, token).ConfigureAwait(false);
            lock (_gate) { _waiting = true; _reason = "video-size-changed"; }
        }
        if (positioned && command.Kind != PhoneInputCommandKind.PointerCancel)
        { lock (_gate) { _videoWidth = command.ScreenWidth; _videoHeight = command.ScreenHeight; } }
        if (IsStale(command)) { return SuppressStale(terminated); }
        if (command.Kind == PhoneInputCommandKind.PointerDown)
        { terminated |= await CancelActiveAsync(command.Sequence, "new-down-replaced-active", token).ConfigureAwait(false); }

        PhoneInputCommand mapped;
        lock (_gate)
        {
            if (_unverified && positioned)
            { _suppressed++; return new(false, terminated, "PHONE_CONTROL_GEOMETRY_UNVERIFIED"); }
            if (command.Kind is PhoneInputCommandKind.PointerMove or PhoneInputCommandKind.PointerUp or PhoneInputCommandKind.PointerCancel && _active is null)
            {
                _suppressed++; _reason = "waiting-for-new-down";
                return new(false, terminated, "PHONE_CONTROL_GEOMETRY_GESTURE_SUPPRESSED");
            }
            mapped = command.Kind == PhoneInputCommandKind.PointerCancel && _active is { } active
                ? active with { Kind = PhoneInputCommandKind.PointerCancel, Sequence = command.Sequence, CreatedAt = command.CreatedAt }
                : _native.Map(command);
        }
        await write(mapped, token).ConfigureAwait(false);
        lock (_gate)
        {
            if (command.Kind == PhoneInputCommandKind.PointerDown) { _active = mapped; _waiting = false; }
            else if (command.Kind == PhoneInputCommandKind.PointerMove) { _active = mapped; }
            else if (command.Kind is PhoneInputCommandKind.PointerUp or PhoneInputCommandKind.PointerCancel) { _active = null; }
        }
        return new(true, terminated);
    }

    private bool IsStale(PhoneInputCommand command)
    {
        lock (_gate)
        {
            if (command.GeometryRevision is not { } revision) { return false; }
            if (command.Kind is PhoneInputCommandKind.PointerUp or PhoneInputCommandKind.PointerCancel)
            { return _active?.GeometryRevision is { } activeRevision && revision != activeRevision; }
            return command.Kind is PhoneInputCommandKind.PointerDown or PhoneInputCommandKind.PointerMove or PhoneInputCommandKind.Scroll && revision != _revision;
        }
    }

    private PhoneInputWriteResult SuppressStale(bool terminated)
    {
        lock (_gate) { _suppressed++; _reason = "stale-geometry-command"; if (_active is null) { _waiting = true; } }
        return new(false, terminated, "PHONE_CONTROL_GEOMETRY_STALE_COMMAND");
    }

    private async ValueTask<bool> RefreshAsync(long sequence, CancellationToken token)
    {
        lock (_gate) { _refreshes++; }
        AndroidDisplaySize next;
        try
        {
            next = await readSize(token).ConfigureAwait(false);
            if (next.Width is < 1 or > ushort.MaxValue || next.Height is < 1 or > ushort.MaxValue)
            { throw new AndroidConnectionException("ANDROID_CONTROL_DISPLAY_REFRESH_FAILED", "手机显示尺寸无效，已停止触控以避免坐标偏移"); }
        }
        catch (AndroidConnectionException error) when (error.ReasonCode == "ANDROID_CONTROL_DISPLAY_REFRESH_TIMEOUT" && !token.IsCancellationRequested)
        {
            lock (_gate) { _failures++; _consecutiveTimeouts++; _reason = "native-size-unverified"; }
            bool cancelled = await CancelActiveAsync(sequence, "native-size-unverified", token).ConfigureAwait(false);
            lock (_gate)
            {
                if (!_unverified) { _revision++; }
                _unverified = true; _waiting = true;
                if (_consecutiveTimeouts >= 3) { throw; }
            }
            return cancelled;
        }
        catch
        {
            lock (_gate) { _failures++; _reason = "native-size-unverified"; }
            throw;
        }
        bool changed;
        lock (_gate)
        {
            changed = _unverified || next.Width != _native.Width || next.Height != _native.Height ||
            (next.Rotation is { } rotation && _lastKnownRotation is { } known && rotation != known);
        }
        bool terminated = changed && await CancelActiveAsync(sequence, "native-geometry-changed", token).ConfigureAwait(false);
        lock (_gate)
        {
            _native = next; _verified = DateTimeOffset.UtcNow;
            _unverified = false; _consecutiveTimeouts = 0;
            if (next.Rotation is not null) { _lastKnownRotation = next.Rotation; }
            if (changed) { _revision++; _waiting = true; _reason = "native-geometry-changed"; }
        }
        return terminated;
    }

    private async ValueTask<bool> CancelActiveAsync(long sequence, string reason, CancellationToken token)
    {
        PhoneInputCommand? active;
        lock (_gate) { active = _active; if (active is not null) { _cancelAttempts++; _reason = reason; } }
        if (active is null) { return false; }
        await write(active.Value with
        {
            Kind = PhoneInputCommandKind.PointerCancel,
            Sequence = sequence,
            CreatedAt = DateTimeOffset.UtcNow
        }, token).ConfigureAwait(false);
        lock (_gate) { _active = null; _waiting = true; _cancelWrites++; }
        return true;
    }
}
