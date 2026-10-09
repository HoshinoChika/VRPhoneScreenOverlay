using System.Runtime.InteropServices;
using Valve.VR;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OpenVrDashboardReleaseTests
{
    [Fact]
    public void ClosingDashboardBeforePhonePollDoesNotMoveUntilANewPhysicalRelease()
    {
        using PlayspaceFixture fixture = new();
        // Dashboard may suppress action values, so false here is not a release.
        fixture.Publish(new(true, false, false, true));
        fixture.Poll(dashboard: true, x: 0);
        fixture.Poll(dashboard: false, x: 0);
        fixture.Publish(new(false, true, false, true));
        fixture.Poll(dashboard: false, x: 0);
        Assert.False(fixture.Poll(dashboard: false, x: 0.01f).OffsetApplied);
        Assert.Equal(0, fixture.Controller.OffsetX);

        fixture.Publish(new(false, false, false, true));
        fixture.Poll(dashboard: false, x: 0.01f);
        fixture.Publish(new(false, true, false, true));
        fixture.Poll(dashboard: false, x: 0.01f);
        Assert.True(fixture.Poll(dashboard: false, x: 0.02f).OffsetApplied);
        Assert.Equal(0.01f, fixture.Controller.OffsetX, 5);
    }

    [Fact]
    public void NeutralSampleFromBeforeDashboardOpenedCannotUnlockAfterItCloses()
    {
        using PlayspaceFixture fixture = new();
        fixture.Publish(new(false, false, false, true));
        fixture.Poll(dashboard: false, x: 0);
        // The faster worker observes an entire Dashboard transition before the
        // phone producer publishes again. Even a non-Dashboard sample is stale.
        fixture.Poll(dashboard: true, x: 0);
        fixture.Poll(dashboard: false, x: 0);
        fixture.Publish(new(false, true, true, true));
        fixture.Poll(dashboard: false, x: 0);
        Assert.False(fixture.Poll(dashboard: false, x: 0.01f).OffsetApplied);
        Assert.False(fixture.DragAllowed);
        Assert.False(fixture.ResetAllowed);
    }

    [Fact]
    public void FreshDashboardSampleAndInvalidSamplesCannotProveRelease()
    {
        OpenVrDashboardInputGate gate = new();
        gate.Apply(false, new(true, false, false, true), true, out _, out _);
        gate.Apply(false, new(false, false, false, false), true, out _, out _);
        gate.Apply(false, new(false, true, true, true), true, out bool drag, out bool reset);
        Assert.False(drag || reset);
        gate.Apply(false, new(false, false, false, true), false, out _, out _);
        gate.Apply(false, new(false, true, true, true), true, out drag, out reset);
        Assert.False(drag || reset);

        gate.Apply(false, new(false, false, false, true), true, out _, out _);
        gate.Apply(false, new(false, true, true, true), true, out drag, out reset);
        Assert.True(drag && reset);
    }

    [Fact]
    public void BothSemanticButtonsMustReleaseAndStandaloneFreshSamplesCanResume()
    {
        OpenVrDashboardInputGate gate = new();
        gate.Apply(true, new(true, true, true, true), true, out _, out _);
        gate.Apply(false, new(false, false, true, true), true, out _, out bool reset);
        Assert.False(reset);
        gate.Apply(false, new(false, true, false, true), true, out bool drag, out _);
        Assert.False(drag);
        gate.Apply(false, new(false, false, false, true), true, out _, out _);
        gate.Apply(false, new(false, false, true, true), true, out _, out reset);
        Assert.True(reset);
    }

    [Fact]
    public void RegularDragReusesTheLatestSampleAndResetStillRestoresOffsets()
    {
        using PlayspaceFixture fixture = new();
        fixture.Publish(new(false, true, false, true));
        fixture.Poll(dashboard: false, x: 0);
        Assert.True(fixture.Poll(dashboard: false, x: 0.01f).OffsetApplied);
        Assert.Equal(0.01f, fixture.Controller.OffsetX, 5);
        fixture.Publish(new(false, false, true, true));
        fixture.Poll(dashboard: false, x: 0.01f);
        Assert.True(fixture.ResetAllowed);
        Assert.Equal(0, fixture.Controller.OffsetX);
    }

    [Fact]
    public void SharedRawStateSurvivesDisableAndNewPhoneLeaseRequiresANewSample()
    {
        OpenVrActionUpdateCoordinator coordinator = new();
        coordinator.SetPlayspaceEnabled(true);
        long firstRevision;
        using (coordinator.AcquirePhonePoller())
        {
            coordinator.PublishPlayspaceInput(new(false, true, false, true));
            firstRevision = coordinator.Read().InputRevision;
            coordinator.SetPlayspaceEnabled(false);
            Assert.False(coordinator.Read().DragPressed);
            Assert.True(coordinator.Read().InputSample.DragPressed);
        }
        using (coordinator.AcquirePhonePoller())
        {
            Assert.False(coordinator.Read().InputSample.IsValid);
            coordinator.PublishPlayspaceInput(new(false, false, false, true));
            Assert.True(coordinator.Read().InputRevision > firstRevision);
        }
    }

    [Fact]
    public void SwitchingBetweenPhoneAndStandalonePollersDoesNotBypassDashboardRelease()
    {
        OpenVrActionUpdateCoordinator coordinator = new();
        OpenVrDashboardInputGate gate = new();
        coordinator.SetPlayspaceEnabled(true);
        using (coordinator.AcquirePhonePoller())
        {
            coordinator.PublishPlayspaceInput(new(true, false, false, false));
            gate.Apply(true, coordinator.Read().InputSample, true, out _, out _);
        }

        Assert.False(coordinator.Read().PhonePollerActive);
        gate.Apply(false, new(false, true, false, true), true, out bool drag, out _);
        Assert.False(drag);
        gate.Apply(false, new(false, false, false, true), true, out _, out _);
        gate.Apply(false, new(false, true, false, true), true, out drag, out _);
        Assert.True(drag);

        gate.Apply(true, new(true, true, false, true), true, out _, out _);
        using (coordinator.AcquirePhonePoller())
        {
            gate.Apply(false, coordinator.Read().InputSample, true, out drag, out _);
            Assert.False(drag);
            coordinator.PublishPlayspaceInput(new(false, true, false, true));
            gate.Apply(false, coordinator.Read().InputSample, true, out drag, out _);
            Assert.False(drag);
            coordinator.PublishPlayspaceInput(new(false, false, false, true));
            gate.Apply(false, coordinator.Read().InputSample, true, out _, out _);
            coordinator.PublishPlayspaceInput(new(false, true, false, true));
            gate.Apply(false, coordinator.Read().InputSample, true, out drag, out _);
            Assert.True(drag);
        }
    }

    [Theory]
    [InlineData(101ul, true, false)]
    [InlineData(202ul, false, true)]
    public void SamplingUsesProvidedSemanticHandlesAndSelectedSourceIncludingOptionalUnboundActions(
        ulong source, bool dragPressed, bool resetPressed)
    {
        List<(ulong Action, ulong Source)> reads = [];
        IVRInput table = new()
        {
            GetDigitalActionData = (ulong action, ref InputDigitalActionData_t data, uint _, ulong selectedSource) =>
            {
                reads.Add((action, selectedSource));
                // The other semantic drag action can legitimately be unbound.
                data.bActive = action != 11;
                data.bState = action == 22 ? dragPressed : action == 33 && resetPressed;
                return EVRInputError.None;
            },
        };
        nint pointer = Marshal.AllocHGlobal(Marshal.SizeOf<IVRInput>());
        try
        {
            Marshal.StructureToPtr(table, pointer, false);
            OpenVrPlayspaceInputSample sample = OpenVrPlayspaceInputSample.Read(
                new CVRInput(pointer), 11, 22, 33, source, false);
            Assert.True(sample.IsValid);
            Assert.Equal(dragPressed, sample.DragPressed);
            Assert.Equal(resetPressed, sample.ResetPressed);
            Assert.Equal(new[] { (11ul, source), (22ul, source), (33ul, source) }, reads);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
            GC.KeepAlive(table);
        }
    }

    [Theory]
    [InlineData(EVRInputError.None, false)]
    [InlineData(EVRInputError.IPCError, true)]
    public void UnavailableInputCannotBeReportedAsAValidNeutralSample(EVRInputError error, bool active)
    {
        IVRInput table = new()
        {
            GetDigitalActionData = (ulong _, ref InputDigitalActionData_t data, uint _, ulong _) =>
            {
                data.bActive = active;
                return error;
            },
        };
        nint pointer = Marshal.AllocHGlobal(Marshal.SizeOf<IVRInput>());
        try
        {
            Marshal.StructureToPtr(table, pointer, false);
            OpenVrPlayspaceInputSample sample = OpenVrPlayspaceInputSample.Read(
                new CVRInput(pointer), 11, 22, 33, 101, false);
            Assert.False(sample.IsValid);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
            GC.KeepAlive(table);
        }
    }

    private sealed class PlayspaceFixture : IDisposable
    {
        private readonly OpenVrActionUpdateCoordinator _coordinator = new();
        private readonly OpenVrDashboardInputGate _dashboardGate = new();
        private readonly IDisposable _phoneLease;
        private readonly IVRSystem _systemTable;
        private readonly IVRChaperoneSetup _chaperoneTable;
        private readonly nint _systemPointer;
        private readonly nint _chaperonePointer;
        private readonly TrackedDevicePose_t[] _poses = new TrackedDevicePose_t[2];
        private long _lastRevision = -1;

        public PlayspaceFixture()
        {
            _coordinator.SetPlayspaceEnabled(true);
            _phoneLease = _coordinator.AcquirePhonePoller();
            _systemTable = new IVRSystem { GetTrackedDeviceIndexForControllerRole = _ => 1 };
            _chaperoneTable = new IVRChaperoneSetup
            {
                RevertWorkingCopy = () => { },
                GetWorkingStandingZeroPoseToRawTrackingPose = (ref HmdMatrix34_t basis) =>
                {
                    basis = OpenVrTransformMath.Identity();
                    return true;
                },
                SetWorkingStandingZeroPoseToRawTrackingPose = (ref HmdMatrix34_t _) => { },
                ShowWorkingSetPreview = () => { },
                HideWorkingSetPreview = () => { },
            };
            _systemPointer = Marshal.AllocHGlobal(Marshal.SizeOf<IVRSystem>());
            _chaperonePointer = Marshal.AllocHGlobal(Marshal.SizeOf<IVRChaperoneSetup>());
            Marshal.StructureToPtr(_systemTable, _systemPointer, false);
            Marshal.StructureToPtr(_chaperoneTable, _chaperonePointer, false);
            Controller = new OpenVrPlayspaceDragController(
                new CVRSystem(_systemPointer), new CVRChaperoneSetup(_chaperonePointer), OpenVrControllerHand.Left);
            _poses[1] = new TrackedDevicePose_t
            {
                bDeviceIsConnected = true,
                bPoseIsValid = true,
                eTrackingResult = ETrackingResult.Running_OK,
                mDeviceToAbsoluteTracking = OpenVrTransformMath.Identity(),
            };
        }

        public OpenVrPlayspaceDragController Controller { get; }
        public bool DragAllowed { get; private set; }
        public bool ResetAllowed { get; private set; }

        public void Publish(OpenVrPlayspaceInputSample sample) => _coordinator.PublishPlayspaceInput(sample);

        public OpenVrPlayspaceDragUpdate Poll(bool dashboard, float x)
        {
            OpenVrSharedPlayspaceInput shared = _coordinator.Read();
            _dashboardGate.Apply(dashboard, shared.InputSample, shared.InputRevision != _lastRevision,
                out bool drag, out bool reset);
            _lastRevision = shared.InputRevision;
            DragAllowed = drag;
            ResetAllowed = reset;
            _poses[1].mDeviceToAbsoluteTracking.m3 = x;
            return Controller.Update(drag, reset, 1, _poses);
        }

        public void Dispose()
        {
            _phoneLease.Dispose();
            Marshal.FreeHGlobal(_systemPointer);
            Marshal.FreeHGlobal(_chaperonePointer);
            GC.KeepAlive(_systemTable);
            GC.KeepAlive(_chaperoneTable);
        }
    }
}
