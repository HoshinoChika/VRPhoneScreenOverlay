namespace VRPhoneScreenOverlay.Presentation;

public static class RoundedPanelGeometry
{
    public const float CornerRadiusRatio = 0.045f;

    public static bool Contains(float x, float y, float width, float height) =>
        float.IsFinite(x) && float.IsFinite(y) && x >= 0 && y >= 0 && x <= width && y <= height &&
        Distance(x, y, width, height) <= 0;

    public static void Apply(byte[] rgba, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if (rgba.Length < checked(width * height * 4)) { throw new ArgumentException("Insufficient panel pixels.", nameof(rgba)); }
        int corner = (int)MathF.Ceiling(Math.Min(width, height) * CornerRadiusRatio) + 1;
        for (int y = 0; y < height; y++)
        {
            if (y >= corner && y < height - corner) { continue; }
            for (int x = 0; x < width; x++)
            {
                if (x >= corner && x < width - corner) { continue; }
                float coverage = Math.Clamp(0.5f - Distance(x + 0.5f, y + 0.5f, width, height), 0, 1);
                int alpha = (y * width + x) * 4 + 3;
                rgba[alpha] = (byte)(rgba[alpha] * coverage);
            }
        }
    }

    private static float Distance(float x, float y, float width, float height)
    {
        float radius = Math.Min(width, height) * CornerRadiusRatio;
        float qx = MathF.Abs(x - width / 2) - (width / 2 - radius);
        float qy = MathF.Abs(y - height / 2) - (height / 2 - radius);
        return MathF.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0)) + Math.Min(Math.Max(qx, qy), 0) - radius;
    }
}
