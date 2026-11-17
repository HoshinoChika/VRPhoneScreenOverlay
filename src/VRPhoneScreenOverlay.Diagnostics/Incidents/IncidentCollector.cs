using System.Diagnostics;
using System.Text.Json.Serialization.Metadata;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Diagnostics;

public static class IncidentCollector
{
    public static IncidentCollectionReport Collect(string? root = null, DateTimeOffset? windowStart = null, DateTimeOffset? windowEnd = null)
    {
        root ??= IncidentStorage.DefaultRoot;
        DateTimeOffset until = windowEnd ?? DateTimeOffset.UtcNow;
        DateTimeOffset cutoff = windowStart ?? until - DiagnosticRetentionPolicy.UploadWindow;
        List<IncidentReport> reports = [];
        int available = 0;
        try
        {
            IncidentStorage.ValidateDirectory(root);
            if (!Directory.Exists(root)) { return new(1, "not-collected", 0, IncidentStorage.MaximumBytes, []); }
            DirectoryInfo[] candidates = new DirectoryInfo(root).EnumerateDirectories()
                .Where(d => Guid.TryParseExact(d.Name, "N", out _))
                .Where(d => (d.Attributes & FileAttributes.ReparsePoint) == 0)
                .Where(d => d.EnumerateFiles().Any(f => f.Extension is ".json" or ".jsonl" && f.LastWriteTimeUtc >= cutoff.UtcDateTime)).ToArray();
            available = candidates.Length;
            foreach (DirectoryInfo directory in candidates.OrderByDescending(d => d.CreationTimeUtc))
            {
                IncidentState? state = null;
                IncidentFailure? failure = null;
                IncidentExit? exit = null;
                IncidentCrashEvidence? evidence = null;
                IncidentFailureHistory? failures = null;
                DiagnosticRuntimeHealth? health = null;
                IncidentObserverHealth? observer = null;
                string observerStatus = "not-collected";
                FlightRecord? flight = null;
                FlightRecord? failureFlight = null;
                string status = "collected";
                bool failed = false;
                try
                {
                    IncidentStorage.ValidateDirectory(directory.FullName);
                    state = ReadPart(directory.FullName, "state.json", IncidentJsonContext.Default.IncidentState, ref failed);
                    if (state is not null && (state.Checkpoints is null || state.SessionId != directory.Name))
                    { state = null; failed = true; }
                    failure = ReadPart(directory.FullName, "failure.json", IncidentJsonContext.Default.IncidentFailure, ref failed);
                    exit = ReadPart(directory.FullName, "exit.json", IncidentJsonContext.Default.IncidentExit, ref failed);
                    evidence = ReadPart(directory.FullName, "crash-events.json", IncidentJsonContext.Default.IncidentCrashEvidence, ref failed);
                    failures = ReadPart(directory.FullName, "failures.json", IncidentJsonContext.Default.IncidentFailureHistory, ref failed);
                    if (failures is not null && failures.Failures is null) { failures = null; failed = true; }
                    health = ReadPart(directory.FullName, "health.json", IncidentJsonContext.Default.DiagnosticRuntimeHealth, ref failed);
                    observer = ReadPart(directory.FullName, "observer.json", IncidentJsonContext.Default.IncidentObserverHealth, ref failed);
                    flight = ReadPart(directory.FullName, "flight-record.json", FlightJsonContext.Default.FlightRecord, ref failed);
                    failureFlight = ReadPart(directory.FullName, "flight-failure.json", FlightJsonContext.Default.FlightRecord, ref failed);
                    if (flight is not null && flight.Samples is null) { flight = null; failed = true; }
                    if (failureFlight is not null && failureFlight.Samples is null) { failureFlight = null; failed = true; }
                    if (flight is not null) { flight = flight with { Samples = flight.Samples.Where(s => s.At >= cutoff && s.At <= until).ToArray() }; }
                    if (failureFlight is not null) { failureFlight = failureFlight with { Samples = failureFlight.Samples.Where(s => s.At >= cutoff && s.At <= until).ToArray() }; }
                    if (observer is not null)
                    {
                        observerStatus = exit is not null ? "exit-recorded" : ProcessStatus(observer.ProcessId, observer.ProcessStartTicks);
                        if (observerStatus == "alive" && DateTimeOffset.UtcNow - observer.SampledAt > TimeSpan.FromSeconds(8))
                        { observerStatus = "heartbeat-stale"; }
                    }
                    if (state is null) { status = "state-missing"; }
                    else if (exit is null)
                    {
                        string parent = ProcessStatus(state.ProcessId, state.ProcessStartTicks);
                        status = parent == "alive" ? "active" : parent == "not-running"
                            ? "process-ended-exit-unobserved" : "process-status-unavailable";
                    }
                    if (failed) { status = "collection-failed"; }
                    if (state is not null) { state = state with { Checkpoints = state.Checkpoints.Where(c => c.RecordedAt >= cutoff && c.RecordedAt <= until).ToArray() }; }
                    if (failure?.RecordedAt < cutoff || failure?.RecordedAt > until) { failure = null; }
                    if (failures is not null) { failures = failures with { Failures = failures.Failures.Where(f => f.RecordedAt >= cutoff && f.RecordedAt <= until).ToArray() }; }
                }
                catch (Exception error) when (IncidentStorage.IsStorageFailure(error)) { status = "collection-failed"; }
                reports.Add(new(directory.Name, status, state, failure, exit, evidence, failures, health, observer, observerStatus)
                { Flight = flight, FirstFailureFlight = failureFlight });
            }
            return new(1, "collected", 0, IncidentStorage.MaximumBytes, reports.ToArray())
            { AvailableSessions = available, OmittedSessions = Math.Max(0, available - reports.Count), WindowStart = cutoff };
        }
        catch (Exception error) when (IncidentStorage.IsStorageFailure(error))
        { return new(1, "collection-failed", 0, IncidentStorage.MaximumBytes, reports.ToArray()); }
    }

    internal static string ProcessStatus(int processId, long startTicks)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == startTicks ? "alive" : "not-running";
        }
        catch (ArgumentException) { return "not-running"; }
        catch (InvalidOperationException) { return "not-running"; }
        catch (System.ComponentModel.Win32Exception) { return "unavailable"; }
    }

    private static T? ReadPart<T>(string directory, string name, JsonTypeInfo<T> type, ref bool failed)
    {
        try { return IncidentStorage.Read(Path.Combine(directory, name), type); }
        catch (Exception error) when (IncidentStorage.IsStorageFailure(error)) { failed = true; return default; }
    }
}
