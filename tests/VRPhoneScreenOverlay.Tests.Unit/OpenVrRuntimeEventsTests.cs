using Valve.VR;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OpenVrRuntimeEventsTests
{
    [Fact]
    public void BindingTransitionsSuspendInputAndReleaseTheGateAfterRecoveryOrShutdown()
    {
        OpenVrRuntimeEvents events = new();
        events.BeginBindingChange();
        Assert.True(events.BindingChanging);
        long began = events.BindingRevision;
        events.EndBindingChange();
        Assert.False(events.BindingChanging);
        Assert.True(events.BindingRevision > began);
        events.BeginBindingChange();
        events.Reset();
        Assert.False(events.BindingChanging);
        events.EndBindingChange();
        Assert.False(events.BindingChanging);
    }

    [Theory]
    [InlineData(EVREventType.VREvent_StandingZeroPoseReset)]
    [InlineData(EVREventType.VREvent_SeatedZeroPoseReset)]
    public void RuntimeRecenterInvalidatesThePlayspaceBasis(EVREventType type)
    {
        OpenVrRuntimeEvents events = new();
        events.Observe(type);
        Assert.Equal(1, events.OriginRevision);
        Assert.False(events.TakeBindingFailure());
    }

    [Fact]
    public void PreviewNotificationsDoNotTriggerAResetLoop()
    {
        OpenVrRuntimeEvents events = new();
        events.Observe(EVREventType.VREvent_ChaperoneTempDataHasChanged);
        events.Observe(EVREventType.VREvent_ChaperoneUniverseHasChanged);
        Assert.Equal(0, events.OriginRevision);
    }

    [Fact]
    public void BindingFailuresSurviveConsumptionByTheOtherPollerAndAreConsumedOnce()
    {
        OpenVrRuntimeEvents events = new();
        events.Observe(EVREventType.VREvent_Input_BindingLoadFailed);
        events.Observe(EVREventType.VREvent_StandingZeroPoseReset);
        events.Observe(EVREventType.VREvent_Input_BindingLoadFailed);
        Assert.True(events.TakeBindingFailure());
        Assert.False(events.TakeBindingFailure());
        Assert.Equal(1, events.OriginRevision);
    }

    [Fact]
    public void RoomSetupSuspendsOffsetsUntilTheNewCalibrationIsReady()
    {
        OpenVrRuntimeEvents events = new();
        events.Observe(EVREventType.VREvent_ChaperoneRoomSetupStarting);
        Assert.True(events.RoomSetupActive);
        events.Observe(EVREventType.VREvent_ChaperoneRoomSetupFinished);
        Assert.False(events.RoomSetupActive);
        Assert.Equal(2, events.OriginRevision);
        events.Observe(EVREventType.VREvent_Input_BindingLoadFailed);
        events.Reset();
        Assert.False(events.TakeBindingFailure());
        Assert.Equal(3, events.OriginRevision);
    }
}
