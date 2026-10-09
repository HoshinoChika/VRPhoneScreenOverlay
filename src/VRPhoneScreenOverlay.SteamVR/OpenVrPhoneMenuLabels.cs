namespace VRPhoneScreenOverlay.SteamVR;

// Raster labels, not a font file. Draw once per visual value change without
// bringing WinForms/GDI or font discovery into the real-time overlay module.
internal static class OpenVrPhoneMenuLabels
{
    private const int _width = 160;
    private const int _height = 32;
    private static readonly byte[] _alpha = Load();

    public static void DrawCentered(byte[] pixels, int label, int centerX, int centerY)
    {
        int left = _width, right = -1, top = _height, bottom = -1;
        for (int row = 0; row < _height; row++)
        {
            for (int column = 0; column < _width; column++)
            {
                if (_alpha[(label * _height + row) * _width + column] == 0) { continue; }
                left = Math.Min(left, column); right = Math.Max(right, column);
                top = Math.Min(top, row); bottom = Math.Max(bottom, row);
            }
        }
        if (right >= left) { Draw(pixels, label, centerX - (left + right) / 2, centerY - (top + bottom) / 2); }
    }

    public static void Draw(byte[] pixels, int label, int x, int y)
    {
        for (int row = 0; row < _height; row++)
        {
            for (int column = 0; column < _width; column++)
            {
                int alpha = _alpha[(label * _height + row) * _width + column];
                int px = x + column;
                int py = y + row;
                if (alpha == 0 || px < 0 || px >= OpenVrPhoneMenuLayout.Width || py < 0 || py >= pixels.Length / 4 / OpenVrPhoneMenuLayout.Width) { continue; }
                OpenVrUiCanvas.Pixel(pixels, px, py, VRPhoneScreenOverlay.Presentation.UiTheme.TextPrimary, alpha / 255f);
            }
        }
    }

    private static byte[] Load()
    {
        using Stream stream = typeof(OpenVrPhoneMenuLabels).Assembly.GetManifestResourceStream(
            "VRPhoneScreenOverlay.SteamVR.PhoneMenuLabels") ?? throw new InvalidOperationException("Menu label resource missing.");
        byte[] alpha = new byte[_width * _height * 31];
        stream.ReadExactly(alpha);
        return alpha;
    }
}
