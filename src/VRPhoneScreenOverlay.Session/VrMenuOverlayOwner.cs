using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Session;

// The application owns the native overlay; a phone session owns only its lease.
// One lease, no queue. Submission and detach are serialized so a retired video
// session cannot publish into a later connection or runtime generation.
public sealed class VrMenuOverlayOwner : IDisposable
{
    private readonly object _gate = new();
    private IOpenVrSceneOverlay? _overlay;
    private VideoLease? _lease;
    private bool _disposed;
    private bool _keepAwake;
    private bool _keypad;
    private bool _suspended;
    public bool IsReady { get { lock (_gate) { return _overlay is not null && !_disposed && !_suspended; } } }
    public void SuspendVideoAcquisition() { lock (_gate) { _suspended = true; } }

    public ValueTask SwitchControllerHandAsync(OpenVrControllerHand hand, CancellationToken token)
    {
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            return _overlay?.SwitchControllerHandAsync(hand, token) ?? ValueTask.CompletedTask;
        }
    }

    public void ConfigureLockScreenFeatures(bool keepAwake, bool keypad)
    {
        lock (_gate)
        {
            _keepAwake = keepAwake;
            _keypad = keypad;
            _overlay?.ConfigureLockScreenFeatures(keepAwake, keypad);
        }
    }

    public void Attach(IOpenVrSceneOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_overlay is not null) { throw new InvalidOperationException("VR menu already attached."); }
            overlay.EnableStandaloneMenu();
            overlay.ConfigureLockScreenFeatures(_keepAwake, _keypad);
            _overlay = overlay;
            _suspended = false;
        }
    }

    public IOpenVrSceneOverlay AcquireVideo()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_overlay is null || _suspended) { throw new InvalidOperationException("VR menu is not ready."); }
            if (_lease is not null) { throw new InvalidOperationException("Phone video already attached."); }
            _lease = new VideoLease(this, _overlay);
            return _lease;
        }
    }

    public void Detach()
    {
        IOpenVrSceneOverlay? overlay;
        lock (_gate)
        {
            _lease?.Retire();
            _lease = null;
            overlay = _overlay;
            _overlay = null;
        }
        // Disposal joins the input thread: never hold the owner gate here.
        overlay?.Dispose();
    }

    public void Dispose()
    {
        lock (_gate) { _disposed = true; }
        Detach();
    }

    private sealed class VideoLease : IOpenVrSceneOverlay
    {
        private readonly VrMenuOverlayOwner _owner;
        private readonly IOpenVrSceneOverlay _overlay;
        private volatile bool _retired;

        public VideoLease(VrMenuOverlayOwner owner, IOpenVrSceneOverlay overlay)
        {
            _owner = owner;
            _overlay = overlay;
            _overlay.PhoneInputReceived += ForwardInput;
        }

        private T Read<T>(Func<IOpenVrSceneOverlay, T> action)
        {
            lock (_owner._gate)
            {
                ObjectDisposedException.ThrowIf(_retired, this);
                return action(_overlay);
            }
        }

        private void Invoke(Action<IOpenVrSceneOverlay> action) => Read(overlay => { action(overlay); return true; });
        public bool IsVisible => Read(overlay => overlay.IsVisible);
        public long GraphicsAdapterLuid => Read(overlay => overlay.GraphicsAdapterLuid);
        public OpenVrPhoneInteractionSnapshot Interaction => Read(overlay => overlay.Interaction);
        public OpenVrBindingHealthSnapshot BindingHealth => Read(overlay => overlay.BindingHealth);
        public PhoneInputPathDiagnostic? CaptureInputEvidence() => Read(overlay => overlay.CaptureInputEvidence());
        public event OpenVrPhoneInputSink? PhoneInputReceived;
        public ValueTask SwitchControllerHandAsync(OpenVrControllerHand hand, CancellationToken cancellationToken) =>
            Read(overlay => overlay.SwitchControllerHandAsync(hand, cancellationToken));
        public void ConfigureLockScreenFeatures(bool keepAwakeWhileGrabbed, bool unlockKeypadEnabled) =>
            Invoke(overlay => overlay.ConfigureLockScreenFeatures(keepAwakeWhileGrabbed, unlockKeypadEnabled));
        public void Submit(GpuVideoFrame frame) => Invoke(overlay => overlay.Submit(frame));
        public void SetPhoneLocked(bool? locked) => Invoke(overlay => overlay.SetPhoneLocked(locked));
        // Native implementations are atomic handoffs. Input callbacks can invoke
        // these while ApiGate is held: taking the owner gate would invert locks
        // against video submission (owner -> ApiGate).
        public void SetScreenGuard(bool enabled, bool faulted)
        {
            if (!_retired) { _overlay.SetScreenGuard(enabled, faulted); }
        }
        public void NotifyControlGeometryRevision(long revision)
        {
            if (!_retired) { _overlay.NotifyControlGeometryRevision(revision); }
        }
        public OpenVrBindingResult OpenBindingUi() => Read(overlay => overlay.OpenBindingUi());
        public void Hide() => Invoke(overlay => overlay.Hide());
        private void ForwardInput(OpenVrPhoneInputCommand command)
        {
            if (!_retired) { PhoneInputReceived?.Invoke(command); }
        }

        internal void Retire()
        {
            _retired = true;
            _overlay.PhoneInputReceived -= ForwardInput;
        }

        public void Dispose()
        {
            lock (_owner._gate)
            {
                if (_retired) { return; }
                Retire();
                _owner._lease = null;
                _overlay.DisconnectVideo();
            }
        }
    }
}
