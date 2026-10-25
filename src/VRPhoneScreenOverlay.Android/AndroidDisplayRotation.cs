using System.Globalization;

namespace VRPhoneScreenOverlay.Android;

internal static class AndroidDisplayRotation
{
    // AOSP DisplayManagerService dumps mViewports with DisplayViewport.toString().
    // Only valid internal viewport(s) of logical display 0 are accepted; ambiguity
    // and vendor formats remain explicitly unknown. Raw dumps never enter logs.
    internal static (int? Rotation, string Status) Parse(string output)
    {
        if (output.Length > 262144) { return (null, "output-limit"); }
        int? rotation = null;
        int offset = 0;
        while ((offset = output.IndexOf("DisplayViewport{", offset, StringComparison.Ordinal)) >= 0)
        {
            int end = output.IndexOf('}', offset);
            if (end < 0 || end - offset > 4096) { return (null, "malformed-viewport"); }
            string block = output[(offset + 16)..end];
            offset = end + 1;
            string? type = null, valid = null, display = null, orientation = null;
            foreach (string field in block.Split(','))
            {
                int separator = field.IndexOf('=');
                if (separator < 0) { continue; }
                string key = field[..separator].Trim(), value = field[(separator + 1)..].Trim();
                switch (key)
                {
                    case "type": type = value; break;
                    case "valid": valid = value; break;
                    case "displayId": display = value; break;
                    case "orientation": orientation = value; break;
                }
            }
            if (type != "INTERNAL" || valid != "true" || display != "0") { continue; }
            if (!int.TryParse(orientation, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) || parsed is < 0 or > 3)
            { return (null, "invalid-rotation"); }
            if (rotation is not null && rotation != parsed) { return (null, "ambiguous-rotation"); }
            rotation = parsed;
        }
        return (rotation, rotation is null ? "unrecognized-viewport" : "observed");
    }
}
