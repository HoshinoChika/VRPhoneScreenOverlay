using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

internal readonly record struct OpenVrPhoneMenuHit(OpenVrMenuTarget Target, float SliderFraction,
    VROverlayIntersectionResults_t Intersection, OpenVrPhoneSurface Surface = OpenVrPhoneSurface.Phone);

internal static class OpenVrPhoneMenuLayout
{
    public const int Width = 360;
    public const int Height = 640;
    public const int MotionHeight = 504;
    public const float DotWidth = 0.056f;
    public const float ControlGap = 0.008f;
    public const float PanelWidth = 0.32f;
    public const float PanelAspect = Width / (float)Height;
    public const int KeypadHeight = 320;
    public const float KeypadWidth = 0.26f;
    public const float KeypadAspect = Width / (float)KeypadHeight;

    public static HmdMatrix34_t DotTransform(bool hidden, HmdMatrix34_t phone, float width, float aspect, HmdMatrix34_t head)
    {
        HmdMatrix34_t relative = OpenVrTransformMath.Identity();
        if (hidden) { relative.m7 = 0.26f; relative.m11 = -0.85f; }
        else
        {
            var shell = OpenVrPhoneShellGeometry.OuterSize(width, aspect);
            relative.m3 = shell.Width / 2 + ControlGap + DotWidth / 2;
            relative.m7 = shell.Width / shell.Aspect / 2 - DotWidth / 2;
        }
        return OpenVrTransformMath.Multiply(hidden ? head : phone, relative);
    }

    public static HmdMatrix34_t PanelTransform(HmdMatrix34_t dot)
    {
        HmdMatrix34_t relative = OpenVrTransformMath.Identity();
        relative.m3 = DotWidth / 2 + ControlGap + PanelWidth / 2;
        relative.m7 = DotWidth / 2 - PanelWidth / PanelAspect / 2;
        return OpenVrTransformMath.Multiply(dot, relative);
    }

    public static HmdMatrix34_t KeypadTransform(HmdMatrix34_t phone, float width, float aspect)
    {
        HmdMatrix34_t relative = OpenVrTransformMath.Identity();
        relative.m3 = width / 2 + KeypadWidth / 2;
        relative.m7 = -width / aspect / 2 + KeypadWidth / KeypadAspect / 2;
        // If the phone is very short, keep the keyboard below the sidebar.
        float menuBottom = width / aspect / 2 - PanelWidth * 564 / Width;
        relative.m7 = Math.Min(relative.m7, menuBottom - KeypadWidth / KeypadAspect / 2 - 0.008f);
        return OpenVrTransformMath.Multiply(phone, relative);
    }

    public static bool InsideDot(float x, float y) =>
        float.IsFinite(x) && float.IsFinite(y) && x is >= 0 and <= 1 && y is >= 0 and <= 1;

    public static bool TryMap(float x, float y, bool keypad, out OpenVrMenuTarget target, out float fraction, bool motion = false, bool video = false)
    {
        target = OpenVrMenuTarget.None;
        fraction = 0;
        if (!float.IsFinite(x) || !float.IsFinite(y) || x is < 0 or > 1 || y < 0 || y * Height > (motion || video ? MotionHeight : keypad ? 564 : 500)) { return false; }
        float px = x * Width;
        float py = y * Height;
        if (video)
        {
            if (py is >= 12 and < 68) { target = OpenVrMenuTarget.VideoBack; }
            else if (py is >= 100 and < 156 && px >= 170) { target = px < 232 ? OpenVrMenuTarget.ResolutionDown : px >= 294 ? OpenVrMenuTarget.ResolutionUp : OpenVrMenuTarget.None; }
            else if (py is >= 180 and < 236 && px >= 170) { target = px < 232 ? OpenVrMenuTarget.BitrateDown : px >= 294 ? OpenVrMenuTarget.BitrateUp : OpenVrMenuTarget.None; }
            else if (py is >= 260 and < 316 && px >= 170) { target = px < 232 ? OpenVrMenuTarget.FrameRateDown : px >= 294 ? OpenVrMenuTarget.FrameRateUp : OpenVrMenuTarget.None; }
            else if (py is >= 360 and < 416) { target = OpenVrMenuTarget.VideoApply; }
            return true;
        }
        if (motion)
        {
            if (py is >= 12 and < 68) { target = OpenVrMenuTarget.MotionBack; }
            else if (py is >= 80 and < 136 && px >= 170) { target = px < 232 ? OpenVrMenuTarget.MultiplierDown : px >= 294 ? OpenVrMenuTarget.MultiplierUp : OpenVrMenuTarget.None; }
            else if (py is >= 150 and < 206 && px >= 170) { target = px < 232 ? OpenVrMenuTarget.StrengthDown : px >= 294 ? OpenVrMenuTarget.StrengthUp : OpenVrMenuTarget.None; }
            else if (py is >= 220 and < 276 && px >= 170) { target = px < 232 ? OpenVrMenuTarget.GravityDown : px >= 294 ? OpenVrMenuTarget.GravityUp : OpenVrMenuTarget.None; }
            else if (py is >= 290 and < 346 && px >= 170) { target = px < 232 ? OpenVrMenuTarget.FrictionDown : px >= 294 ? OpenVrMenuTarget.FrictionUp : OpenVrMenuTarget.None; }
            else if (py is >= 384 and < 438)
            { target = px < 68 ? OpenVrMenuTarget.ResetModePrevious : px >= 292 ? OpenVrMenuTarget.ResetMode : OpenVrMenuTarget.None; }
            return true;
        }
        if (py is >= 16 and < 76) { target = OpenVrMenuTarget.PhoneToggle; }
        else if (py is >= 82 and < 138) { target = OpenVrMenuTarget.VideoSettings; }
        else if (py is >= 168 and <= 232 && px is >= 8 and <= 352)
        {
            target = OpenVrMenuTarget.Opacity;
            fraction = Math.Clamp((px - 24) / 312, 0, 1);
        }
        else if (py is >= 234 and < 294) { target = OpenVrMenuTarget.ScreenGuardToggle; }
        else if (py is >= 298 and < 358) { target = OpenVrMenuTarget.PlayspaceToggle; }
        else if (py is >= 366 and < 430) { target = OpenVrMenuTarget.FlingToggle; }
        else if (py is >= 440 and < 482 && px is >= 20 and < 340) { target = OpenVrMenuTarget.MotionSettings; }
        else if (keypad && py is >= 506 and < 562) { target = OpenVrMenuTarget.KeypadToggle; }
        return true; // Blank areas inside the panel still count as inside, not outside-dismiss.
    }

