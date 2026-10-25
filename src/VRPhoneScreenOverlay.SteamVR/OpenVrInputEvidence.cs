using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.SteamVR;

internal enum InputBlock
{
    None, BindingWait, Dashboard, Hidden, PoseUnavailable, Grabbed, MenuConsumed,
    ModeSuppressed, RayMiss, PointerInvisible, ActionsDisabled, ActionInactive, ActionReadFailed, GeometryWaiting
}

internal readonly record struct InputPathSample(bool ActionSetEnabled, bool ActionActive, bool ActionPressed,
    int ActionReadError, bool RayHit, bool PointerVisible, InputBlock Block, bool ObserverActive = false,
    bool ObserverPressed = false, bool AwaitingFreshVideoFrame = false);

// Fixed counters, no event queue. Sampling mutates only scalars; immutable snapshots
// are materialized at most once per second, except for explicit lifecycle flushes.
internal sealed class OpenVrInputEvidence
{
    private readonly object _gate = new();
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private readonly long[] _blocks = new long[Enum.GetValues<InputBlock>().Length];
    private long _samples, _readFailures, _active, _presses, _hits, _eligible;
    private long _down, _move, _up, _cancel, _other, _sinkFailures;
    private bool _previousPressed;
    private bool _previousObserverPressed;
    private long _observerActive, _observerPresses;
    private InputPathSample _current;
    private PhoneInputPathDiagnostic? _cached;
    private long _snapshotAt;
    private DateTimeOffset? _lastObservedAt;
    private bool _recallPressed;
    private long _recallRequests, _recallCompleted;
    private string? _lastRecallReason;
    private DateTimeOffset? _lastRecallAt;

    internal void Recall(bool pressed, bool completed, string reason)
    {
        lock (_gate)
        {
            if (pressed && !_recallPressed)
            {
                _recallRequests++;
                if (completed) { _recallCompleted++; }
                _lastRecallReason = reason;
                _lastRecallAt = DateTimeOffset.UtcNow;
            }
            _recallPressed = pressed;
        }
    }

    internal void Observe(InputPathSample sample)
    {
        lock (_gate)
        {
            _samples++;
            _lastObservedAt = DateTimeOffset.UtcNow;
            if (sample.ActionReadError != 0) { _readFailures++; }
            if (sample.ActionActive) { _active++; }
            bool pressed = sample.ActionReadError == 0 && sample.ActionActive && sample.ActionPressed;
            if (pressed && !_previousPressed) { _presses++; }
            _previousPressed = pressed;
            if (sample.ObserverActive) { _observerActive++; }
            bool observerPressed = sample.ObserverActive && sample.ObserverPressed;
            if (observerPressed && !_previousObserverPressed) { _observerPresses++; }
            _previousObserverPressed = observerPressed;
            if (sample.RayHit) { _hits++; }
            if (sample.Block == InputBlock.None) { _eligible++; }
            else { _blocks[(int)sample.Block]++; }
            _current = sample;
        }
    }

    internal void Emitted(PhoneInputCommandKind kind)
    {
        lock (_gate)
        {
            switch (kind)
            {
                case PhoneInputCommandKind.PointerDown: _down++; break;
                case PhoneInputCommandKind.PointerMove: _move++; break;
                case PhoneInputCommandKind.PointerUp: _up++; break;
                case PhoneInputCommandKind.PointerCancel: _cancel++; break;
                default: _other++; break;
            }
        }
    }

    internal void SinkFailed() { lock (_gate) { _sinkFailures++; } }

    internal PhoneInputPathDiagnostic Snapshot(long timestamp, bool force = false)
    {
        lock (_gate)
        {
            if (!force && _cached is not null && timestamp - _snapshotAt < 1000) { return _cached; }
            _snapshotAt = timestamp;
            _cached = new(_sessionId, DateTimeOffset.UtcNow, _samples, _readFailures, _active, _presses, _hits,
                _eligible, _down, _move, _up, _cancel, _other, _sinkFailures, _current.Block.ToString(),
                _current.ActionReadError, _current.ActionSetEnabled, _current.ActionActive, _current.ActionPressed,
                _current.RayHit, _current.PointerVisible, Enum.GetValues<InputBlock>()
                    .Where(block => block != InputBlock.None).Select(block => new InputBlockCount(block.ToString(), _blocks[(int)block])).ToArray())
            {
                ObserverActiveSamples = _observerActive,
                LastObservedAt = _lastObservedAt,
                ObserverPressEdges = _observerPresses,
                ObserverActive = _current.ObserverActive,
                ObserverPressed = _current.ObserverPressed,
                AwaitingFreshVideoFrame = _current.AwaitingFreshVideoFrame,
                RecallRequests = _recallRequests,
                RecallCompleted = _recallCompleted,
                LastRecallReason = _lastRecallReason,
                LastRecallAt = _lastRecallAt
            };
            return _cached;
        }
    }

    internal static InputBlock BlockFor(bool dashboard, bool visible, bool validPose, bool grabbed,
        bool menuConsumed, bool suppressed, bool hit, bool pointerVisible, bool actionsEnabled, bool active, int readError)
        => dashboard ? InputBlock.Dashboard : !visible ? InputBlock.Hidden : !validPose ? InputBlock.PoseUnavailable :
            grabbed ? InputBlock.Grabbed : menuConsumed ? InputBlock.MenuConsumed : suppressed ? InputBlock.ModeSuppressed :
            !hit ? InputBlock.RayMiss : !pointerVisible ? InputBlock.PointerInvisible : !actionsEnabled ? InputBlock.ActionsDisabled :
            readError != 0 ? InputBlock.ActionReadFailed : !active ? InputBlock.ActionInactive : InputBlock.None;
}
