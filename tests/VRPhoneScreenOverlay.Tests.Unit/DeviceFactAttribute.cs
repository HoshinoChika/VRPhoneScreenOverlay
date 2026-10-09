namespace VRPhoneScreenOverlay.Tests.Unit;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class DeviceFactAttribute : FactAttribute
{
    public DeviceFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("VRPSO_DEVICE_TEST"),
                "1",
                StringComparison.Ordinal))
        {
            Skip = "Set VRPSO_DEVICE_TEST=1 and connect an authorized Android device.";
        }
    }
}
