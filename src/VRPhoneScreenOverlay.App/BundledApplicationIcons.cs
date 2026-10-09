using System.Reflection;

namespace VRPhoneScreenOverlay.App;

internal enum BundledApplicationIcon
{
    Steam,
    SteamVr,
}

internal static class BundledApplicationIcons
{
    public static Bitmap Load(BundledApplicationIcon icon)
    {
        string resourceName = icon switch
        {
            BundledApplicationIcon.Steam => "VRPhoneScreenOverlay.App.Assets.steam.png",
            BundledApplicationIcon.SteamVr => "VRPhoneScreenOverlay.App.Assets.steamvr.png",
            _ => throw new ArgumentOutOfRangeException(nameof(icon)),
        };
        Assembly assembly = typeof(BundledApplicationIcons).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(resourceName) ??
            throw new InvalidOperationException($"Missing bundled icon resource: {resourceName}");
        using Image source = Image.FromStream(stream);
        return new Bitmap(source);
    }
}
