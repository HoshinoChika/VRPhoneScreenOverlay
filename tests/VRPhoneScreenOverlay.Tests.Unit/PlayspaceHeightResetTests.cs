using Valve.VR;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class PlayspaceHeightResetTests
{
    [Fact]
    public void HeightResetKeepsPlanarDestinationAndRemovesOnlyAltitude()
    {
        FakeRuntime runtime = new();
        OpenVrPlayspaceDragController controller = MoveToB(runtime);
        HmdMatrix34_t before = runtime.Working;
        controller.Update(false, true, 1, runtime.Poses(2, 3, 4));
        Assert.Equal(before.m3, runtime.Working.m3);
        Assert.Equal(before.m11, runtime.Working.m11);
        Assert.Equal(runtime.Live.m7, runtime.Working.m7);
        Assert.Equal(0, controller.OffsetX);
        Assert.Equal(0, controller.OffsetY);
        Assert.Equal(0, controller.OffsetZ);
    }

    [Fact]
    public void HeightResetUsesCurrentFloorInsteadOfStartupSnapshot()
    {
        FakeRuntime runtime = new();
        OpenVrPlayspaceDragController controller = MoveToB(runtime);
        runtime.Live = Identity(10, 0.8f, 20);
        controller.Update(false, true, 1, runtime.Poses(2, 3, 4));
        Assert.Equal(0.8f, runtime.Working.m7, 5);
        Assert.Equal(2, runtime.Working.m3, 5);
        Assert.Equal(4, runtime.Working.m11, 5);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(40)]
    public void HeldDragCannotReusePreResetPose(int multiplier)
    {
        FakeRuntime runtime = new();
        OpenVrPlayspaceDragController controller = MoveToB(runtime);
        controller.Update(true, true, multiplier, runtime.Poses(2, 3, 4));
        HmdMatrix34_t afterReset = runtime.Working;
        int writes = runtime.Writes;
        controller.Update(true, false, multiplier, runtime.Poses(2, 3, 4));
        Assert.Equal(afterReset, runtime.Working);
        Assert.Equal(writes, runtime.Writes);
        Assert.Equal(0, controller.OffsetY);
        controller.Update(false, false, multiplier, runtime.Poses(2, 3, 4));
        controller.Update(true, false, multiplier, runtime.Poses(2, 3, 4));
        Assert.Equal(afterReset, runtime.Working);
    }

    [Fact]
    public void StationaryHandDoesNotRewriteTheSamePreview()
    {
        FakeRuntime runtime = new();
        OpenVrPlayspaceDragController controller = MoveToB(runtime);
        int writes = runtime.Writes;
        for (int index = 0; index < 100; index++)
        {
            controller.Update(true, false, 1, runtime.Poses(2, 3, 4));
        }
        Assert.Equal(writes, runtime.Writes);
    }

    [Fact]
    public void RestoreDoesNotWriteAnOutdatedOrigin()
    {
        FakeRuntime runtime = new();
        OpenVrPlayspaceDragController controller = MoveToB(runtime);
        runtime.Live = Identity(10, 0.8f, 20);
        int writes = runtime.Writes;
        controller.Restore();
        Assert.Equal(writes, runtime.Writes);
        Assert.False(runtime.Preview);
        Assert.Equal(runtime.Live, runtime.Working);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void PlanarResetPreservesDestinationUnderCalibratedYaw(int degrees)
    {
        float angle = degrees * MathF.PI / 180;
        HmdMatrix34_t live = Identity(10, 2, 30);
        live.m0 = MathF.Cos(angle);
        live.m2 = MathF.Sin(angle);
        live.m8 = -MathF.Sin(angle);
        live.m10 = MathF.Cos(angle);
        HmdMatrix34_t current = OpenVrPlayspaceDragController.ApplyStandingOffset(live, 4, -3, 7);
        Assert.True(OpenVrPlayspaceDragController.TryCreatePlanarResetBasis(current, live, out HmdMatrix34_t target));
        HmdMatrix34_t expected = OpenVrPlayspaceDragController.ApplyStandingOffset(live, 4, 0, 7);
        Assert.Equal(expected.m3, target.m3, 4);
        Assert.Equal(expected.m7, target.m7, 4);
        Assert.Equal(expected.m11, target.m11, 4);
    }

    [Fact]
    public void CalibratedUpAxisDefinesAltitudeInsteadOfRawTrackingY()
    {
        HmdMatrix34_t live = Identity(10, 20, 30);
        live.m5 = 0;
        live.m6 = -1;
        live.m9 = 1;
        live.m10 = 0;
        HmdMatrix34_t current = OpenVrPlayspaceDragController.ApplyStandingOffset(live, 4, -3, 7);
        Assert.True(OpenVrPlayspaceDragController.TryCreatePlanarResetBasis(current, live, out HmdMatrix34_t target));
        Assert.Equal(14, target.m3);
        Assert.Equal(13, target.m7);
        Assert.Equal(30, target.m11);
    }

    [Fact]
    public void RepeatedResetsDoNotAccumulateHeightOrReturnToA()
    {
        FakeRuntime runtime = new();
        OpenVrPlayspaceDragController controller = MoveToB(runtime);
        for (int index = 0; index < 50; index++)
        {
            controller.Update(false, true, 1, runtime.Poses(2, 3, 4));
            controller.Update(false, false, 1, runtime.Poses(2, 3, 4));
            Assert.Equal(Identity(2, 0, 4), runtime.Working);
        }
        controller.Update(true, false, 1, runtime.Poses(2, 3, 4));
        controller.Update(true, false, 1, runtime.Poses(3, 4, 6));
        Assert.Equal(Identity(3, 1, 6), runtime.Working);
        controller.Update(false, true, 1, runtime.Poses(3, 4, 6));
        Assert.Equal(Identity(3, 0, 6), runtime.Working);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrInvalidLiveFloorCannotWriteGuessedCoordinates(bool invalid)
    {
        FakeRuntime runtime = new();
        OpenVrPlayspaceDragController controller = MoveToB(runtime);
        int writes = runtime.Writes;
        runtime.ReadSucceeds = invalid;
        if (invalid) { runtime.Live = default; }
        OpenVrPlayspaceDragUpdate result = controller.Update(false, true, 1, runtime.Poses(2, 3, 4));
        Assert.True(result.HeightResetRequested);
        Assert.False(result.HeightResetSucceeded);
        Assert.False(controller.BasisReady);
        Assert.Equal(writes, runtime.Writes);
        Assert.False(runtime.Preview);
        runtime.ReadSucceeds = true;
        runtime.Live = Identity(10, 1, 20);
        controller.Update(false, false, 1, runtime.Poses(2, 3, 4));
        controller.Update(true, false, 1, runtime.Poses(2, 3, 4));
        Assert.True(controller.BasisReady);
        Assert.Equal(writes, runtime.Writes);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(1000)]
    public void InvalidMotionPausesWithoutResettingTheUserPosition(float rawX)
    {
        FakeRuntime runtime = new();
        OpenVrPlayspaceDragController controller = MoveToB(runtime);
        HmdMatrix34_t before = runtime.Working;
        int writes = runtime.Writes;
        controller.Update(true, false, 1, runtime.Poses(rawX, 3, 4));
        controller.Update(true, false, 1, runtime.Poses(2, 3, 4));
        Assert.Equal(before, runtime.Working);
        Assert.Equal(writes, runtime.Writes);
        Assert.Equal(2, controller.OffsetY);
    }

    [Fact]
    public void ResetWithoutAnOwnedPreviewDoesNotReplaceTheRuntimeOrigin()
    {
        FakeRuntime runtime = new() { Live = Identity(10, 2, 30) };
        OpenVrPlayspaceDragController controller = new(runtime, OpenVrControllerHand.Left);
        OpenVrPlayspaceDragUpdate result = controller.Update(false, true, 1, runtime.Poses(0, 1, 0));
        Assert.True(result.HeightResetSucceeded);
        Assert.Equal(runtime.Live, runtime.Working);
        Assert.Equal(0, runtime.Writes);
        Assert.False(runtime.Preview);
    }

    [Theory]
    [InlineData(0.6f)]
    [InlineData(1.7f)]
    [InlineData(2.0f)]
    public void HeadRemainsAtBAfterRemovingAnUpwardDragWithoutInventingHeadHeight(float physicalHeight)
    {
        FakeRuntime runtime = new();
        OpenVrPlayspaceDragController controller = new(runtime, OpenVrControllerHand.Left);
        controller.Update(true, false, 1, runtime.Poses(0, 1, 0));
        controller.Update(true, false, 1, runtime.Poses(-2, -2, 4));
        HmdMatrix34_t headBefore = runtime.Poses(0.25f, physicalHeight, -0.8f)[1].mDeviceToAbsoluteTracking;
        Assert.Equal(physicalHeight + 3, headBefore.m7, 5);
        controller.Update(false, true, 1, runtime.Poses(-2, -2, 4));
        HmdMatrix34_t headAfter = runtime.Poses(0.25f, physicalHeight, -0.8f)[1].mDeviceToAbsoluteTracking;
        Assert.Equal(headBefore.m3, headAfter.m3, 5);
        Assert.Equal(headBefore.m11, headAfter.m11, 5);
        Assert.Equal(physicalHeight, headAfter.m7, 5);
    }

    [Fact]
    public void IdleOrDisabledControllerDoesNotTouchTheCalibrationWorkingCopy()
    {
        FakeRuntime runtime = new();
        OpenVrPlayspaceDragController controller = new(runtime, OpenVrControllerHand.Left);
        for (int index = 0; index < 100; index++)
        {
            controller.Update(false, false, 1, runtime.Poses(0, 1, 0));
        }
        controller.Restore();
        Assert.Equal(0, runtime.Reads);
        Assert.Equal(0, runtime.Reverts);
        Assert.Equal(0, runtime.Writes);
    }

    private static OpenVrPlayspaceDragController MoveToB(FakeRuntime runtime)
    {
        OpenVrPlayspaceDragController controller = new(runtime, OpenVrControllerHand.Left);
        controller.Update(true, false, 1, runtime.Poses(0, 1, 0));
        controller.Update(true, false, 1, runtime.Poses(2, 3, 4));
        return controller;
    }

    internal static HmdMatrix34_t Identity(float x = 0, float y = 0, float z = 0) => new()
    {
        m0 = 1,
        m5 = 1,
        m10 = 1,
        m3 = x,
        m7 = y,
        m11 = z,
    };

    private sealed class FakeRuntime : IOpenVrPlayspaceRuntime
    {
        public HmdMatrix34_t Live { get; set; } = Identity();
        public HmdMatrix34_t Working { get; private set; } = Identity();
        public bool Preview { get; private set; }
        public int Writes { get; private set; }
        public bool ReadSucceeds { get; set; } = true;
        public int Reads { get; private set; }
        public int Reverts { get; private set; }
        public uint ControllerIndex(ETrackedControllerRole role) => 1;
        public bool ReadWorkingBasis(ref HmdMatrix34_t basis) { Reads++; basis = Working; return ReadSucceeds; }
        public void RevertWorkingCopy() { Working = Live; Reverts++; }
        public void SetWorkingBasis(ref HmdMatrix34_t basis) { Working = basis; Writes++; }
        public void ShowPreview() => Preview = true;
        public void HidePreview() => Preview = false;

        public TrackedDevicePose_t[] Poses(float rawX, float rawY, float rawZ)
        {
            HmdMatrix34_t origin = Preview ? Working : Live;
            float x = rawX - origin.m3;
            float y = rawY - origin.m7;
            float z = rawZ - origin.m11;
            HmdMatrix34_t pose = Identity(
                origin.m0 * x + origin.m4 * y + origin.m8 * z,
                origin.m1 * x + origin.m5 * y + origin.m9 * z,
                origin.m2 * x + origin.m6 * y + origin.m10 * z);
            return [default, new TrackedDevicePose_t
            {
                bPoseIsValid = true, bDeviceIsConnected = true,
                eTrackingResult = ETrackingResult.Running_OK, mDeviceToAbsoluteTracking = pose,
            }];
        }
    }
}
