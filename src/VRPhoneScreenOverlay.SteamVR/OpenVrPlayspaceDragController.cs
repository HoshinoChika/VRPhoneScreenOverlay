using System.Numerics;
using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

internal readonly record struct OpenVrPlayspaceDragUpdate(
    bool OffsetApplied,
    float SourceDistance,
    float AppliedDistance,
    float OffsetX,
    float OffsetY,
    float OffsetZ,
    float SourceDeltaX,
    float SourceDeltaY,
    float SourceDeltaZ,
    float AppliedDeltaX,
    float AppliedDeltaY,
    float AppliedDeltaZ,
    bool HeightResetRequested = false,
    bool HeightResetSucceeded = false);

internal sealed class OpenVrPlayspaceDragController
{
    private readonly IOpenVrPlayspaceRuntime _runtime;
    private readonly ETrackedControllerRole _role;

    public OpenVrPlayspaceDragController(CVRSystem system, CVRChaperoneSetup chaperoneSetup, OpenVrControllerHand hand)
        : this(new OpenVrPlayspaceRuntime(system, chaperoneSetup), hand)
    {
    }

    internal OpenVrPlayspaceDragController(IOpenVrPlayspaceRuntime runtime, OpenVrControllerHand hand)
    {
        _runtime = runtime;
        _role = OpenVrControllerHandRouting.TrackedRole(hand);
    }
    private HmdMatrix34_t _basis;
    private bool _basisReady;
    private bool _dragTracking;
    private bool _previousReset;
    private bool _previewVisible;
    private bool _waitForRelease;
    private readonly OpenVrPlayspaceMotion _motion = new();
    private bool _flingWasEnabled;
    private float _offsetX;
    private float _offsetY;
    private float _offsetZ;
    private float _lastX;
    private float _lastY;
    private float _lastZ;

    public bool BasisReady => _basisReady;

    public float OffsetX => _offsetX;

    public float OffsetY => _offsetY;

    public float OffsetZ => _offsetZ;

