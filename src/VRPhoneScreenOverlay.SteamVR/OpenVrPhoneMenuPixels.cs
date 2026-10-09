using System.Globalization;
using VRPhoneScreenOverlay.Presentation;

namespace VRPhoneScreenOverlay.SteamVR;

internal readonly record struct OpenVrMenuVisual(bool Hidden, int Opacity, bool DragEnabled, float Multiplier, bool Keypad, int DigitCount,
    bool KeypadExpanded = false, bool ScreenGuardEnabled = false, bool ScreenGuardFaulted = false, bool MotionExpanded = false, bool FlingEnabled = false,
    OpenVrPlayspaceMotionOptions? MotionOptions = null, OpenVrMotionSaveState MotionSaveState = OpenVrMotionSaveState.None, bool VideoExpanded = false,
    OpenVrVideoOptions? VideoOptions = null, OpenVrVideoApplyState VideoState = OpenVrVideoApplyState.Idle);

internal static class OpenVrPhoneMenuPixels
{
    public static byte[] Dot()
    {
        byte[] pixels = new byte[64 * 64 * 4];
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                float distance = MathF.Sqrt((x - 31.5f) * (x - 31.5f) + (y - 31.5f) * (y - 31.5f));
                int offset = (y * 64 + x) * 4;
                float qx = MathF.Abs(x - 31.5f) - 20;
                float qy = MathF.Abs(y - 31.5f) - 20;
                float edge = MathF.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0)) +
                    Math.Min(Math.Max(qx, qy), 0) - 12;
                float rim = Math.Clamp(1 + edge, 0, 1);
                pixels[offset] = (byte)(22 + 50 * rim);
                pixels[offset + 1] = (byte)(26 + 55 * rim);
                pixels[offset + 2] = (byte)(33 + 60 * rim);
                pixels[offset + 3] = (byte)(Math.Clamp(0.5f - edge, 0, 1) * 255);
                // Larger white gear on the shell-colored rounded square.
                float angle = MathF.Atan2(y - 31.5f, x - 31.5f);
                float outerRadius = 19 + 5 * Math.Clamp((MathF.Cos(angle * 8) - 0.2f) * 2, 0, 1);
                float gear = Math.Clamp(outerRadius - distance, 0, 1) * Math.Clamp(distance - 9f, 0, 1);
                pixels[offset] = (byte)(pixels[offset] * (1 - gear) + 245 * gear);
                pixels[offset + 1] = (byte)(pixels[offset + 1] * (1 - gear) + 247 * gear);
                pixels[offset + 2] = (byte)(pixels[offset + 2] * (1 - gear) + 250 * gear);
            }
        }
        return pixels;
    }

    public static byte[] Panel(OpenVrMenuVisual value)
    {
        byte[] pixels = new byte[OpenVrPhoneMenuLayout.Width * OpenVrPhoneMenuLayout.Height * 4];
        PaintPanel(pixels, value);
        return pixels;
    }

    public static void PaintPanel(byte[] pixels, OpenVrMenuVisual value)
    {
        Array.Clear(pixels);
        if (value.VideoExpanded) { PaintVideo(pixels, value); return; }
        if (value.MotionExpanded) { PaintMotion(pixels, value); return; }
        OpenVrUiCanvas.Card(pixels, 0, 0, 360, value.Keypad ? 564 : 500, UiTheme.Surface, 14);
        OpenVrUiCanvas.Card(pixels, 12, 16, 336, 60, UiTheme.SurfaceRaised);
        OpenVrMenuIcons.Draw(pixels, OpenVrMenuIcon.CloseCircle, 24, 30, 32);
        OpenVrPhoneMenuLabels.Draw(pixels, 0, 64, 30);
        OpenVrUiCanvas.Card(pixels, 12, 82, 336, 56, UiTheme.SurfaceRaised);
        OpenVrMenuIcons.Draw(pixels, OpenVrMenuIcon.Video, 24, 94, 32);
        OpenVrPhoneMenuLabels.Draw(pixels, 23, 64, 94);
        OpenVrMenuIcons.Draw(pixels, OpenVrMenuIcon.ChevronRight, 310, 96, 28);
        OpenVrPhoneMenuLabels.Draw(pixels, 1, 20, 147);
        Text(pixels, value.Opacity.ToString(CultureInfo.InvariantCulture) + "%", 266, 147, 3);
        OpenVrUiCanvas.Rounded(pixels, 24, 200, 312, 6, 3, UiTheme.Border);
        int length = Math.Clamp(value.Opacity, 0, 100) * 312 / 100;
        if (length > 0) { OpenVrUiCanvas.Rounded(pixels, 24, 200, length, 6, 3, UiTheme.Accent); }
        OpenVrUiCanvas.Rounded(pixels, 16 + length, 195, 16, 16, 8, UiTheme.TextPrimary);
        OpenVrPhoneMenuLabels.Draw(pixels, 9, 20, 248);
        Toggle(pixels, 248, value.ScreenGuardEnabled);
        if (value.ScreenGuardFaulted) { OpenVrUiCanvas.Rounded(pixels, 222, 260, 10, 10, 5, UiTheme.Danger); }
        // The two movement switches form one group; parameters live on the settings page.
        OpenVrUiCanvas.Card(pixels, 12, 298, 336, 192, UiTheme.SurfaceRaised);
        OpenVrPhoneMenuLabels.Draw(pixels, 2, 20, 312);
        Toggle(pixels, 312, value.DragEnabled, grouped: true);
        OpenVrPhoneMenuLabels.Draw(pixels, 12, 20, 376);
        Toggle(pixels, 376, value.FlingEnabled, grouped: true);
        OpenVrUiCanvas.Card(pixels, 20, 440, 320, 42, UiTheme.Surface);
        OpenVrMenuIcons.Draw(pixels, OpenVrMenuIcon.Settings, 28, 447, 28);
        OpenVrPhoneMenuLabels.Draw(pixels, 10, 64, 445);
        OpenVrMenuIcons.Draw(pixels, OpenVrMenuIcon.ChevronRight, 302, 447, 28);
        if (value.Keypad)
        {
            OpenVrUiCanvas.Card(pixels, 12, 506, 336, 48, value.KeypadExpanded ? UiTheme.NavigationSelected : UiTheme.SurfaceRaised);
            OpenVrPhoneMenuLabels.Draw(pixels, 4, 20, 514);
            OpenVrMenuIcons.Draw(pixels, OpenVrMenuIcon.ChevronRight, 310, 516, 28);
        }
    }

    private static void PaintVideo(byte[] pixels, OpenVrMenuVisual value)
    {
        OpenVrVideoOptions options = value.VideoOptions ?? new(100, 16, 60);
        OpenVrUiCanvas.Card(pixels, 0, 0, 360, OpenVrPhoneMenuLayout.MotionHeight, UiTheme.Surface, 14);
        OpenVrUiCanvas.Card(pixels, 12, 12, 336, 56, UiTheme.SurfaceRaised);
        OpenVrMenuIcons.Draw(pixels, OpenVrMenuIcon.Back, 24, 26, 28);
        OpenVrPhoneMenuLabels.Draw(pixels, 11, 64, 24);
        MotionNumber(pixels, 24, 108, options.Resolution, percentage: true);
        MotionNumber(pixels, 25, 188, options.Bitrate);
        MotionNumber(pixels, 26, 268, options.FrameRate);
        OpenVrUiCanvas.Card(pixels, 12, 360, 336, 56, UiTheme.AccentDark);
        OpenVrPhoneMenuLabels.DrawCentered(pixels, value.VideoState == OpenVrVideoApplyState.Applying ? 28 : 27, 180, 388);
        if (value.VideoState == OpenVrVideoApplyState.Failed) { OpenVrPhoneMenuLabels.Draw(pixels, 29, 20, 440); }
        if (value.VideoState == OpenVrVideoApplyState.Applied) { OpenVrPhoneMenuLabels.Draw(pixels, 30, 20, 440); }
    }

    private static void PaintMotion(byte[] pixels, OpenVrMenuVisual value)
    {
        OpenVrPlayspaceMotionOptions options = value.MotionOptions ?? OpenVrPlayspaceMotionOptions.Default;
        OpenVrUiCanvas.Card(pixels, 0, 0, 360, OpenVrPhoneMenuLayout.MotionHeight, UiTheme.Surface, 14);
        OpenVrUiCanvas.Card(pixels, 12, 12, 336, 56, UiTheme.SurfaceRaised);
        OpenVrMenuIcons.Draw(pixels, OpenVrMenuIcon.Back, 24, 26, 28);
        OpenVrPhoneMenuLabels.Draw(pixels, 11, 64, 24);
        MotionNumber(pixels, 3, 88, value.Multiplier, multiplier: true);
        MotionNumber(pixels, 13, 158, options.FlingStrength);
        MotionNumber(pixels, 14, 228, options.Gravity);
        MotionNumber(pixels, 15, 298, options.Friction);
        OpenVrPhoneMenuLabels.Draw(pixels, 22, 20, 350);
        OpenVrUiCanvas.Card(pixels, 12, 384, 336, 54, UiTheme.SurfaceRaised);
        OpenVrPhoneMenuLabels.DrawCentered(pixels, options.ResetAllOffsets ? 17 : 16, 180, 411);
        OpenVrMenuIcons.Draw(pixels, OpenVrMenuIcon.ChevronLeft, 22, 398, 28);
        OpenVrMenuIcons.Draw(pixels, OpenVrMenuIcon.ChevronRight, 310, 398, 28);
        int saveLabel = value.MotionSaveState switch
        {
            OpenVrMotionSaveState.Failed => 21,
            _ => -1,
        };
        if (saveLabel >= 0) { OpenVrPhoneMenuLabels.Draw(pixels, saveLabel, 20, 456); }
    }

    private static void MotionNumber(byte[] pixels, int label, int top, float value, bool multiplier = false, bool percentage = false)
    {
        OpenVrPhoneMenuLabels.Draw(pixels, label, 20, top);
        OpenVrUiCanvas.Card(pixels, 170, top - 8, 48, 52, UiTheme.SurfaceRaised);
        OpenVrUiCanvas.Card(pixels, 298, top - 8, 48, 52, UiTheme.SurfaceRaised);
        OpenVrMenuIcons.Draw(pixels, OpenVrMenuIcon.Minus, 180, top + 4, 28);
        string number = multiplier ? value.ToString("0", CultureInfo.InvariantCulture) + "×" : value.ToString("0.#", CultureInfo.InvariantCulture);
        if (percentage) { number += "%"; }
        float size = number.Length > 4 ? 0.8f : 1f;
        int width = (int)(18 * size) * (number.Length - 1) + (int)(24 * size);
        OpenVrUiCanvas.Text(pixels, number, 258 - width / 2, top + 18 - (int)(16 * size), size);
        OpenVrMenuIcons.Draw(pixels, OpenVrMenuIcon.Plus, 308, top + 4, 28);
    }

    public static void PaintKeypad(byte[] pixels, int digitCount)
    {
        Array.Clear(pixels);
        OpenVrUiCanvas.Card(pixels, 0, 0, 360, OpenVrPhoneMenuLayout.KeypadHeight, UiTheme.Surface, 14);
        OpenVrPhoneMenuLabels.Draw(pixels, 4, 20, 8);
        OpenVrMenuIcons.Draw(pixels, OpenVrMenuIcon.CloseCircle, 314, 6, 36);
        for (int digit = 0; digit < digitCount; digit++) { OpenVrUiCanvas.Rounded(pixels, 170 + digit * 8, 21, 5, 5, 2.5f, UiTheme.Accent); }
        for (int row = 0; row < 4; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                int left = 20 + col * 320 / 3;
                int top = 50 + row * 62;
                OpenVrUiCanvas.Card(pixels, left + 2, top + 2, 102, 58,
                    row == 3 && col == 2 ? UiTheme.AccentDark : UiTheme.SurfaceRaised);
                string label = row == 3 ? col switch { 0 => "DEL", 1 => "0", _ => "OK" }
                    : (row * 3 + col + 1).ToString(CultureInfo.InvariantCulture);
                if (row == 3 && col != 1)
                {
                    OpenVrMenuIcons.Draw(pixels, col == 0 ? OpenVrMenuIcon.Backspace : OpenVrMenuIcon.Check, left + 37, top + 15, 32);
                }
                else { Text(pixels, label, left + 39, top + 9, 4); }
            }
        }
    }

    private static void Toggle(byte[] pixels, int top, bool enabled, bool grouped = false)
    {
        OpenVrUiCanvas.Rounded(pixels, 256, top, 84, 36, 18, enabled ? UiTheme.SwitchOn : grouped ? UiTheme.Surface : UiTheme.SurfaceRaised);
        OpenVrPhoneMenuLabels.Draw(pixels, enabled ? 5 : 6, enabled ? 268 : 290, top + 2);
        OpenVrUiCanvas.Rounded(pixels, enabled ? 308 : 260, top + 4, 28, 28, 14, UiTheme.TextPrimary);
    }

    private static void Text(byte[] pixels, string text, int x, int y, int scale) =>
        OpenVrUiCanvas.Text(pixels, text, x, y, scale / 3f);
}
