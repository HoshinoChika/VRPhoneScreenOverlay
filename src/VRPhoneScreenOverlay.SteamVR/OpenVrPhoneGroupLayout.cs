using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

internal enum OpenVrPhoneSurface { Phone, Dot, Menu, Keypad }

// Components share a rigid group pose. Only the phone's dimensions depend on
// scale; the controls retain their physical sizes and follow its edge layout.
internal static class OpenVrPhoneGroupLayout
{
    public static HmdMatrix34_t SurfacePose(OpenVrPhoneSurface surface, HmdMatrix34_t root,
        float phoneWidth, float aspect, bool hidden)
    {
        HmdMatrix34_t dot = OpenVrPhoneMenuLayout.DotTransform(hidden, root, phoneWidth, aspect, root);
        return surface switch
        {
            OpenVrPhoneSurface.Dot => dot,
            OpenVrPhoneSurface.Menu => OpenVrPhoneMenuLayout.PanelTransform(dot),
            OpenVrPhoneSurface.Keypad => OpenVrPhoneMenuLayout.KeypadTransform(root, phoneWidth, aspect),
            _ => root,
        };
    }

    public static HmdVector3_t Point(OpenVrPhoneSurface surface, HmdMatrix34_t root, float phoneWidth,
        float aspect, bool hidden, float u, float v)
    {
        HmdMatrix34_t pose = SurfacePose(surface, root, phoneWidth, aspect, hidden);
        (float width, float height) = surface switch
        {
            OpenVrPhoneSurface.Dot => (OpenVrPhoneMenuLayout.DotWidth, OpenVrPhoneMenuLayout.DotWidth),
            OpenVrPhoneSurface.Menu => (OpenVrPhoneMenuLayout.PanelWidth, OpenVrPhoneMenuLayout.PanelWidth / OpenVrPhoneMenuLayout.PanelAspect),
            OpenVrPhoneSurface.Keypad => (OpenVrPhoneMenuLayout.KeypadWidth, OpenVrPhoneMenuLayout.KeypadWidth / OpenVrPhoneMenuLayout.KeypadAspect),
            _ => (phoneWidth, phoneWidth / aspect),
        };
        float x = (u - 0.5f) * width;
        float y = (v - 0.5f) * height;
        return new()
        {
            v0 = pose.m3 + pose.m0 * x + pose.m1 * y,
            v1 = pose.m7 + pose.m4 * x + pose.m5 * y,
            v2 = pose.m11 + pose.m8 * x + pose.m9 * y
        };
    }

    public static HmdMatrix34_t AnchorScale(OpenVrPhoneSurface surface, HmdMatrix34_t root,
        float before, float after, float aspect, bool hidden, float u, float v)
    {
        HmdVector3_t oldPoint = Point(surface, root, before, aspect, hidden, u, v);
        HmdVector3_t newPoint = Point(surface, root, after, aspect, hidden, u, v);
        root.m3 += oldPoint.v0 - newPoint.v0;
        root.m7 += oldPoint.v1 - newPoint.v1;
        root.m11 += oldPoint.v2 - newPoint.v2;
        return root;
    }
}
