using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class ControllerDiscoveryProcessTests
{
    private static string CommandShell => Environment.GetEnvironmentVariable("ComSpec") ?? Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Fact]
    public async Task InvalidAndOversizedProbeOutputBecomeStableFailures()
    {
        ControllerDiscoverySnapshot invalid = await OpenVrControllerDiscovery.ReadProbeAsync(CommandShell, "/d /c echo malformed", CancellationToken.None);
        Assert.Equal("CONTROLLER_PROBE_FAILED", invalid.ReasonCode);
        ControllerDiscoverySnapshot oversized = await OpenVrControllerDiscovery.ReadProbeAsync(CommandShell,
            "/d /c for /L %i in (1,1,1200) do @echo synthetic-output", CancellationToken.None);
        Assert.Equal("CONTROLLER_PROBE_INVALID", oversized.ReasonCode);
    }

    [Fact]
    public async Task AStalledProbeTimesOutAndCallerCancellationIsPreserved()
    {
        using CancellationTokenSource cancel = new();
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            OpenVrControllerDiscovery.ReadProbeAsync(CommandShell, "/d /c echo unused", cancel.Token));
        ControllerDiscoverySnapshot timeout = await OpenVrControllerDiscovery.ReadProbeAsync("powershell.exe",
            "-NoProfile -NonInteractive -Command Start-Sleep -Seconds 30", CancellationToken.None, TimeSpan.FromMilliseconds(150));
        Assert.Equal("CONTROLLER_PROBE_TIMEOUT", timeout.ReasonCode);
    }
}
