using VRPhoneScreenOverlay.Session;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class GenericControllerGroupTests
{
    [Theory]
    [InlineData(false, null, "pico_controller")]
    [InlineData(true, "pico_controller", "pico_controller")]
    [InlineData(true, "pico_controller_ice", "pico_controller")]
    [InlineData(true, "vive_controller", "vive_controller")]
    [InlineData(true, "knuckles", "knuckles")]
    [InlineData(true, "hpmotioncontroller", "hpmotioncontroller")]
    [InlineData(true, "unknown_controller", "generic")]
    [InlineData(true, null, "generic")]
    [InlineData(true, "holographic_controller", "generic")]
    public void DetectionSelectsTheCorrespondingGroupWithoutGuessingAnAmbiguousBrand(bool connected, string? type, string expected)
        => Assert.Equal(expected, ControllerBindingGuideService.SelectDetectedGroup(new(connected, type, "test"), null));

    [Fact]
    public async Task GenericInstructionsEqualPicoDefaultsForBothHands()
    {
        foreach (OpenVrControllerHand hand in Enum.GetValues<OpenVrControllerHand>())
        {
            ControllerBindingGuide pico = await OpenVrBindingGuide.ReadDefaultAsync("pico_controller", hand, CancellationToken.None);
            ControllerBindingGuide generic = await OpenVrBindingGuide.ReadDefaultAsync("generic", hand, CancellationToken.None);
            Assert.Equal(pico.Entries, generic.Entries);
        }
        ControllerBindingGuide detected = await ControllerBindingGuideService.ReadDetectedAsync(new(true, "unknown_controller", "test"), null, CancellationToken.None);
        Assert.Equal("generic", detected.SelectionKey);
        Assert.True(detected.Unrecognized);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("")]
    [InlineData("unknown/controller")]
    public void InvalidUnknownTypesCannotGenerateBindingPaths(string type)
        => Assert.Throws<InvalidDataException>(() => OpenVrGenericBinding.Create(type));
}
