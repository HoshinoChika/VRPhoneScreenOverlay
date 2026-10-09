namespace VRPhoneScreenOverlay.App;

internal static class UiLayoutMetrics
{
    public const int TitleBarHeight = 42;

    public static Size MainWindowClientSize { get; } = new(760, 502);

    public static Point PageLocation { get; } = new(18, 18);

    public static float DisplayScale(Size screen, Size workingArea, int dpi)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(screen.Width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(screen.Height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(workingArea.Width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(workingArea.Height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(dpi, 1);
        float resolutionScale = Math.Min(screen.Width / 1920f, screen.Height / 1080f);
        float desired = Math.Max(resolutionScale, dpi / 96f);
        float fits = Math.Min(workingArea.Width * 0.94f / MainWindowClientSize.Width,
            workingArea.Height * 0.94f / MainWindowClientSize.Height);
        return Math.Min(desired, fits);
    }

    public static Size ClientSizeFor(float scale) => new(
        (int)MathF.Round(MainWindowClientSize.Width * scale),
        (int)MathF.Round(MainWindowClientSize.Height * scale));

    public static void LockWindowSize(Form window, float scale)
    {
        window.MinimumSize = Size.Empty;
        window.MaximumSize = Size.Empty;
        window.ClientSize = ClientSizeFor(scale);
        window.MinimumSize = window.Size;
        window.MaximumSize = window.Size;
    }

    public static int Pixels(Control control, int logicalPixels)
    {
        for (Control? parent = control; parent is not null; parent = parent.Parent)
        {
            if (parent is Views.MainShellView shell)
            {
                return (int)MathF.Round(logicalPixels * shell.ContentScale);
            }
        }
        return logicalPixels;
    }
}
