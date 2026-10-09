using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OpenVrActionUpdateCoordinatorTests
{
    [Fact]
    public void PanelCommandsCoalesceAndGuiChangesOverridePendingCommands()
    {
        OpenVrActionUpdateCoordinator coordinator = new();
        coordinator.RequestControls(true, 5);
        coordinator.RequestControls(true, 20);
        Assert.Equal(1, coordinator.CoalescedControls);
        Assert.Equal(new OpenVrPlayspaceControlRequest(true, 20), coordinator.TakeControls());
        Assert.Null(coordinator.TakeControls());
        coordinator.RequestControls(true, 40);
        coordinator.SetPlayspaceMultiplier(10);
        Assert.Null(coordinator.TakeControls());
        Assert.Equal(10, coordinator.Read().Multiplier);
    }

    [Theory]
    [InlineData(1, true, 5)]
    [InlineData(40, true, 40)]
    [InlineData(20, false, 10)]
    [InlineData(1, false, 1)]
    public void PanelUsesTheExistingMainWindowMultiplierSteps(float current, bool increase, int expected) =>
        Assert.Equal(expected, OpenVrPhoneMenuState.NextMultiplier(current, increase));

    [Fact]
    public void PhonePollerOwnsActionUpdatesAndPublishesPlayspaceInput()
    {
        OpenVrActionUpdateCoordinator coordinator = new();
        coordinator.SetPlayspaceEnabled(true);

        using (coordinator.AcquirePhonePoller())
        {
            coordinator.PublishPlayspaceInput(new(false, true, true, true));

            OpenVrSharedPlayspaceInput active = coordinator.Read();
            Assert.True(active.PhonePollerActive);
            Assert.True(active.PlayspaceEnabled);
            Assert.True(active.DragPressed);
            Assert.True(active.ResetPressed);
        }

        OpenVrSharedPlayspaceInput released = coordinator.Read();
        Assert.False(released.PhonePollerActive);
        Assert.False(released.DragPressed);
        Assert.False(released.ResetPressed);
    }

    [Fact]
    public void PhoneActionUpdateOwnershipIsExclusive()
    {
        OpenVrActionUpdateCoordinator coordinator = new();
        using IDisposable lease = coordinator.AcquirePhonePoller();

        Assert.Throws<InvalidOperationException>(() => coordinator.AcquirePhonePoller());
    }

    [Fact]
    public void DisablingPlayspaceClearsPublishedDragState()
    {
        OpenVrActionUpdateCoordinator coordinator = new();
        coordinator.SetPlayspaceEnabled(true);
        using IDisposable lease = coordinator.AcquirePhonePoller();
        coordinator.PublishPlayspaceInput(new(false, true, false, true));

        coordinator.SetPlayspaceEnabled(false);

        OpenVrSharedPlayspaceInput snapshot = coordinator.Read();
        Assert.False(snapshot.PlayspaceEnabled);
        Assert.False(snapshot.DragPressed);
    }
}
