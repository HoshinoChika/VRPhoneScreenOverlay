namespace VRPhoneScreenOverlay.Contracts;

public static class DiagnosticRetentionPolicy
{
    public static TimeSpan LocalWindow => TimeSpan.FromHours(24);
    public static TimeSpan UploadWindow => TimeSpan.FromHours(6);
}
