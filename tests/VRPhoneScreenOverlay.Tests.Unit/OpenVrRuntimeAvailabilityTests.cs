using Valve.VR;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OpenVrRuntimeAvailabilityTests
{
    [Fact]
    public async Task DisabledServiceWithoutRuntimeWaitsWithoutReportingFault()
    {
        using OpenVrPlayspaceDragService service = new(OpenVrControllerHand.Right, 1, () => false);
        TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool faulted = false;
        service.StateChanged += (_, args) =>
        {
            if (args.Snapshot.State == OpenVrPlayspaceDragState.Faulted) { faulted = true; }
            if (args.Snapshot.State == OpenVrPlayspaceDragState.WaitingForSteamVr) { ready.TrySetResult(); }
        };
        service.Start();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(service.Snapshot.Enabled);
        Assert.Equal(OpenVrReasonCodes.PlayspaceWaitingForSteamVr, service.Snapshot.ReasonCode);
        service.StopService();
        Assert.False(faulted);
    }

    [Theory]
    [InlineData(OpenVrControllerHand.Right, OpenVrControllerHand.Right, ETrackedControllerRole.RightHand)]
    [InlineData(OpenVrControllerHand.Left, OpenVrControllerHand.Right, ETrackedControllerRole.LeftHand)]
    [InlineData(OpenVrControllerHand.Left, OpenVrControllerHand.Left, ETrackedControllerRole.RightHand)]
    [InlineData(OpenVrControllerHand.Right, OpenVrControllerHand.Left, ETrackedControllerRole.LeftHand)]
    public void NativeHandFlipIsRelativeToPreparedBindings(OpenVrControllerHand desired, OpenVrControllerHand prepared, ETrackedControllerRole expected)
    {
        Assert.Equal(expected, OpenVrRuntimeHost.RelativeHandRole(desired, prepared));
    }
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, false, false)]
    public void UtilityServerAloneDoesNotAuthorizeOverlayInitialization(bool server, bool compositor, bool monitor, bool expected)
    {
        Assert.Equal(expected, OpenVrRuntimeAvailability.IsInteractiveRuntimeRunning(server, compositor, monitor));
    }
}
