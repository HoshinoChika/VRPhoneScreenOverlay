using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

internal static class OpenVrPhonePlacement
{
    public static HmdMatrix34_t InFrontOf(HmdMatrix34_t head, float distance)
    {
        HmdMatrix34_t relative = OpenVrTransformMath.Identity();
        relative.m11 = -distance;
        return OpenVrTransformMath.Multiply(head, relative);
    }
}
