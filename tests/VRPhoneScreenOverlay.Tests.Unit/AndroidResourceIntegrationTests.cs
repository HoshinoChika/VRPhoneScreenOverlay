using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class AndroidResourceIntegrationTests
{
    [Fact]
    public async Task BundledToolsPassHashesAndExecutableStarts()
    {
        string resourceDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "resources",
            "android-platform-tools");

        AndroidResourceValidationResult validation = await AndroidResourceValidator.ValidateAsync(
            resourceDirectory,
            CancellationToken.None);
        AdbCommandRunner runner = new(validation.ExecutablePath);
        AdbCommandResult version = await runner.RunAsync(
            ["version"],
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.True(validation.Succeeded, validation.Message);
        Assert.True(version.Succeeded, version.StandardError);
        Assert.Contains("Android Debug Bridge", version.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("37.0.0-14910828", version.StandardOutput, StringComparison.Ordinal);
    }
}
