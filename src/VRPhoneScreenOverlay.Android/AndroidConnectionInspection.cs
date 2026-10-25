namespace VRPhoneScreenOverlay.Android;

public sealed record AndroidConnectionInspection(string Method, int SavedDevices, int AutoConnectDevices,
    string SavedEndpointReachability, IReadOnlyList<AndroidConnectionInspectionStage> Stages);

public sealed record AndroidConnectionInspectionStage(string Stage, bool Succeeded, bool TimedOut,
    string? TimeoutPhase, string ErrorCategory, int ExitCode, long DurationMilliseconds, int? Items)
{
    public int? ReadyDevices { get; init; }
    public int? UnauthorizedDevices { get; init; }
    public int? OfflineDevices { get; init; }
}

public interface IAndroidConnectionInspectionService
{
    public Task<AndroidConnectionInspection> InspectConnectionAsync(CancellationToken token);
}

/// <summary>Runs a fixed read-only command set. No caller/model command text or raw device output escapes this layer.</summary>
internal static class AndroidConnectionInspector
{
    internal static async Task<AndroidConnectionInspectionStage[]> InspectAsync(IAdbCommandRunner runner, bool wireless, CancellationToken token)
    {
        List<AndroidConnectionInspectionStage> results = new(3);
        await Run("tool", ["version"]);
        await Run("devices", ["devices", "-l"]);
        if (wireless) { await Run("wireless_discovery", ["mdns", "services"]); }
        return results.ToArray();

        async Task Run(string stage, string[] arguments)
        {
            AdbCommandResult result = await runner.RunAsync(arguments, TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
            IReadOnlyList<AdbDeviceRecord>? devices = result.Succeeded && stage == "devices" ? AdbOutputParser.ParseDevices(result.StandardOutput) : null;
            int? items = !result.Succeeded ? null : stage == "devices" ? devices!.Count
                : stage == "wireless_discovery" ? result.StandardOutput.Split('\n').Count(line => line.Contains("_adb-tls-connect._tcp", StringComparison.Ordinal)) : null;
            results.Add(new(stage, result.Succeeded, result.TimedOut, result.Diagnostic?.TimeoutPhase,
                result.Diagnostic?.ErrorCategory ?? "unknown", result.ExitCode, (long)result.Duration.TotalMilliseconds, items)
            {
                ReadyDevices = devices?.Count(d => d.Status == AndroidDeviceStatus.Ready),
                UnauthorizedDevices = devices?.Count(d => d.Status == AndroidDeviceStatus.Unauthorized),
                OfflineDevices = devices?.Count(d => d.Status == AndroidDeviceStatus.Offline),
            });
        }
    }
}
