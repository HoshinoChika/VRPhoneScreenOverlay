namespace VRPhoneScreenOverlay.SteamVR;

internal readonly record struct OpenVrSharedPlayspaceInput(
    bool PhonePollerActive,
    bool PlayspaceEnabled,
    bool DragPressed,
    bool ResetPressed,
    float Multiplier = 1,
    OpenVrPlayspaceInputSample InputSample = default,
    long InputRevision = 0,
    bool FlingEnabled = false,
    OpenVrPlayspaceMotionOptions? MotionOptions = null,
    OpenVrMotionSaveState MotionSaveState = OpenVrMotionSaveState.None);

internal enum OpenVrMotionSaveState { None, Pending, Saved, Failed }

internal sealed record OpenVrPlayspaceControlRequest(bool Enabled, float Multiplier,
    bool? FlingEnabled = null, OpenVrPlayspaceMotionOptions? MotionOptions = null);

internal sealed class OpenVrActionUpdateCoordinator
{
    private readonly object _gate = new();
    private bool _phonePollerActive;
    private bool _playspaceEnabled;
    private OpenVrPlayspaceInputSample _inputSample;
    private long _inputRevision;
    private float _multiplier = 1;
    private bool _flingEnabled;
    private OpenVrMotionSaveState _motionSaveState;
    private OpenVrPlayspaceMotionOptions _motionOptions = OpenVrPlayspaceMotionOptions.Default;
    // One latest-value command slot, owned by the playspace worker. New clicks
    // coalesce while the worker is busy; no command is created per pointer move.
    private OpenVrPlayspaceControlRequest? _pendingControls;
    private long _coalescedControls;

    public long CoalescedControls { get { lock (_gate) { return _coalescedControls; } } }

    public void RequestControls(bool enabled, float multiplier, bool? flingEnabled = null, OpenVrPlayspaceMotionOptions? motionOptions = null)
    {
        if (!float.IsFinite(multiplier) || multiplier is < 1 or > 40)
        {
            throw new ArgumentOutOfRangeException(nameof(multiplier));
        }
        lock (_gate)
        {
            if (_pendingControls is not null) { _coalescedControls++; }
            _pendingControls = new OpenVrPlayspaceControlRequest(enabled, multiplier,
                flingEnabled ?? _pendingControls?.FlingEnabled, motionOptions ?? _pendingControls?.MotionOptions);
        }
    }

    public void SetMotionSaveState(OpenVrMotionSaveState state)
    {
        lock (_gate) { _motionSaveState = state; }
    }

    public void SetMotion(bool enabled, OpenVrPlayspaceMotionOptions options)
    {
        lock (_gate) { _flingEnabled = enabled; _motionOptions = options; }
    }

    public OpenVrPlayspaceControlRequest? TakeControls()
    {
        lock (_gate)
        {
            OpenVrPlayspaceControlRequest? request = _pendingControls;
            _pendingControls = null;
            return request;
        }
    }

    public void SetPlayspaceMultiplier(float multiplier)
    {
        lock (_gate)
        {
            _multiplier = multiplier;
            _pendingControls = null;
        }
    }

    public IDisposable AcquirePhonePoller()
    {
        lock (_gate)
        {
            if (_phonePollerActive)
            {
                throw new InvalidOperationException(
                    "Only one SteamVR phone action-state poller may be active.");
            }

            _phonePollerActive = true;
            _inputSample = default;
            return new PhonePollerLease(this);
        }
    }

    public void SetPlayspaceEnabled(bool enabled)
    {
        lock (_gate)
        {
            _playspaceEnabled = enabled;
            _pendingControls = null;
        }
    }

    public void PublishPlayspaceInput(OpenVrPlayspaceInputSample inputSample)
    {
        lock (_gate)
        {
            if (!_phonePollerActive)
            {
                return;
            }

            // Retain raw input and the Dashboard state sampled for the same
            // UpdateActionState call. Consumers distinguish reuse from a release.
            _inputSample = inputSample;
            _inputRevision++;
        }
    }

    public OpenVrSharedPlayspaceInput Read()
    {
        lock (_gate)
        {
            return new OpenVrSharedPlayspaceInput(
                _phonePollerActive,
                _playspaceEnabled,
                _playspaceEnabled && _inputSample.IsValid && !_inputSample.DashboardVisible && _inputSample.DragPressed,
                _inputSample.IsValid && !_inputSample.DashboardVisible && _inputSample.ResetPressed,
                _multiplier,
                _inputSample,
                _inputRevision, _flingEnabled, _motionOptions, _motionSaveState);
        }
    }

    private void ReleasePhonePoller()
    {
        lock (_gate)
        {
            _phonePollerActive = false;
            _inputSample = default;
            _pendingControls = null;
        }
    }

    private sealed class PhonePollerLease(OpenVrActionUpdateCoordinator owner) : IDisposable
    {
        private OpenVrActionUpdateCoordinator? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ReleasePhonePoller();
    }
}
