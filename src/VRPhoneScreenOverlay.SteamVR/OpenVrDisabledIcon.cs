namespace VRPhoneScreenOverlay.SteamVR;

internal static class OpenVrDisabledIcon
{
    public static byte[] Create(byte[] source)
    {
        byte[] pixels = (byte[])source.Clone();
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte gray = (byte)((pixels[i] + pixels[i + 1] + pixels[i + 2]) / 6);
            pixels[i] = pixels[i + 1] = pixels[i + 2] = gray;
        }
        return pixels;
    }
}
