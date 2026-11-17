using System.Globalization;

namespace VRPhoneScreenOverlay.App;

internal static class TextInputGuard
{
    // Reject the whole invalid edit, including paste/IME, rather than silently changing its meaning.
    internal static void Attach(TextBox editor, Func<string, bool> accepts)
    {
        string accepted = editor.Text;
        bool restoring = false;
        editor.KeyPress += (_, args) =>
        {
            if (char.IsControl(args.KeyChar)) { return; }
            string next = editor.Text.Remove(editor.SelectionStart, editor.SelectionLength)
                .Insert(editor.SelectionStart, args.KeyChar.ToString());
            args.Handled = !accepts(next);
        };
        editor.TextChanged += (_, _) =>
        {
            if (restoring) { return; }
            if (accepts(editor.Text)) { accepted = editor.Text; return; }
            int caret = Math.Min(editor.SelectionStart, accepted.Length);
            restoring = true;
            try { editor.Text = accepted; editor.SelectionStart = caret; }
            finally { restoring = false; }
        };
    }

    internal static bool IsNumericDraft(string text, int decimalPlaces, bool allowNegative)
    {
        if (text.Length > 32) { return false; }
        string value = text;
        string sign = CultureInfo.CurrentCulture.NumberFormat.NegativeSign;
        if (allowNegative && value.StartsWith(sign, StringComparison.Ordinal)) { value = value[sign.Length..]; }
        string separator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        int index = value.IndexOf(separator, StringComparison.Ordinal);
        if (index >= 0)
        {
            if (decimalPlaces == 0 || value.Length - index - separator.Length > decimalPlaces) { return false; }
            value = value.Remove(index, separator.Length);
        }
        return value.All(char.IsAsciiDigit);
    }
}
