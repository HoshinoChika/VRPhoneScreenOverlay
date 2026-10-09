using VRPhoneScreenOverlay.Session;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class PhoneOverlayLogGateTests
{
    [Fact]
    public void FirstInteractionSnapshotIsLogged()
    {
        PhoneOverlayLogGate gate = new();
        Assert.True(gate.ShouldLogInteraction(Interaction()));
    }

    [Fact]
    public void RepeatingTheSameInteractionSnapshotIsNotLogged()
    {
        PhoneOverlayLogGate gate = new();
        gate.ShouldLogInteraction(Interaction());

        Assert.False(gate.ShouldLogInteraction(Interaction()));
        Assert.False(gate.ShouldLogInteraction(Interaction()));
    }

    [Fact]
    public void EachTrackedInteractionFieldTriggersALog()
    {
        PhoneOverlayLogGate gate = new();
        gate.ShouldLogInteraction(Interaction());

        Assert.True(gate.ShouldLogInteraction(Interaction(reasonCode: "OTHER")));
        Assert.True(gate.ShouldLogInteraction(Interaction(reasonCode: "OTHER", worldAnchored: true)));
        Assert.True(gate.ShouldLogInteraction(
            Interaction(reasonCode: "OTHER", worldAnchored: true, grabbed: true)));
        Assert.True(gate.ShouldLogInteraction(
            Interaction(reasonCode: "OTHER", worldAnchored: true, grabbed: true, hovered: true)));
        Assert.True(gate.ShouldLogInteraction(Interaction(
            reasonCode: "OTHER",
            worldAnchored: true,
            grabbed: true,
            hovered: true,
            controllerPoseValid: true)));
    }

    [Fact]
    public void BindingHealthIsLoggedOnlyWhenTheReasonCodeChanges()
    {
        PhoneOverlayLogGate gate = new();

        Assert.True(gate.ShouldLogBindingHealth("READY"));
        Assert.False(gate.ShouldLogBindingHealth("READY"));
        Assert.True(gate.ShouldLogBindingHealth("DEGRADED"));
        Assert.True(gate.ShouldLogBindingHealth("READY"));
    }

    [Fact]
    public void SubmittedSizeIsLoggedOnlyWhenItChanges()
    {
        PhoneOverlayLogGate gate = new();

        Assert.True(gate.ShouldLogSubmittedSize(1080, 2400));
        Assert.False(gate.ShouldLogSubmittedSize(1080, 2400));
        Assert.True(gate.ShouldLogSubmittedSize(2400, 1080));
    }

    [Fact]
    public void FirstQualitySampleWaitsOneWholeInterval()
    {
        // The session clock starts at zero, so logging must not fire on the first frame.
        PhoneOverlayLogGate gate = new();
        TimeSpan interval = TimeSpan.FromSeconds(5);

        Assert.False(gate.ShouldLogQualitySample(TimeSpan.Zero, interval));
        Assert.False(gate.ShouldLogQualitySample(TimeSpan.FromSeconds(4.9), interval));
        Assert.True(gate.ShouldLogQualitySample(TimeSpan.FromSeconds(5), interval));
    }

    [Fact]
    public void QualitySamplesAreSpacedByTheInterval()
    {
        PhoneOverlayLogGate gate = new();
        TimeSpan interval = TimeSpan.FromSeconds(5);
        gate.ShouldLogQualitySample(TimeSpan.FromSeconds(5), interval);

        Assert.False(gate.ShouldLogQualitySample(TimeSpan.FromSeconds(9), interval));
        Assert.True(gate.ShouldLogQualitySample(TimeSpan.FromSeconds(10), interval));
    }

    private static OpenVrPhoneInteractionSnapshot Interaction(
        string reasonCode = "READY",
        bool worldAnchored = false,
        bool grabbed = false,
        bool hovered = false,
        bool controllerPoseValid = false) =>
        OpenVrPhoneInteractionSnapshot.Stopped with
        {
            ReasonCode = reasonCode,
            WorldAnchored = worldAnchored,
            Grabbed = grabbed,
            Hovered = hovered,
            ControllerPoseValid = controllerPoseValid,
        };
}
