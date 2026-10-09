using System.Globalization;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Android;

internal readonly record struct AndroidDisplaySize(int Width, int Height)
{
    public static bool TryParseWmSize(string output, out AndroidDisplaySize size)
    {
        size = default;
        AndroidDisplaySize physical = default;
        AndroidDisplaySize overridden = default;
        foreach (string line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = line.IndexOf(':');
            if (separator < 0 || !TryParseDimensions(line[(separator + 1)..], out AndroidDisplaySize found))
            {
                continue;
            }

            if (line.Contains("Override", StringComparison.OrdinalIgnoreCase))
            {
                overridden = found;
            }
            else if (line.Contains("Physical", StringComparison.OrdinalIgnoreCase))
            {
                physical = found;
            }
        }

        size = overridden.Width > 0 ? overridden : physical;
        return size.Width > 0 && size.Height > 0;
    }

    public PhoneInputCommand Map(PhoneInputCommand command)
    {
        if (command.ScreenWidth < 1 || command.ScreenHeight < 1)
        {
            return command;
        }

        bool videoIsLandscape = command.ScreenWidth > command.ScreenHeight;
        int shortEdge = Math.Min(Width, Height);
        int longEdge = Math.Max(Width, Height);
        return command with
        {
            ScreenWidth = videoIsLandscape ? longEdge : shortEdge,
            ScreenHeight = videoIsLandscape ? shortEdge : longEdge,
        };
    }

    private static bool TryParseDimensions(string text, out AndroidDisplaySize size)
    {
        size = default;
        string value = text.Trim();
        int separator = value.IndexOf('x');
        if (separator < 1 ||
            !int.TryParse(value[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out int width) ||
            !int.TryParse(value[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int height) ||
            width is < 1 or > ushort.MaxValue ||
            height is < 1 or > ushort.MaxValue)
        {
            return false;
        }

        size = new AndroidDisplaySize(width, height);
        return true;
    }
}
