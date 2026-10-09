namespace VRPhoneScreenOverlay.SteamVR;

internal static class OpenVrPhoneShellGeometry
{
    public const float CornerRadius = 0.045f;
    private const float _border = 0.012f;
    public const int TextureSize = 512;

    public static (float Width, float Aspect) OuterSize(float width, float aspect)
    {
        float height = width / aspect;
        float padding = Math.Min(width, height) * _border;
        return (width + 2 * padding, (width + 2 * padding) / (height + 2 * padding));
    }

    public static bool ContainsScreenPoint(float u, float v, float aspect)
    {
        float radius = Math.Min(aspect, 1) * CornerRadius;
        return Distance((u - 0.5f) * aspect, v - 0.5f, aspect, 1, radius) <= Math.Min(aspect, 1) * 0.000001f;
    }

    public static byte[] Pixels(float aspect, bool rimOnly = false)
    {
        byte[] pixels = new byte[TextureSize * TextureSize * 4];
        float shortEdge = Math.Min(aspect, 1);
        float padding = shortEdge * _border;
        float width = aspect + 2 * padding;
        float height = 1 + 2 * padding;
        float radius = shortEdge * (CornerRadius + _border);
        float antialias = Math.Max(width, height) / TextureSize;
        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                float distance = Distance(((x + 0.5f) / TextureSize - 0.5f) * width,
                    ((y + 0.5f) / TextureSize - 0.5f) * height, width, height, radius);
                float coverage = Math.Clamp(0.5f - distance / antialias, 0, 1);
                if (rimOnly)
                {
                    float inner = Distance(((x + 0.5f) / TextureSize - 0.5f) * width,
                        ((y + 0.5f) / TextureSize - 0.5f) * height, aspect, 1, shortEdge * CornerRadius);
                    coverage *= Math.Clamp(0.5f + inner / antialias, 0, 1);
                }
                float rim = Math.Clamp(1 - Math.Max(0, -distance) / (shortEdge * 0.007f), 0, 1);
                int offset = (y * TextureSize + x) * 4;
                pixels[offset] = (byte)(22 + 64 * rim);
                pixels[offset + 1] = (byte)(26 + 70 * rim);
                pixels[offset + 2] = (byte)(33 + 76 * rim);
                pixels[offset + 3] = (byte)(coverage * 255);
            }
        }
        return pixels;
    }

    private static float Distance(float x, float y, float width, float height, float radius)
    {
        float qx = MathF.Abs(x) - (width / 2 - radius);
        float qy = MathF.Abs(y) - (height / 2 - radius);
        return MathF.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0)) +
            Math.Min(Math.Max(qx, qy), 0) - radius;
    }
}
