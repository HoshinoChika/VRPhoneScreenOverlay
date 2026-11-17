using System.Text.Json;

namespace VRPhoneScreenOverlay.Diagnostics;

public static class StartupFailureReport
{
    public static async Task<bool> RecordAsync(Exception error, string? directory = null)
    {
        // One small record per failed launch; no raw exception message, stack, paths or content.
        string json = JsonSerializer.Serialize(new StartupFailureRecord("APP_STARTUP_FAILED", DateTimeOffset.UtcNow,
            error.GetType().FullName, error.HResult), StartupFailureJsonContext.Default.StartupFailureRecord);
        using CancellationTokenSource deadline = new(TimeSpan.FromMilliseconds(250));
        Task write = Task.Run(async () =>
        {
            string root = directory ?? DiagnosticLocalFiles.LogsDirectory;
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "startup-failure.json"), json, deadline.Token).ConfigureAwait(false);
        });
        try { await write.WaitAsync(deadline.Token).ConfigureAwait(false); return true; }
        catch (Exception)
        {
            _ = write.ContinueWith(done => { _ = done.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return false;
        }
    }
}

internal sealed record StartupFailureRecord(string Reason, DateTimeOffset Timestamp, string? ExceptionType, int HResult);

[System.Text.Json.Serialization.JsonSerializable(typeof(StartupFailureRecord))]
internal sealed partial class StartupFailureJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
