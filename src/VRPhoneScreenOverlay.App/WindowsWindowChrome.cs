using System.Runtime.InteropServices;

namespace VRPhoneScreenOverlay.App;

/// <summary>
/// The sole approved native UI interop boundary. It configures Windows 11 DWM
/// window appearance once when the main window handle is created.
/// </summary>
internal static class WindowsWindowChrome
{
    private const int _immersiveDarkModeAttribute = 20;
    private const int _cornerPreferenceAttribute = 33;
    private const int _borderColorAttribute = 34;
    private const int _roundedCornerPreference = 2;
    private const int _noSystemBorderColor = unchecked((int)0xFFFFFFFE);

    public static bool TryApply(nint windowHandle)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ||
            windowHandle == nint.Zero)
        {
            return false;
        }

        int darkMode = 1;
        _ = DwmSetWindowAttribute(
            windowHandle,
            _immersiveDarkModeAttribute,
            ref darkMode,
            sizeof(int));
        int borderColor = _noSystemBorderColor;
        _ = DwmSetWindowAttribute(
            windowHandle,
            _borderColorAttribute,
            ref borderColor,
            sizeof(int));
        int cornerPreference = _roundedCornerPreference;
        int result = DwmSetWindowAttribute(
            windowHandle,
            _cornerPreferenceAttribute,
            ref cornerPreference,
            sizeof(int));
        return result >= 0;
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(
        nint windowHandle,
        int attribute,
        ref int value,
        int valueSize);
}
