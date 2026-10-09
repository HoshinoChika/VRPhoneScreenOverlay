using System.Numerics;
using Valve.VR;
using VRPhoneScreenOverlay.Settings;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class PlayspaceFlingTests
{
    [Theory]
    [InlineData(72)]
    [InlineData(90)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(250)]
    public void BallisticTrajectoryMatchesUpstreamEulerOrderAndPhysicalLimit(int rate)
    {
        OpenVrPlayspaceMotion motion = new();
        float dt = 1f / rate;
        motion.Capture(new Vector3(2, -3, 1) * dt, dt, 1);
        Vector3 position = new(0, -10, 0);
        for (int i = 0; i < rate; i++) { position = motion.Step(position, dt, new()); }
        // Analytic discrete Euler trajectory, independent of production stepping.
        float expectedY = -13 + 9.8f * (1 - dt) / 2;
        Assert.InRange(position.X, 1.9999f, 2.0001f);
        Assert.InRange(position.Z, 0.9999f, 1.0001f);
        Assert.InRange(position.Y, expectedY - 0.0001f, expectedY + 0.0001f);
        // Continuous physical answer is -8.1 m; OVRAS's Euler error is g*dt/2.
        Assert.InRange(MathF.Abs(position.Y + 8.1f), 0, 0.07f);
    }

    [Theory]
    [InlineData(72)]
    [InlineData(144)]
    public void FrictionMatchesUpstreamTimeScaledDamping(int rate)
    {
        OpenVrPlayspaceMotion motion = new();
        float dt = 1f / rate;
        motion.Capture(new Vector3(10, 0, 0) * dt, dt, 1);
        Vector3 position = new(0, -10, 0);
        for (int i = 0; i < rate; i++) { position = motion.Step(position, dt, new(Gravity: 0, Friction: 10)); }
        double ratio = 1 - (double)dt;
        double expected = 10 * dt * ratio * (1 - Math.Pow(ratio, rate)) / (1 - ratio);
        Assert.InRange(Math.Abs(position.X - expected), 0, 0.0001);
        Assert.InRange(motion.Velocity.X, 3.6f, 3.7f);
    }

    [Fact]
    public void TouchdownMovesOnlyToIntersectionThenStopsEveryAxis()
    {
        OpenVrPlayspaceMotion motion = new();
        motion.Capture(new Vector3(1, 1, 0.5f), 0.1f, 1);
        Vector3 landed = motion.Step(new Vector3(0, -0.5f, 0), 0.1f, new());
        Assert.Equal(new Vector3(0.5f, 0, 0.25f), landed);
        Assert.Equal(Vector3.Zero, motion.Velocity);
        Assert.Equal(landed, motion.Step(landed, 0.1f, new()));
    }

    [Fact]
    public void FlingMultiplierAndTerminalVelocityAreIndependentOfDragDistance()
    {
        OpenVrPlayspaceMotion motion = new();
        motion.Capture(new Vector3(0.1f, -0.2f, 0.05f), 0.01f, 2);
        Assert.Equal(new Vector3(20, -40, 10), motion.Velocity);
        motion.Capture(new Vector3(10, -10, 10), 0.01f, 20);
        Vector3 after = motion.Step(new Vector3(0, -100, 0), 0.1f, new(Gravity: 0));
        Assert.Equal(new Vector3(5, -105, 5), after);
        Assert.Equal(new Vector3(50, -50, 50), motion.Velocity);
    }

    [Fact]
    public void FrictionPrecedesTerminalClampAsInOvras()
    {
        OpenVrPlayspaceMotion motion = new();
        motion.Capture(new Vector3(10, 0, 0), 0.1f, 1);
        Vector3 after = motion.Step(new Vector3(0, -1, 0), 0.1f, new(Gravity: 0, Friction: 50));
        // 100 m/s * 0.5, then clamp to 50; clamping first would incorrectly move 2.5 m.
        Assert.Equal(5, after.X);
    }

    [Fact]
    public void MotionSteppingAllocatesNoPerFrameObjects()
    {
        OpenVrPlayspaceMotion motion = new();
        OpenVrPlayspaceMotionOptions options = new(Gravity: 0);
        Vector3 position = new(0, -1, 0);
        motion.Capture(new Vector3(0.001f, 0, 0), 0.01f, 1);
        for (int i = 0; i < 100; i++) { position = motion.Step(position, 0.01f, options); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) { position = motion.Step(position, 0.01f, options); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.True(position.X > 10);
    }

    [Fact]
    public void DuplicateCompositorFramesDoNotEraseVelocityOrAdvanceTime()
    {
        OpenVrPlayspaceFrameClock clock = new();
        Assert.False(clock.TryAdvance(10, 1, out _));
        Assert.False(clock.TryAdvance(10, 1.004, out _));
        Assert.False(clock.TryAdvance(10, 1.008, out _));
        Assert.True(clock.TryAdvance(11, 1.012, out float elapsed));
        Assert.Equal(0.012f, elapsed, 6);
        Assert.True(clock.TryAdvance(12, 1.024, out elapsed));
        Assert.Equal(0.012f, elapsed, 6);
    }

    [Fact]
    public void FallbackClockIsBoundedAndRetainsLongGapForControllerRejection()
    {
        OpenVrPlayspaceFrameClock clock = new();
        Assert.False(clock.TryAdvance(null, 0, out _));
        Assert.False(clock.TryAdvance(null, 0.004, out _));
        Assert.True(clock.TryAdvance(null, 0.012, out _));
        Assert.True(clock.TryAdvance(null, 3, out float elapsed));
        Assert.True(elapsed > 0.1f);
    }

    [Fact]
    public void ExistingDragKeyLaunchesAndRegrabDoesNotJump()
    {
        Rig rig = new();
        rig.Throw();
        float releasedX = rig.Controller.OffsetX;
        rig.Tick(false);
        Assert.True(rig.Controller.OffsetX > releasedX);
        float airborneX = rig.Controller.OffsetX;
        rig.Tick(true);
        Assert.Equal(airborneX, rig.Controller.OffsetX);
        rig.Tick(true);
        Assert.Equal(airborneX, rig.Controller.OffsetX);
        rig.Tick(false);
        rig.Tick(false);
        Assert.Equal(airborneX, rig.Controller.OffsetX);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResetClearsThrowAndHeldGestureForBothModes(bool resetAll)
    {
        Rig rig = new();
        rig.Throw();
        rig.Tick(false);
        float x = rig.Runtime.Working.m3;
        rig.Controller.Update(true, true, 1, rig.Poses(), new(ResetAllOffsets: resetAll), true, 0.01f);
        Assert.Equal(resetAll ? 0 : x, rig.Runtime.Working.m3);
        Assert.Equal(0, rig.Runtime.Working.m7);
        rig.Raw += new Vector3(5, 5, 5);
        rig.Tick(true);
        rig.Tick(true);
        rig.Tick(false);
        for (int i = 0; i < 30; i++) { rig.Tick(false); }
        Assert.Equal(resetAll ? 0 : x, rig.Runtime.Working.m3);
        Assert.Equal(0, rig.Runtime.Working.m7);
    }

    [Fact]
    public void DisabledFlingPreservesOldDragBehavior()
    {
        Rig rig = new();
        rig.Tick(true, fling: false);
        rig.Raw = new Vector3(1, -1, 2);
        rig.Tick(true, fling: false);
        Vector3 held = rig.Position;
        for (int i = 0; i < 100; i++) { rig.Tick(false, fling: false); }
        Assert.Equal(held, rig.Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterruptionOrLongStallCancelsThrowWithoutTeleport(bool stall)
    {
        Rig rig = new();
        rig.Throw();
        Vector3 before = rig.Position;
        rig.Controller.Update(false, false, 1, rig.Poses(), flingEnabled: true,
            elapsedSeconds: stall ? 2 : 0.01f, motionAllowed: stall);
        Assert.Equal(before, rig.Position);
        rig.Raw += new Vector3(20, 20, 20);
        rig.Tick(true);
        Assert.Equal(before, rig.Position);
        rig.Tick(false);
        rig.Tick(false, fling: false);
        Assert.Equal(before, rig.Position);
    }

    [Fact]
    public void LostControllerWhileHeldCannotLaunchOnRelease()
    {
        Rig rig = new();
        rig.Throw(release: false);
        Vector3 before = rig.Position;
        rig.Controller.Update(true, false, 1, [default, default], flingEnabled: true, elapsedSeconds: 0.01f);
        rig.Tick(false);
        rig.Tick(false, fling: false);
        Assert.Equal(before, rig.Position);
    }

    [Fact]
    public void TurningOffFlingStopsImmediatelyAndDoesNotRestoreOldMomentum()
    {
        Rig rig = new();
        rig.Throw();
        rig.Tick(false, fling: false);
        Vector3 before = rig.Position;
        rig.Tick(false, fling: false);
        Assert.Equal(before, rig.Position);
        rig.Controller.Update(false, false, 1, rig.Poses(), new(Gravity: 0), true, 0.01f);
        Assert.Equal(before, rig.Position);
    }

    [Fact]
    public void InvalidSettingsNormalizeAndRuntimeRejectsNonFiniteValues()
    {
        AppSettings normalized = AppSettingsPolicy.Normalize(AppSettings.Default with
        { PlayspaceFlingStrength = float.NaN, PlayspaceGravity = -1, PlayspaceFriction = 1000 });
        Assert.Equal(1, normalized.PlayspaceFlingStrength);
        Assert.Equal(9.8f, normalized.PlayspaceGravity);
        Assert.Equal(0, normalized.PlayspaceFriction);
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpenVrPlayspaceMotionOptions(Gravity: float.PositiveInfinity).Validate());
    }

    private sealed class Rig
    {
        public Runtime Runtime { get; } = new();
        public OpenVrPlayspaceDragController Controller { get; }
        public Vector3 Raw { get; set; }
        public Vector3 Position => new(Controller.OffsetX, Controller.OffsetY, Controller.OffsetZ);
        public Rig() => Controller = new(Runtime, OpenVrControllerHand.Left);
        public TrackedDevicePose_t[] Poses() => [default, new()
        {
            bPoseIsValid = true, bDeviceIsConnected = true, eTrackingResult = ETrackingResult.Running_OK,
            mDeviceToAbsoluteTracking = PlayspaceHeightResetTests.Identity(
                Raw.X - Runtime.Working.m3, Raw.Y - Runtime.Working.m7, Raw.Z - Runtime.Working.m11),
        }];
        public void Tick(bool drag, bool fling = true) => Controller.Update(drag, false, 1, Poses(), flingEnabled: fling, elapsedSeconds: 0.01f);
        public void Throw(bool release = true)
        {
            Tick(true);
            Raw = new Vector3(0.02f, -0.03f, 0.01f);
            Tick(true);
            if (release) { Tick(false); }
        }
    }

    private sealed class Runtime : IOpenVrPlayspaceRuntime
    {
        public HmdMatrix34_t Working { get; private set; } = PlayspaceHeightResetTests.Identity();
        public uint ControllerIndex(ETrackedControllerRole role) => 1;
        public bool ReadWorkingBasis(ref HmdMatrix34_t basis) { basis = Working; return true; }
        public void RevertWorkingCopy() => Working = PlayspaceHeightResetTests.Identity();
        public void SetWorkingBasis(ref HmdMatrix34_t basis) => Working = basis;
        public void ShowPreview() { }
        public void HidePreview() { }
    }
}
