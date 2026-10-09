namespace VRPhoneScreenOverlay.Protocols;

public static class UpdateDownloadAddressPolicy
{
    public static bool IsAllowed(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == "https" && uri.IsDefaultPort &&
        uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 &&
        (uri.Host.EndsWith(".115.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".115cdn.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".115cdn.net", StringComparison.OrdinalIgnoreCase));
}
