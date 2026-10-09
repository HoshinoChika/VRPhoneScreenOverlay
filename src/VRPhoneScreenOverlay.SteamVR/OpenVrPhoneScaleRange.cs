namespace VRPhoneScreenOverlay.SteamVR;

internal static class OpenVrPhoneScaleRange
{
    public const float Minimum = 0.2f;

    public const float Maximum = 2.5f;

    public static float Clamp(float scaleFactor) =>
        Math.Clamp(scaleFactor, Minimum, Maximum);
}
