using System.Diagnostics;
using System.Text.Json;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class SteamVrStartupIsolationTests
{
    [Fact]
    public async Task CompletedUtilityReturnsItsStructuredResult()
    {
        string json = JsonSerializer.Serialize(new OpenVrBindingResult(true, OpenVrReasonCodes.StartupConfigured, "configured"));
        ProcessStartInfo start = Command("[Console]::WriteLine('" + json + "'); exit 0");
        OpenVrBindingResult result = await OpenVrStartupRegistration.RunUtilityAsync(start, TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.True(result.Succeeded);
        Assert.Equal(OpenVrReasonCodes.StartupConfigured, result.ReasonCode);
    }

    [Fact]
    public async Task HungUtilityTimesOutAndIsTerminated()
    {
        string pidFile = Path.Combine(AppContext.BaseDirectory, "startup-child-" + Guid.NewGuid().ToString("N") + ".txt");
        ProcessStartInfo start = Command("[IO.File]::WriteAllText('" + pidFile.Replace("'", "''", StringComparison.Ordinal) + "',[string]$PID); Start-Sleep -Seconds 30");
        Stopwatch timer = Stopwatch.StartNew();
        OpenVrBindingResult result = await OpenVrStartupRegistration.RunUtilityAsync(start, TimeSpan.FromSeconds(1.5), CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal(OpenVrReasonCodes.StartupRegistrationTimeout, result.ReasonCode);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(4));
        Assert.True(File.Exists(pidFile), "The child must actually start before the timeout reproduction.");
        int child = int.Parse(await File.ReadAllTextAsync(pidFile), System.Globalization.CultureInfo.InvariantCulture);
        Assert.Throws<ArgumentException>(() => { using Process process = Process.GetProcessById(child); });
        File.Delete(pidFile);
    }

    [Fact]
    public async Task InvalidUtilityResponseDoesNotEscapeIntoStartup()
    {
        OpenVrBindingResult result = await OpenVrStartupRegistration.RunUtilityAsync(Command("[Console]::WriteLine('invalid'); exit 1"),
            TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal(OpenVrReasonCodes.StartupRegistrationFailed, result.ReasonCode);
    }

    private static ProcessStartInfo Command(string command)
    {
        ProcessStartInfo start = new()
        {
            FileName = "pwsh.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(command);
        return start;
    }
}
