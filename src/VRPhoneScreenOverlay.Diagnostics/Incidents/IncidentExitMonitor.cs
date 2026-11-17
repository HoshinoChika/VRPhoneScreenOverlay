using System.Diagnostics;
using System.Globalization;

namespace VRPhoneScreenOverlay.Diagnostics;

public static class IncidentExitMonitor
{
    public static async Task<int> RunAsync(string directory, int processId, long startTicks, CancellationToken cancellationToken)
    {
        try
        {
            IncidentStorage.ValidateDirectory(directory);
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) { return 2; }
            IncidentState? initial = IncidentStorage.Read(Path.Combine(directory, "state.json"), IncidentJsonContext.Default.IncidentState);
            if (initial is null || initial.ProcessId != processId || initial.ProcessStartTicks != startTicks ||
                initial.SessionId != Path.GetFileName(directory)) { return 2; }
            using FileStream lease = new(Path.Combine(directory, "monitor.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            using Process parent = Process.GetProcessById(processId);
            if (parent.StartTime.ToUniversalTime().Ticks != startTicks) { return 2; }
            // Acquire the OS handle before the handshake so even a rapid exit retains its code.
            _ = parent.Handle;
            string processName = parent.ProcessName;
            using Process observer = Process.GetCurrentProcess();
            IncidentObserverHealth health = new(DateTimeOffset.UtcNow, observer.Id, observer.StartTime.ToUniversalTime().Ticks);
            await IncidentStorage.WriteAsync(Path.Combine(directory, "observer.json"), health,
                IncidentJsonContext.Default.IncidentObserverHealth, cancellationToken).ConfigureAwait(false);
            await IncidentStorage.WriteAsync(Path.Combine(directory, "monitor-ready.json"),
                new IncidentExit(DateTimeOffset.UtcNow, null, "observing", false),
                IncidentJsonContext.Default.IncidentExit, cancellationToken).ConfigureAwait(false);
            bool shutdownTimedOut = false;
            Task exitTask = parent.WaitForExitAsync(cancellationToken);
            using (PeriodicTimer timer = new(TimeSpan.FromSeconds(2)))
            {
                while (!exitTask.IsCompleted)
                {
                    Task<bool> tick = timer.WaitForNextTickAsync(cancellationToken).AsTask();
                    await Task.WhenAny(exitTask, tick).ConfigureAwait(false);
                    if (exitTask.IsCompleted) { timer.Dispose(); await tick.ConfigureAwait(false); break; }
                    await tick.ConfigureAwait(false);
                    try
                    {
                        IncidentShutdownIntent? intent = IncidentStorage.Read(Path.Combine(directory, "shutdown-intent.json"), IncidentJsonContext.Default.IncidentShutdownIntent);
                        if (ShutdownExpired(intent, initial, Environment.TickCount64))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            try
                            {
                                if (!parent.HasExited)
                                {
                                    parent.Kill(entireProcessTree: false);
                                    shutdownTimedOut = true;
                                }
                            }
                            catch (InvalidOperationException) when (parent.HasExited) { }
                            break;
                        }
                        await IncidentStorage.WriteAsync(Path.Combine(directory, "observer.json"), health with { SampledAt = DateTimeOffset.UtcNow },
                            IncidentJsonContext.Default.IncidentObserverHealth, cancellationToken, durable: false).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (IncidentStorage.IsStorageFailure(exception)) { /* Retain the OS exit handle even if a heartbeat cannot be saved. */ }
                }
            }
            await exitTask.ConfigureAwait(false);
            IncidentState? final = null;
            try { final = IncidentStorage.Read(Path.Combine(directory, "state.json"), IncidentJsonContext.Default.IncidentState); }
            catch (Exception error) when (IncidentStorage.IsStorageFailure(error)) { }
            bool clean = !shutdownTimedOut && final?.CleanShutdown == true && parent.ExitCode == 0;
            IncidentExit exit = new(DateTimeOffset.UtcNow, parent.ExitCode,
                shutdownTimedOut ? "shutdown-timeout-terminated" : clean ? "normal-exit" : "abnormal-exit-unclassified", clean);
            await IncidentStorage.WriteAsync(Path.Combine(directory, "exit.json"), exit,
                IncidentJsonContext.Default.IncidentExit, cancellationToken).ConfigureAwait(false);
            if (!clean)
            {
                // Exit evidence is already durable. Allow Windows a bounded interval to publish Event 1000.
                await Task.Delay(750, cancellationToken).ConfigureAwait(false);
                IncidentCrashEvidence evidence = await WindowsCrashEvidence.ReadAsync(processId, processName,
                    initial.StartedAt, exit.ObservedAt, cancellationToken).ConfigureAwait(false);
                await IncidentStorage.WriteAsync(Path.Combine(directory, "crash-events.json"), evidence,
                    IncidentJsonContext.Default.IncidentCrashEvidence, cancellationToken).ConfigureAwait(false);
            }
            return 0;
        }
        catch (Exception error) when (IncidentStorage.IsStorageFailure(error) ||
            error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return 3; }
    }

    internal static bool ShutdownExpired(IncidentShutdownIntent? intent, IncidentState state, long now) =>
        intent is not null && intent.SessionId == state.SessionId && intent.ProcessId == state.ProcessId &&
        intent.ProcessStartTicks == state.ProcessStartTicks && intent.RequestedTick >= 0 && now >= intent.RequestedTick &&
        now - intent.RequestedTick >= 20000;

    public static int Run(string[] args)
    {
        if (args.Length != 4 || !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out int pid) ||
            !long.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks)) { return 2; }
        return RunAsync(args[1], pid, ticks, CancellationToken.None).GetAwaiter().GetResult();
    }
}
