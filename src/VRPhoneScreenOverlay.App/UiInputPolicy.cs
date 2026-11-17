using System.Globalization;
using System.Text;

namespace VRPhoneScreenOverlay.App;

internal static class UiInputPolicy
{
    public const string DateTimeFormat = "yyyy-MM-dd HH:mm";
    public const int MaximumDescriptionLength = 300;

    public static bool TryParseDateTime(
        string text,
        DateTime minimum,
        DateTime maximum,
        out DateTime value)
    {
        if (DateTime.TryParseExact(
                text,
                DateTimeFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out DateTime parsed) &&
            parsed >= minimum &&
            parsed <= maximum)
        {
            value = parsed;
            return true;
        }

        value = default;
        return false;
    }

    public static bool TryNormalizeDescription(string? text, out string? normalized)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            normalized = null;
            return true;
        }

        StringBuilder builder = new(Math.Min(text.Length, MaximumDescriptionLength));
        foreach (char character in text)
        {
            if (char.IsControl(character) && character is not '\r' and not '\n' and not '\t')
            {
                continue;
            }

            builder.Append(character);
            if (builder.Length > MaximumDescriptionLength)
            {
                normalized = null;
                return false;
            }
        }

        string value = builder.ToString().Trim();
        normalized = value.Length == 0 ? null : value;
        return true;
    }
}
