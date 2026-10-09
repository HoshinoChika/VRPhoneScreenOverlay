using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

internal static class OpenVrControllerHandRouting
{
    public static OpenVrControllerHand Opposite(OpenVrControllerHand hand) => hand switch
    {
        OpenVrControllerHand.Left => OpenVrControllerHand.Right,
        _ => OpenVrControllerHand.Left,
    };

    public static string InputSourcePath(OpenVrControllerHand hand) => hand switch
    {
        OpenVrControllerHand.Left => "/user/hand/left",
        _ => "/user/hand/right",
    };

    public static ETrackedControllerRole TrackedRole(OpenVrControllerHand hand) => hand switch
    {
        OpenVrControllerHand.Left => ETrackedControllerRole.LeftHand,
        _ => ETrackedControllerRole.RightHand,
    };
}
