using VRPhoneScreenOverlay.Network;

namespace VRPhoneScreenOverlay.Tests.Unit;

internal static class TestServiceConfiguration
{
    public static ClientServiceConfiguration Value { get; } = new()
    {
        UpdateManifestUri = new("https://service.invalid/updates/manifest?channel=beta"),
        DiagnosticsInitUri = new("https://service.invalid/diagnostics/init"),
        UsageHeartbeatUri = new("https://service.invalid/usage/heartbeat"),
    };
}
