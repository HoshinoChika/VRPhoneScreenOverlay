using System.Text.Json.Nodes;
using Valve.VR;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OpenVrInputCaptureTests
{
    [Fact]
    public void HoverCaptureCannotMaskNativeRecallBeforeRelease()
    {
        OpenVrRecallInputLifetime lifetime = new();
        lifetime.Recalled();
        lifetime.Observe(true, true, false);
        Assert.Equal(0, OpenVrPhoneInteraction.CapturePriority(true, true, true, false, lifetime.Finishing));
        lifetime.Observe(true, false, false);
        Assert.Equal(OpenVR.k_nActionSetOverlayGlobalPriorityMin,
            OpenVrPhoneInteraction.CapturePriority(true, true, true, false, lifetime.Finishing));
    }

    [Fact]
    public void NativeRecallKeepsItsPriorityUntilAPhysicalReleaseIsObserved()
    {
        OpenVrRecallInputLifetime lifetime = new();
        lifetime.Recalled();
        lifetime.Observe(true, false, false); // Old sample: another observer source is active.
        Assert.True(lifetime.Finishing);
        lifetime.Observe(false, false, false);
        Assert.True(lifetime.Finishing);
        lifetime.Observe(true, true, false);
        Assert.Equal(2, OpenVrPhoneInteraction.RecallPriority(false, false, lifetime.Finishing));
        lifetime.Observe(true, false, false);
        Assert.Equal(0, OpenVrPhoneInteraction.RecallPriority(false, false, lifetime.Finishing));
        lifetime.Recalled();
        lifetime.Observe(false, true, true);
        Assert.False(lifetime.Finishing);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void RecallOutranksItsSharedButtonObserverOnlyWhileHidden(bool hidden, bool dashboard)
    {
        int priority = OpenVrPhoneInteraction.RecallPriority(hidden, dashboard);
        if (hidden && !dashboard) { Assert.InRange(priority, 2, OpenVR.k_nActionSetOverlayGlobalPriorityMin - 1); }
        else { Assert.Equal(0, priority); }
    }

    [Theory]
    [InlineData(false, true, true, false, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(true, true, false, false, false)]
    [InlineData(true, true, true, false, true)]
    [InlineData(true, true, false, true, true)]
    public void CaptureStopsOutsideVisibleControlsAndOnTrackingLoss(bool available, bool valid,
        bool hovered, bool grabbed, bool expected) =>
        Assert.Equal(expected ? OpenVR.k_nActionSetOverlayGlobalPriorityMin : 0,
            OpenVrPhoneInteraction.CapturePriority(available, valid, hovered, grabbed));

    [Theory]
    [InlineData(OpenVrControllerHand.Left, "left")]
    [InlineData(OpenVrControllerHand.Right, "right")]
    public void DriverControlsAreCapturedOnlyForTheSelectedHand(OpenVrControllerHand hand, string side)
    {
        JsonNode binding = JsonNode.Parse("""
            {"bindings":{"/actions/main":{"sources":[
            {"path":"/user/hand/right/input/custom","mode":"button","inputs":{"click":{"output":"/actions/main/in/phoneback"}}}]}}}
            """)!;
        string originalMain = binding["bindings"]!["/actions/main"]!.ToJsonString();
        JsonNode profile = JsonNode.Parse("""
            {"input_source":{
            "/input/trigger":{"type":"trigger"},
            "/input/joystick":{"type":"joystick"},
            "/input/a":{"type":"button","side":"right"},
            "/input/x":{"type":"button","side":"left"},
            "/pose/raw":{"type":"pose"},
            "/output/haptic":{"type":"haptic"}}}
            """)!;
        OpenVrInputCaptureBindings.Add(binding, hand, profile: profile);
        JsonArray sources = binding["bindings"]![OpenVrInputCaptureBindings.ActionSet]!["sources"]!.AsArray();
        Assert.All(sources, source => Assert.StartsWith("/user/hand/" + side + "/input/", source!["path"]!.GetValue<string>()));
        Assert.Contains(sources, source => source!["inputs"]?["position"]?["output"]?.GetValue<string>() == "/actions/phonecapture/in/axis");
        Assert.Contains(sources, source => source!["inputs"]?["pull"]?["output"]?.GetValue<string>() == "/actions/phonecapture/in/scalar");
        Assert.Equal(originalMain, binding["bindings"]!["/actions/main"]!.ToJsonString());
    }
}