    public OpenVrPlayspaceDragUpdate Update(
        bool dragPressed,
        bool resetPressed,
        float multiplier,
        IReadOnlyList<TrackedDevicePose_t> poses,
        OpenVrPlayspaceMotionOptions? options = null,
        bool flingEnabled = false,
        float elapsedSeconds = 0.004f,
        bool motionAllowed = true)
    {
        options ??= OpenVrPlayspaceMotionOptions.Default;
        if (_flingWasEnabled != flingEnabled) { _motion.Stop(); }
        _flingWasEnabled = flingEnabled;
        if (!motionAllowed) { Suspend(); return default; }
        bool resetRequested = resetPressed && !_previousReset;
        _previousReset = resetPressed;
        if (resetRequested)
        {
            _motion.Stop();
            bool succeeded = options.ResetAllOffsets ? ResetAllOffsets() : ResetHeightAtCurrentPosition();
            return default(OpenVrPlayspaceDragUpdate) with
            {
                HeightResetRequested = true,
                HeightResetSucceeded = succeeded,
            };
        }

        if (!float.IsFinite(elapsedSeconds) || elapsedSeconds is <= 0 or > 0.1f)
        {
            Suspend();
            return default;
        }

        // Poses supplied to this update were sampled before changing the origin.
        // Do not seed a new drag from them or from a held gesture across a reset.
        if (_waitForRelease)
        {
            if (!dragPressed && !resetPressed) { _waitForRelease = false; }
            _dragTracking = false;
            return default;
        }

        if (!dragPressed)
        {
            bool justReleased = _dragTracking;
            _dragTracking = false;
            if (justReleased) { return default; }
            return flingEnabled && _basisReady ? AdvanceMotion(elapsedSeconds, options) : default;
        }

        if (!_basisReady)
        {
            _runtime.RevertWorkingCopy();
            _basisReady = _runtime.ReadWorkingBasis(ref _basis) && IsValidBasis(_basis);
        }
        if (!_basisReady)
        {
            _dragTracking = false;
            return default;
        }

        uint device = _runtime.ControllerIndex(_role);
        if (device == OpenVR.k_unTrackedDeviceIndexInvalid || device >= poses.Count)
        {
            Suspend();
            return default;
        }

        TrackedDevicePose_t pose = poses[(int)device];
        if (!pose.bPoseIsValid || !pose.bDeviceIsConnected ||
            pose.eTrackingResult != ETrackingResult.Running_OK)
        {
            Suspend();
            return default;
        }

        float currentX = pose.mDeviceToAbsoluteTracking.m3 + _offsetX;
        float currentY = pose.mDeviceToAbsoluteTracking.m7 + _offsetY;
        float currentZ = pose.mDeviceToAbsoluteTracking.m11 + _offsetZ;
        if (!float.IsFinite(currentX) || !float.IsFinite(currentY) || !float.IsFinite(currentZ))
        {
            Suspend();
            return default;
        }
        OpenVrPlayspaceDragUpdate update = default;
        if (_dragTracking)
        {
            (float sourceX, float sourceY, float sourceZ) = CalculateDelta(
                currentX,
                currentY,
                currentZ,
                _lastX,
                _lastY,
                _lastZ,
                1f);
            (float deltaX, float deltaY, float deltaZ) = CalculateDelta(
                currentX,
                currentY,
                currentZ,
                _lastX,
                _lastY,
                _lastZ,
                multiplier);
            if (!IsSafeDelta(deltaX, deltaY, deltaZ))
            {
                Suspend();
                return default;
            }
            if (flingEnabled) { _motion.Capture(new Vector3(deltaX, deltaY, deltaZ), elapsedSeconds, options.FlingStrength); }
            if (deltaX != 0 || deltaY != 0 || deltaZ != 0)
            {
                _offsetX += deltaX;
                _offsetY += deltaY;
                _offsetZ += deltaZ;
                ApplyOffsets();
                update = new OpenVrPlayspaceDragUpdate(
                    true,
                    VectorLength(sourceX, sourceY, sourceZ),
                    VectorLength(deltaX, deltaY, deltaZ),
                    _offsetX,
                    _offsetY,
                    _offsetZ,
                    sourceX,
                    sourceY,
                    sourceZ,
                    deltaX,
                    deltaY,
                    deltaZ);
            }
        }

        if (!_dragTracking) { _motion.Stop(); }
        _lastX = currentX;
        _lastY = currentY;
        _lastZ = currentZ;
        _dragTracking = true;
        return update;
    }

    public void Suspend()
    {
        _motion.Stop();
        _dragTracking = false;
        _waitForRelease = true;
    }

    public void SuspendForBindingChange()
    {
        Suspend();
        _previousReset = true;
    }

    private OpenVrPlayspaceDragUpdate AdvanceMotion(float seconds, OpenVrPlayspaceMotionOptions options)
    {
        Vector3 before = new(_offsetX, _offsetY, _offsetZ);
        Vector3 after = _motion.Step(before, seconds, options);
        if (after == before) { return default; }
        Vector3 delta = after - before;
        if (!IsSafeDelta(delta.X, delta.Y, delta.Z)) { Suspend(); return default; }
        _offsetX = after.X;
        _offsetY = after.Y;
        _offsetZ = after.Z;
        ApplyOffsets();
        return new(true, 0, delta.Length(), after.X, after.Y, after.Z,
            0, 0, 0, delta.X, delta.Y, delta.Z);
    }

    private bool ResetAllOffsets()
    {
        Restore();
        _runtime.RevertWorkingCopy();
        _basisReady = _runtime.ReadWorkingBasis(ref _basis) && IsValidBasis(_basis);
        return _basisReady;
    }

    public void Restore()
    {
        _motion.Stop();
        // Release our temporary preview; never write a cached room origin back.
        if (_previewVisible)
        {
            _runtime.HidePreview();
            _runtime.RevertWorkingCopy();
        }
        _previewVisible = false;
        _basisReady = false;
        _dragTracking = false;
        _waitForRelease = true;
        _offsetX = 0;
        _offsetY = 0;
        _offsetZ = 0;
    }

    internal static (float X, float Y, float Z) CalculateDelta(
        float currentX,
        float currentY,
        float currentZ,
        float previousX,
        float previousY,
        float previousZ,
        float multiplier) =>
        (
            (currentX - previousX) * multiplier,
            (currentY - previousY) * multiplier,
            (currentZ - previousZ) * multiplier
        );

