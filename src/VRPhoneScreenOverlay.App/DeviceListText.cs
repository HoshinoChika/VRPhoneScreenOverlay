using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.App;

internal static class DeviceListText
{
    public static string FormatManaged(ManagedAndroidDevice device) =>
        $"{(device.IsAvailable ? "可识别" : "历史记录")} · {AndroidDisplayNames.Transport(device.Transport)} · {device.DisplayName}" +
        (device.IsSelected ? " · 当前连接" : "") + (device.AutoConnect ? " · 自动连接" : "") +
        (device.IsAvailable && device.Status != AndroidDeviceStatus.Ready ? $" · {AndroidDisplayNames.DeviceStatus(device.Status)}" : "");

    public static string Format(AndroidDeviceView device) =>
        $"{Clean(device.DisplayName, "未知型号")}　|　{AndroidDisplayNames.Transport(device.Transport)}{(device.IsSelected ? " · 当前" : "")}　|　ID {Clean(device.Model, "未知")}　|　" +
        $"Android {Clean(device.AndroidVersion, "未知")}　|　{AndroidDisplayNames.DeviceStatus(device.Status)}";

    private static string Clean(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback
        : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
