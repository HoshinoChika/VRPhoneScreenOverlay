using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace VRPhoneScreenOverlay.Diagnostics;

/// <summary>Windows x64 debugger observer supporting automatic and bounded manual sessions. All debugger calls stay on its entry thread.</summary>
public static class DiagnosticCaptureObserver
{
    private const uint Continue = 0x10002;
    private const uint NotHandled = 0x80010001;
    private const long MaximumDumpBytes = 32 * 1024 * 1024;

    public static int Run(string[] args)
    {
        if (args.Length != 4 || !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out int pid) ||
            !long.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks) || !Environment.Is64BitProcess)
        { return 2; }
        string directory = Path.GetFullPath(args[1]);
        try
        {
            IncidentStorage.ValidateDirectory(directory);
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) { return 2; }
            IncidentState? state = IncidentStorage.Read(Path.Combine(directory, "state.json"), IncidentJsonContext.Default.IncidentState);
            if (state is null || state.SessionId != Path.GetFileName(directory) || state.ProcessId != pid ||
                state.ProcessStartTicks != ticks || pid == Environment.ProcessId) { return 2; }
            using Process target = Process.GetProcessById(pid);
            if (target.StartTime.ToUniversalTime().Ticks != ticks) { return 2; }
            // Acquire before the process exits; FailFast may have no second-chance event.
            _ = target.SafeHandle;
            using FileStream lease = new(Path.Combine(directory, "capture.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return Observe(target, directory, state.AutomaticCapture);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or Win32Exception or JsonException)
        { return 3; }
    }

    private static int Observe(Process target, string directory, bool automatic)
    {
        uint processId = checked((uint)target.Id);
        if (!DebugActiveProcess(processId)) { throw new Win32Exception(Marshal.GetLastPInvokeError()); }
        bool attached = true;
        bool initialBreakpoint = true;
        bool capturedAccessViolation = false;
        bool capturedHang = false;
        DiagnosticHangDetector hangDetector = new();
        DiagnosticCaptureBudget budget = new();
        long nextCoverageWrite = 0;
        string lastStatus = "observing";
        string lastTrigger = "none";
        int? lastFailure = null;
        string privateDirectory = Path.Combine(directory, "private-dumps");
        try
        {
            if (!DebugSetProcessKillOnExit(false)) { throw new Win32Exception(Marshal.GetLastPInvokeError()); }
            using Timer watchdog = new(_ => Environment.Exit(4), null, TimeSpan.FromSeconds(20), Timeout.InfiniteTimeSpan);
            File.WriteAllText(Path.Combine(directory, "capture-safe"), "kill-on-exit-disabled");
            IncidentStorage.ValidateDirectory(privateDirectory);
            Directory.CreateDirectory(privateDirectory);
            // Crash dump internals can block. Exiting this observer must never kill its target.
            byte[] debugEvent = new byte[176];
            long deadline = automatic ? long.MaxValue : Environment.TickCount64 + (long)TimeSpan.FromMinutes(15).TotalMilliseconds;
            while (Environment.TickCount64 < deadline && !File.Exists(Path.Combine(directory, "capture-stop")))
            {
                if (!initialBreakpoint && Environment.TickCount64 >= nextCoverageWrite)
                {
                    watchdog.Change(TimeSpan.FromSeconds(20), Timeout.InfiniteTimeSpan);
                    try { SaveCoverage(); }
                    finally { watchdog.Change(Timeout.Infinite, Timeout.Infinite); }
                }
                if (!initialBreakpoint && ReadRequest(directory) is { } request)
                {
                    bool saved = Capture(request, 0, 0, null);
                    if (!budget.CanRetryNonfatal || (saved && ReadRequest(directory) == request))
                    {
                        watchdog.Change(TimeSpan.FromSeconds(20), Timeout.InfiniteTimeSpan);
                        try { SaveCoverage(); IncidentStorage.TryDelete(Path.Combine(directory, "capture-request")); }
                        finally { watchdog.Change(Timeout.Infinite, Timeout.Infinite); }
                    }
                }
                if (!initialBreakpoint && !capturedHang && hangDetector.Observe(HasStaleHeartbeat(directory), Environment.TickCount64))
                {
                    capturedHang = Capture("unresponsive", 0, 0, null);
                }
                if (!WaitForDebugEvent(debugEvent, 250))
                {
                    int error = Marshal.GetLastPInvokeError();
                    if (error == 121) { continue; }
                    throw new Win32Exception(error);
                }
                uint kind = BitConverter.ToUInt32(debugEvent, 0);
                uint eventPid = BitConverter.ToUInt32(debugEvent, 4);
                uint threadId = BitConverter.ToUInt32(debugEvent, 8);
                uint disposition = Continue;
                int resumeError = 0;
                try
                {
                    if (kind is 3 or 6)
                    {
                        nint file = new(BitConverter.ToInt64(debugEvent, 16));
                        if (file != nint.Zero) { using SafeFileHandle handle = new(file, ownsHandle: true); }
                    }
                    else if (kind == 1)
                    {
                        uint code = BitConverter.ToUInt32(debugEvent, 16);
                        bool firstChance = BitConverter.ToUInt32(debugEvent, 168) != 0;
                        disposition = NotHandled;
                        if (initialBreakpoint && code == 0x80000003)
                        {
                            initialBreakpoint = false;
                            disposition = Continue;
                            File.WriteAllText(Path.Combine(directory, "capture-ready.json"), automatic
                                ? "{\"status\":\"observing\",\"scope\":\"process-lifetime\"}"
                                : "{\"status\":\"observing\",\"maximumMinutes\":15}");
                            watchdog.Change(Timeout.Infinite, Timeout.Infinite);
                        }
                        else if (!firstChance || (code == 0xc0000005 && !capturedAccessViolation))
                        {
                            bool saved = Capture(firstChance ? "first-chance" : "unhandled", code, threadId, debugEvent[16..168]);
                            capturedAccessViolation |= code == 0xc0000005 && saved;
                        }
                    }
                    else if (kind == 5)
                    {
                        uint exitCode = BitConverter.ToUInt32(debugEvent, 16);
                        if (exitCode != 0) { Capture("termination", exitCode, threadId, null); }
                    }
                }
                finally
                {
                    if (!ContinueDebugEvent(eventPid, threadId, disposition))
                    { resumeError = Marshal.GetLastPInvokeError(); }
                }
                if (resumeError != 0) { throw new Win32Exception(resumeError); }
                if (kind == 5) { attached = false; return 0; }
            }
            return 0;

            bool Capture(string trigger, uint code, uint thread, byte[]? record)
            {
                bool terminal = trigger is "unhandled" or "termination";
                int slot = budget.Reserve(terminal, Environment.TickCount64);
                if (slot == 0) { return false; }
                string name = slot.ToString(CultureInfo.InvariantCulture) + "-" + trigger + ".dmp";
                string path = Path.Combine(privateDirectory, name);
                string status = "saved";
                long bytes = 0;
                int? failure = null;
                DumpCaptureQuality? quality = null;
                watchdog.Change(TimeSpan.FromSeconds(20), Timeout.InfiniteTimeSpan);
                try
                {
                    long retained = new DirectoryInfo(privateDirectory).EnumerateFiles("*.dmp").Sum(f => f.Length);
                    long available = (terminal ? 3L : 2L) * MaximumDumpBytes - retained;
                    if (available <= 0) { status = "storage-limit"; }
                    else
                    {
                        using FileStream output = new(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
                        quality = WindowsDiagnosticDump.WriteBestEffort(target, output, thread, record,
                            maximumBytes: Math.Min(MaximumDumpBytes, available));
                        bytes = output.Length;
                        output.Flush(flushToDisk: true);
                        if (bytes > MaximumDumpBytes) { status = "size-limit"; }
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception)
                { status = "capture-failed"; failure = error.HResult; }
                if (status != "saved") { IncidentStorage.TryDelete(path); }
                DiagnosticCaptureRecord metadata = new(DateTimeOffset.UtcNow, trigger, code, thread, status,
                    status == "saved" ? name : null, bytes, failure)
                { Detail = quality?.Detail ?? "unavailable", DetailFailureHResult = quality?.FallbackHResult };
                try
                {
                    lastStatus = status;
                    lastTrigger = trigger;
                    lastFailure = failure;
                    File.WriteAllBytes(Path.Combine(privateDirectory, name + ".json"),
                        JsonSerializer.SerializeToUtf8Bytes(metadata, IncidentJsonContext.Default.DiagnosticCaptureRecord));
                    budget.Complete(terminal, status == "saved");
                    SaveCoverage();
                    return status == "saved";
                }
                finally { watchdog.Change(Timeout.Infinite, Timeout.Infinite); }
            }

            void SaveCoverage()
            {
                nextCoverageWrite = Environment.TickCount64 + 1000;
                CaptureRunCoverage summary = new(Path.GetFileName(directory), DateTimeOffset.UtcNow,
                    budget.Attempts, budget.Saved, budget.Skipped)
                { LastStatus = lastStatus, LastTrigger = lastTrigger, FailureHResult = lastFailure };
                IncidentStorage.WriteAsync(Path.Combine(directory, "capture-status.json"), summary,
                    IncidentJsonContext.Default.CaptureRunCoverage, CancellationToken.None).AsTask().GetAwaiter().GetResult();
            }
        }
        finally
        {
            if (attached) { _ = DebugActiveProcessStop(processId); }
        }
    }

    private static bool HasStaleHeartbeat(string directory) =>
        DiagnosticHeartbeat.Read(Path.Combine(directory, "capture-heartbeat"), Environment.TickCount64) == DiagnosticHeartbeatState.UiStale;

    private static string? ReadRequest(string directory)
    {
        try
        {
            string path = Path.Combine(directory, "capture-request");
            FileInfo file = new(path);
            if (!file.Exists || file.Length > 64 || (file.Attributes & FileAttributes.ReparsePoint) != 0) { return null; }
            string value = File.ReadAllText(path);
            if (value is not ("functional-failure" or "pipeline-stall" or "user-report")) { return null; }
            return value;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DebugActiveProcess(uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DebugActiveProcessStop(uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DebugSetProcessKillOnExit([MarshalAs(UnmanagedType.Bool)] bool kill);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WaitForDebugEvent([Out] byte[] debugEvent, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ContinueDebugEvent(uint processId, uint threadId, uint status);
}

public sealed record DiagnosticCaptureRecord(DateTimeOffset RecordedAt, string Trigger, uint Code,
    uint ThreadId, string Status, string? FileName, long Bytes, int? FailureHResult)
{
    public string Detail { get; init; } = "triage";
    public int? DetailFailureHResult { get; init; }
}
