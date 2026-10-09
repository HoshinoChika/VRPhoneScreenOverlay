using VRPhoneScreenOverlay.Presentation;

namespace VRPhoneScreenOverlay.SteamVR;

internal static class OpenVrUiCanvas
{
    private const string _characters = "0123456789%×+−›X.";
    private static readonly byte[] _glyphs = LoadGlyphs();

    public static void Pixel(byte[] pixels, int x, int y, uint color, float coverage)
    {
        if (x < 0 || x >= 360 || y < 0 || y >= pixels.Length / 1440) { return; }
        int offset = (y * 360 + x) * 4;
        float alpha = ((color >> 24) / 255f) * coverage;
        float oldAlpha = pixels[offset + 3] / 255f;
        float combined = alpha + oldAlpha * (1 - alpha);
        if (combined <= 0) { return; }
        for (int c = 0; c < 3; c++)
        {
            byte value = (byte)((color >> (16 - c * 8)) & 255);
            pixels[offset + c] = (byte)Math.Clamp((value * alpha + pixels[offset + c] * oldAlpha * (1 - alpha)) / combined, 0, 255);
        }
        pixels[offset + 3] = (byte)Math.Clamp(combined * 255, 0, 255);
    }

    public static void Rounded(byte[] pixels, int x, int y, int width, int height, float radius, uint color)
    {
        for (int row = y; row < y + height; row++)
        {
            for (int col = x; col < x + width; col++)
            {
                float dx = Math.Max(MathF.Abs(col + 0.5f - x - width / 2f) - (width / 2f - radius), 0);
                float dy = Math.Max(MathF.Abs(row + 0.5f - y - height / 2f) - (height / 2f - radius), 0);
                float coverage = Math.Clamp(radius + 0.5f - MathF.Sqrt(dx * dx + dy * dy), 0, 1);
                Pixel(pixels, col, row, color, coverage);
            }
        }
    }

    public static void Card(byte[] pixels, int x, int y, int width, int height, uint color, int radius = 10)
    {
        Rounded(pixels, x, y, width, height, radius, UiTheme.BorderSoft);
        Rounded(pixels, x + 1, y + 1, width - 2, height - 2, radius - 1, color);
    }

    public static void Text(byte[] pixels, string text, int x, int y, float size = 1)
    {
        int width = (int)(24 * size);
        int height = (int)(32 * size);
        foreach (char character in text)
        {
            int glyph = _characters.IndexOf(character, StringComparison.Ordinal);
            if (glyph >= 0)
            {
                for (int row = 0; row < height; row++)
                {
                    for (int col = 0; col < width; col++)
                    {
                        float sourceX = col / size;
                        float sourceY = row / size;
                        int x0 = Math.Min(23, (int)sourceX);
                        int y0 = Math.Min(31, (int)sourceY);
                        int x1 = Math.Min(23, x0 + 1);
                        int y1 = Math.Min(31, y0 + 1);
                        float dx = sourceX - x0;
                        float dy = sourceY - y0;
                        int start = glyph * 32 * 24;
                        float top = _glyphs[start + y0 * 24 + x0] * (1 - dx) + _glyphs[start + y0 * 24 + x1] * dx;
                        float bottom = _glyphs[start + y1 * 24 + x0] * (1 - dx) + _glyphs[start + y1 * 24 + x1] * dx;
                        Pixel(pixels, x + col, y + row, UiTheme.TextPrimary, (top * (1 - dy) + bottom * dy) / 255f);
                    }
                }
            }
            x += (int)(18 * size);
        }
    }

    private static byte[] LoadGlyphs()
    {
        using Stream stream = typeof(OpenVrUiCanvas).Assembly.GetManifestResourceStream(
            "VRPhoneScreenOverlay.SteamVR.ControlGlyphs") ?? throw new InvalidOperationException("Control glyph resource missing.");
        byte[] glyphs = new byte[_characters.Length * 24 * 32];
        stream.ReadExactly(glyphs);
        return glyphs;
    }
}
