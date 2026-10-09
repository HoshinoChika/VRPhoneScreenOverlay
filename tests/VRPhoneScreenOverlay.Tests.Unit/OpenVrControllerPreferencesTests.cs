using System.Text.Json;
using Valve.VR;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OpenVrControllerPreferencesTests
{
    [Fact]
    public void HandRoutingSelectsMatchingInputSourceAndTrackedRole()
    {
        Assert.Equal(
            "/user/hand/left",
            OpenVrControllerHandRouting.InputSourcePath(OpenVrControllerHand.Left));
        Assert.Equal(
            ETrackedControllerRole.LeftHand,
            OpenVrControllerHandRouting.TrackedRole(OpenVrControllerHand.Left));
        Assert.Equal(
            "/user/hand/right",
            OpenVrControllerHandRouting.InputSourcePath(OpenVrControllerHand.Right));
        Assert.Equal(
            ETrackedControllerRole.RightHand,
            OpenVrControllerHandRouting.TrackedRole(OpenVrControllerHand.Right));
        Assert.Equal(
            OpenVrControllerHand.Right,
            OpenVrControllerHandRouting.Opposite(OpenVrControllerHand.Left));
        Assert.Equal(
            OpenVrControllerHand.Left,
            OpenVrControllerHandRouting.Opposite(OpenVrControllerHand.Right));
    }

    [Fact]
    public void SaveAndLoadRoundTripSelectedHand()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "VRPhoneScreenOverlay.Tests",
            Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "input.json");
        try
        {
            OpenVrControllerPreferences.SaveToPath(OpenVrControllerHand.Left, path);

            Assert.Equal(OpenVrControllerHand.Left, OpenVrControllerPreferences.LoadFromPath(path));
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            Assert.Equal("Left", document.RootElement.GetProperty("controllerHand").GetString());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