    public static bool TryMapKeypad(float x, float y, out OpenVrMenuTarget target)
    {
        target = OpenVrMenuTarget.None;
        if (!InsideDot(x, y)) { return false; }
        float px = x * Width;
        float py = y * KeypadHeight;
        if (py < 48) { target = OpenVrMenuTarget.KeypadToggle; }
        else if (py is >= 50 and < 298 && px is >= 20 and < 340)
        {
            int column = (int)((px - 20) / (320f / 3));
            int row = (int)((py - 50) / 62);
            int digit = row * 3 + column + 1;
            target = row == 3 ? column switch { 0 => OpenVrMenuTarget.Backspace, 1 => OpenVrMenuTarget.Digit0, _ => OpenVrMenuTarget.Confirm }
                : OpenVrMenuTarget.Digit0 + digit;
        }
        return true;
    }

    // The horizontal space between phone and open menu belongs to the gear.
    // It has no rendered surface, but keeps the ray and button target continuous.
    public static bool TryIntersectGearRegion(HmdMatrix34_t pointer, HmdMatrix34_t dot,
        bool expanded, float adjacentHeight, out VROverlayIntersectionResults_t hit)
    {
        const float margin = 0.003f;
        float right = expanded ? ControlGap : margin;
        float width = DotWidth + ControlGap + right;
        float height = expanded ? Math.Max(DotWidth, adjacentHeight) + 2 * margin : DotWidth + 2 * margin;
        HmdMatrix34_t offset = OpenVrTransformMath.Identity();
        offset.m3 = (right - ControlGap) / 2;
        offset.m7 = DotWidth / 2 + margin - height / 2;
        if (!TryIntersect(pointer, OpenVrTransformMath.Multiply(dot, offset), width, width / height, out hit)) { return false; }
        // Preserve the real dot's coordinate system for anchored group grabs.
        hit.vUVs.v0 = (hit.vUVs.v0 * width - ControlGap) / DotWidth;
        hit.vUVs.v1 = (hit.vUVs.v1 * height - (height - DotWidth - margin)) / DotWidth;
        return true;
    }

    public static bool TryIntersectOpacityDrag(HmdMatrix34_t pointer, HmdMatrix34_t panel,
        out VROverlayIntersectionResults_t hit, out float fraction)
    {
        const float dragWidth = 488;
        const float dragHeight = 208;
        float pixel = PanelWidth / Width;
        HmdMatrix34_t offset = OpenVrTransformMath.Identity();
        offset.m7 = (Height / 2f - 203) * pixel;
        fraction = 0;
        if (!TryIntersect(pointer, OpenVrTransformMath.Multiply(panel, offset), dragWidth * pixel,
                dragWidth / dragHeight, out hit)) { return false; }
        float x = hit.vUVs.v0 * dragWidth - 64;
        float y = 203 + dragHeight / 2 - hit.vUVs.v1 * dragHeight;
        fraction = Math.Clamp((x - 24) / 312, 0, 1);
        hit.vUVs.v0 = x / Width;
        hit.vUVs.v1 = 1 - y / Height;
        return true;
    }

    public static bool TryIntersect(HmdMatrix34_t pointer, HmdMatrix34_t panel, float width, float aspect,
        out VROverlayIntersectionResults_t hit)
    {
        hit = default;
        float facing = pointer.m2 * panel.m2 + pointer.m6 * panel.m6 + pointer.m10 * panel.m10;
        return facing > 0 && OpenVrPhoneRaycaster.TryIntersect(pointer, panel, width, aspect, out hit);
    }
}
