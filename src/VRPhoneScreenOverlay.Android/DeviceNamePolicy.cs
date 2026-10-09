using System.Globalization;
using System.Text;

namespace VRPhoneScreenOverlay.Android;

public static class DeviceNamePolicy
{
    public const int MaximumCharacters = 20;

    public static bool IsValid(string? name)
    {
        if (string.IsNullOrEmpty(name)) { return false; }
        int count = 0;
        foreach (Rune character in name.EnumerateRunes())
        {
            if (++count > MaximumCharacters || !IsAllowed(character)) { return false; }
        }
        return true;
    }

    private static bool IsAllowed(Rune character)
    {
        int value = character.Value;
        if (value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or 0x3007) { return true; }
        // Han ideographs, including supplementary name characters; exclude kana and other scripts.
        bool han = value is >= 0x3400 and <= 0x4dbf or >= 0x4e00 and <= 0x9fff or >= 0xf900 and <= 0xfaff or
            >= 0x20000 and <= 0x2ebef or >= 0x2f800 and <= 0x2fa1f or >= 0x30000 and <= 0x3ffff;
        return han && Rune.GetUnicodeCategory(character) == UnicodeCategory.OtherLetter;
    }
}
