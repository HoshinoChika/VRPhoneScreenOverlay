using System.Reflection;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OpenVrPlayspaceSnapshotTests
{
    [Fact]
    public void DelayedCoordinatesCannotReverseTheLatestControls()
    {
        using OpenVrPlayspaceDragService service = new(OpenVrControllerHand.Right, 5);
        OpenVrPlayspaceDragSnapshot stale = service.Snapshot;
        service.SetEnabled(true);
        service.SetMultiplier(20);
        MethodInfo publish = typeof(OpenVrPlayspaceDragService).GetMethod("Publish", BindingFlags.NonPublic | BindingFlags.Instance)!;
        publish.Invoke(service, [stale with { OffsetX = 2 }]);
        Assert.True(service.Snapshot.Enabled);
        Assert.Equal(20, service.Snapshot.Multiplier);
        Assert.Equal(2, service.Snapshot.OffsetX);
        Assert.True(OpenVrRuntimeHost.ActionUpdates.Read().PlayspaceEnabled);
        stale = service.Snapshot;
        service.SetEnabled(false);
        publish.Invoke(service, [stale with { OffsetX = 0 }]);
        Assert.False(service.Snapshot.Enabled);
        Assert.False(OpenVrRuntimeHost.ActionUpdates.Read().PlayspaceEnabled);
    }
}