    internal static bool IsSafeDelta(float x, float y, float z) =>
        float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z) &&
        MathF.Abs(x) < 100f && MathF.Abs(y) < 100f && MathF.Abs(z) < 100f;

    private static float VectorLength(float x, float y, float z) =>
        MathF.Sqrt((x * x) + (y * y) + (z * z));

    internal static HmdMatrix34_t ApplyStandingOffset(
        HmdMatrix34_t basis,
        float offsetX,
        float offsetY,
        float offsetZ)
    {
        basis.m3 += basis.m0 * offsetX + basis.m1 * offsetY + basis.m2 * offsetZ;
        basis.m7 += basis.m4 * offsetX + basis.m5 * offsetY + basis.m6 * offsetZ;
        basis.m11 += basis.m8 * offsetX + basis.m9 * offsetY + basis.m10 * offsetZ;
        return basis;
    }

    private void ApplyOffsets()
    {
        HmdMatrix34_t value = ApplyStandingOffset(_basis, _offsetX, _offsetY, _offsetZ);
        _runtime.SetWorkingBasis(ref value);
        _runtime.ShowPreview();
        _previewVisible = true;
    }

    private bool ResetHeightAtCurrentPosition()
    {
        bool hadPreview = _previewVisible;
        HmdMatrix34_t current = ApplyStandingOffset(_basis, _offsetX, _offsetY, _offsetZ);
        _dragTracking = false;
        _waitForRelease = true;

        // Revert reads the current calibration, not the startup snapshot. Avoid
        // hiding a valid planar preview between the read and its replacement.
        _runtime.RevertWorkingCopy();
        HmdMatrix34_t live = default;
        if (!_runtime.ReadWorkingBasis(ref live) || !IsValidBasis(live) ||
            !TryCreatePlanarResetBasis(hadPreview ? current : live, live, out HmdMatrix34_t target))
        {
            Restore();
            return false;
        }

        _basis = target;
        _basisReady = true;
        _offsetX = 0;
        _offsetY = 0;
        _offsetZ = 0;
        if (hadPreview) { ApplyOffsets(); }
        return true;
    }

    internal static bool TryCreatePlanarResetBasis(
        HmdMatrix34_t current, HmdMatrix34_t live, out HmdMatrix34_t target)
    {
        target = default;
        if (!IsValidBasis(current) || !IsValidBasis(live)) { return false; }
        float x = current.m3 - live.m3;
        float y = current.m7 - live.m7;
        float z = current.m11 - live.m11;
        // Express the current preview origin in the live calibrated frame. Keep
        // its floor-plane coordinates and discard its elevation. This also works
        // when raw tracking axes differ from the calibrated standing axes.
        float planarX = live.m0 * x + live.m4 * y + live.m8 * z;
        float planarZ = live.m2 * x + live.m6 * y + live.m10 * z;
        target = ApplyStandingOffset(live, planarX, 0, planarZ);
        return IsValidBasis(target);
    }

    internal static bool IsValidBasis(HmdMatrix34_t basis)
    {
        ReadOnlySpan<float> values = [basis.m0, basis.m1, basis.m2, basis.m3,
            basis.m4, basis.m5, basis.m6, basis.m7, basis.m8, basis.m9, basis.m10, basis.m11];
        foreach (float value in values)
        {
            if (!float.IsFinite(value)) { return false; }
        }
        Vector3 right = new(basis.m0, basis.m4, basis.m8);
        Vector3 up = new(basis.m1, basis.m5, basis.m9);
        Vector3 forward = new(basis.m2, basis.m6, basis.m10);
        const float tolerance = 0.01f;
        return MathF.Abs(right.LengthSquared() - 1) < tolerance &&
            MathF.Abs(up.LengthSquared() - 1) < tolerance &&
            MathF.Abs(forward.LengthSquared() - 1) < tolerance &&
            MathF.Abs(Vector3.Dot(right, up)) < tolerance &&
            MathF.Abs(Vector3.Dot(up, forward)) < tolerance &&
            MathF.Abs(Vector3.Dot(right, forward)) < tolerance &&
            Vector3.Dot(Vector3.Cross(right, up), forward) > 1 - tolerance;
    }
}
