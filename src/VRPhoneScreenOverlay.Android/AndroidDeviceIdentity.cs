namespace VRPhoneScreenOverlay.Android;

internal static class AndroidDeviceIdentity
{
    // Optional firmware fields, not a guessed mapping from regulatory IDs.
    public static string ReadMarketName(IReadOnlyDictionary<string, string> properties)
    {
        foreach (string key in new[] { "ro.product.marketname", "ro.product.vendor.marketname", "ro.product.odm.marketname", "ro.config.marketing_name" })
        {
            if (properties.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)) { return value.Trim(); }
        }
        return string.Empty;
    }

    public static string DisplayName(AdbProbeResult identity)
    {
        if (!string.IsNullOrWhiteSpace(identity.MarketName)) { return identity.MarketName; }
        string brand = string.IsNullOrWhiteSpace(identity.Manufacturer) ? identity.Brand : identity.Manufacturer;
        if (string.IsNullOrWhiteSpace(identity.Model)) { return "ID 未识别"; }
        if (LooksLikeIdentifier(identity.Model)) { return "ID " + identity.Model; }
        return string.IsNullOrWhiteSpace(brand) || identity.Model.StartsWith(brand, StringComparison.OrdinalIgnoreCase)
            ? identity.Model : brand + " " + identity.Model;
    }

    public static string UnprobedName(string model) => string.IsNullOrWhiteSpace(model) ? "ID 未识别"
        : LooksLikeIdentifier(model) ? "ID " + model : model;

    private static bool LooksLikeIdentifier(string model) => !model.Any(char.IsWhiteSpace) &&
        (model.Contains('-', StringComparison.Ordinal) || model.Count(char.IsDigit) >= model.Count(char.IsLetter));
}
