using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class ScrcpySessionLauncherTests
{
    [Fact]
    public void ServerArgumentsKeepSharedJarForChannelRestarts()
    {
        IReadOnlyList<string> arguments = ScrcpySessionLauncher
            .EnsurePersistentServerArguments(["app_process", "control=false"]);

        Assert.Equal(
            ["app_process", "control=false", "cleanup=false"],
            arguments);
    }

    [Fact]
    public void ExistingCleanupArgumentIsNotDuplicated()
    {
        string[] original = ["app_process", "cleanup=false"];

        IReadOnlyList<string> arguments = ScrcpySessionLauncher
            .EnsurePersistentServerArguments(original);

        Assert.Same(original, arguments);
    }

    [Fact]
    public void ServerDiagnosticsRedactCompleteDeviceSerialAndLimitLength()
    {
        string output = $"device=private-serial {new string('x', 900)}";

        string sanitized = ScrcpySessionLauncher.SanitizeServerOutput(
            output,
            "private-serial");

        Assert.DoesNotContain("private-serial", sanitized, StringComparison.Ordinal);
        Assert.True(sanitized.Length <= 800);
    }
}
