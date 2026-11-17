using VRPhoneScreenOverlay.Presentation;

namespace VRPhoneScreenOverlay.SteamVR;

public enum OpenVrPhonePlaceholderState { Disconnected, Connecting, Failed }

internal static class OpenVrDisconnectedPhonePixels
{
    public const int Size = 360;

    public static byte[] Create(OpenVrPhonePlaceholderState state = OpenVrPhonePlaceholderState.Disconnected)
    {
        byte[] pixels = new byte[Size * Size * 4];
        OpenVrUiCanvas.Card(pixels, 0, 0, Size, Size, UiTheme.Surface, 14);
        using Stream labels = typeof(OpenVrDisconnectedPhonePixels).Assembly.GetManifestResourceStream(
            "VRPhoneScreenOverlay.SteamVR.Phone" + state) ?? throw new InvalidOperationException("Phone placeholder label missing.");
        byte[] alpha = new byte[Size * 128];
        labels.ReadExactly(alpha);
        for (int y = 0; y < 128; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                OpenVrUiCanvas.Pixel(pixels, x, 124 + y,
                    y < 64 ? UiTheme.TextPrimary : UiTheme.TextSecondary, alpha[y * Size + x] / 255f);
            }
        }
        RoundedPanelGeometry.Apply(pixels, Size, Size);
        return pixels;
    }
}
