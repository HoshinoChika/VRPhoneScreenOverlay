using VRPhoneScreenOverlay.Presentation;

namespace VRPhoneScreenOverlay.SteamVR;

internal enum OpenVrMenuIcon
{
    CloseCircle,
    Plus,
    Minus,
    ChevronRight,
    ChevronLeft,
    Back,
    Backspace,
    Check,
    Settings,
    Video,
}

/// <summary>Rounded white strokes on the same 24-unit grid as the desktop icons.</summary>
internal static class OpenVrMenuIcons
{
    public static void Draw(byte[] pixels, OpenVrMenuIcon icon, int left, int top, int size)
    {
        ReadOnlySpan<(float X1, float Y1, float X2, float Y2)> lines = icon switch
        {
            OpenVrMenuIcon.CloseCircle => [(8, 8, 16, 16), (16, 8, 8, 16)],
            OpenVrMenuIcon.Video => [(16, 10, 21, 7.5f), (21, 7.5f, 21, 16.5f), (21, 16.5f, 16, 14)],
            OpenVrMenuIcon.Plus => [(5, 12, 19, 12), (12, 5, 12, 19)],
            OpenVrMenuIcon.Minus => [(5, 12, 19, 12)],
            OpenVrMenuIcon.ChevronRight => [(9, 5, 16, 12), (16, 12, 9, 19)],
            OpenVrMenuIcon.ChevronLeft => [(15, 5, 8, 12), (8, 12, 15, 19)],
            OpenVrMenuIcon.Back => [(10, 5, 3, 12), (3, 12, 10, 19), (3, 12, 21, 12)],
            OpenVrMenuIcon.Backspace => [(8, 5, 21, 5), (21, 5, 21, 19), (21, 19, 8, 19),
                (8, 19, 2, 12), (2, 12, 8, 5), (11, 9, 17, 15), (17, 9, 11, 15)],
            OpenVrMenuIcon.Check => [(4, 12.5f, 9.5f, 18), (9.5f, 18, 20, 6)],
            OpenVrMenuIcon.Settings => [(12, 2, 12, 4), (12, 20, 12, 22), (2, 12, 4, 12), (20, 12, 22, 12),
                (4.93f, 4.93f, 6.34f, 6.34f), (17.66f, 17.66f, 19.07f, 19.07f),
                (4.93f, 19.07f, 6.34f, 17.66f), (17.66f, 6.34f, 19.07f, 4.93f)],
            _ => [],
        };
        float scale = size / 24f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = (x + 0.5f) / scale;
                float py = (y + 0.5f) / scale;
                float distance = float.MaxValue;
                foreach (var line in lines)
                {
                    float dx = line.X2 - line.X1;
                    float dy = line.Y2 - line.Y1;
                    float fraction = Math.Clamp(((px - line.X1) * dx + (py - line.Y1) * dy) / (dx * dx + dy * dy), 0, 1);
                    float rx = px - line.X1 - fraction * dx;
                    float ry = py - line.Y1 - fraction * dy;
                    distance = Math.Min(distance, MathF.Sqrt(rx * rx + ry * ry));
                }
                if (icon is OpenVrMenuIcon.CloseCircle or OpenVrMenuIcon.Settings)
                {
                    float radius = MathF.Sqrt((px - 12) * (px - 12) + (py - 12) * (py - 12));
                    distance = Math.Min(distance, MathF.Abs(radius - (icon == OpenVrMenuIcon.CloseCircle ? 9 : 8)));
                    if (icon == OpenVrMenuIcon.Settings) { distance = Math.Min(distance, MathF.Abs(radius - 3.5f)); }
                }
                if (icon == OpenVrMenuIcon.Video)
                {
                    float qx = MathF.Abs(px - 9.5f) - 4;
                    float qy = MathF.Abs(py - 12) - 3.5f;
                    float edge = MathF.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0)) + Math.Min(Math.Max(qx, qy), 0) - 2.5f;
                    distance = Math.Min(distance, MathF.Abs(edge));
                }
                float coverage = Math.Clamp((0.95f - distance) * scale + 0.5f, 0, 1);
                if (coverage > 0) { OpenVrUiCanvas.Pixel(pixels, left + x, top + y, UiTheme.TextPrimary, coverage); }
            }
        }
    }
}
