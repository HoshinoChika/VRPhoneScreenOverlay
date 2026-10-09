using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

internal static class OpenVrTransformMath
{
    public static HmdMatrix34_t Identity() => new()
    {
        m0 = 1,
        m5 = 1,
        m10 = 1,
    };

    public static HmdMatrix34_t Multiply(HmdMatrix34_t left, HmdMatrix34_t right) => new()
    {
        m0 = left.m0 * right.m0 + left.m1 * right.m4 + left.m2 * right.m8,
        m1 = left.m0 * right.m1 + left.m1 * right.m5 + left.m2 * right.m9,
        m2 = left.m0 * right.m2 + left.m1 * right.m6 + left.m2 * right.m10,
        m3 = left.m0 * right.m3 + left.m1 * right.m7 + left.m2 * right.m11 + left.m3,
        m4 = left.m4 * right.m0 + left.m5 * right.m4 + left.m6 * right.m8,
        m5 = left.m4 * right.m1 + left.m5 * right.m5 + left.m6 * right.m9,
        m6 = left.m4 * right.m2 + left.m5 * right.m6 + left.m6 * right.m10,
        m7 = left.m4 * right.m3 + left.m5 * right.m7 + left.m6 * right.m11 + left.m7,
        m8 = left.m8 * right.m0 + left.m9 * right.m4 + left.m10 * right.m8,
        m9 = left.m8 * right.m1 + left.m9 * right.m5 + left.m10 * right.m9,
        m10 = left.m8 * right.m2 + left.m9 * right.m6 + left.m10 * right.m10,
        m11 = left.m8 * right.m3 + left.m9 * right.m7 + left.m10 * right.m11 + left.m11,
    };

    public static HmdMatrix34_t InverseRigid(HmdMatrix34_t value)
    {
        HmdMatrix34_t result = new()
        {
            m0 = value.m0,
            m1 = value.m4,
            m2 = value.m8,
            m4 = value.m1,
            m5 = value.m5,
            m6 = value.m9,
            m8 = value.m2,
            m9 = value.m6,
            m10 = value.m10,
        };
        result.m3 = -(result.m0 * value.m3 + result.m1 * value.m7 + result.m2 * value.m11);
        result.m7 = -(result.m4 * value.m3 + result.m5 * value.m7 + result.m6 * value.m11);
        result.m11 = -(result.m8 * value.m3 + result.m9 * value.m7 + result.m10 * value.m11);
        return result;
    }

    public static HmdMatrix34_t Smooth(
        HmdMatrix34_t current,
        HmdMatrix34_t target,
        float elapsedSeconds)
    {
        float positionAlpha = 1f - MathF.Exp(-elapsedSeconds / 0.025f);
        current.m3 += (target.m3 - current.m3) * positionAlpha;
        current.m7 += (target.m7 - current.m7) * positionAlpha;
        current.m11 += (target.m11 - current.m11) * positionAlpha;

        float rotationAlpha = 1f - MathF.Exp(-elapsedSeconds / 0.04f);
        float x0 = Lerp(current.m0, target.m0, rotationAlpha);
        float x1 = Lerp(current.m4, target.m4, rotationAlpha);
        float x2 = Lerp(current.m8, target.m8, rotationAlpha);
        Normalize(ref x0, ref x1, ref x2);
        float y0 = Lerp(current.m1, target.m1, rotationAlpha);
        float y1 = Lerp(current.m5, target.m5, rotationAlpha);
        float y2 = Lerp(current.m9, target.m9, rotationAlpha);
        float dot = x0 * y0 + x1 * y1 + x2 * y2;
        y0 -= dot * x0;
        y1 -= dot * x1;
        y2 -= dot * x2;
        Normalize(ref y0, ref y1, ref y2);

        current.m0 = x0;
        current.m4 = x1;
        current.m8 = x2;
        current.m1 = y0;
        current.m5 = y1;
        current.m9 = y2;
        current.m2 = x1 * y2 - x2 * y1;
        current.m6 = x2 * y0 - x0 * y2;
        current.m10 = x0 * y1 - x1 * y0;
        return current;
    }

    private static float Lerp(float from, float to, float amount) =>
        from + ((to - from) * amount);

    private static void Normalize(ref float x, ref float y, ref float z)
    {
        float length = MathF.Sqrt((x * x) + (y * y) + (z * z));
        if (length < 0.000001f)
        {
            return;
        }

        x /= length;
        y /= length;
        z /= length;
    }
}
