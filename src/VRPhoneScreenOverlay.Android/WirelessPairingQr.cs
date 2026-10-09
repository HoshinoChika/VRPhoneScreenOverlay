using System.Security.Cryptography;

namespace VRPhoneScreenOverlay.Android;

// Ephemeral credentials: never persisted, logged, or included in ToString().
public sealed class WirelessPairingQr
{
    public static TimeSpan Lifetime { get; } = TimeSpan.FromMinutes(5);
    public static TimeSpan RefreshInterval { get; } = Lifetime - TimeSpan.FromSeconds(20);
    public string ServiceName { get; } = "studio-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
    public string Password { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    public string Payload => $"WIFI:T:ADB;S:{ServiceName};P:{Password};;";
    public override string ToString() => nameof(WirelessPairingQr);
}
