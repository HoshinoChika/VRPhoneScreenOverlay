using System.Diagnostics;
using System.Globalization;
using System.Reflection;

namespace VRPhoneScreenOverlay.Diagnostics;

/// <summary>Owns one local-only diagnostic observation at a time.</summary>
public sealed class DiagnosticCaptureService : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _helper;
    private readonly string _root;
    private Func<FlightRecord>? _flight;
    private Func<SerializedFlightRecord>? _serializedFlight;
    private readonly CancellationTokenSource _lifetime = new();
    private Process? _observer;
    private string? _directory;
    private Task? _heartbeat;
    private long _uiPulse;
    private bool _disposed;
    private bool _fatalExit;
    private readonly DiagnosticFaultDetector _faultDetector = new();
    private bool _automatic;
    private bool _stopRequested;
    private int _recoveries;
    private Task? _recovery;
    private long _stopEpoch;
    private RuntimeIncidentSession? _incident;
    private int _pendingFailure;
    private string _status = "idle";

    public DiagnosticCaptureService(string helper, string? root = null, Func<FlightRecord>? flight = null)
    {
        _helper = helper;
        _root = root ?? Path.Combine(DiagnosticLocalFiles.DiagnosticsDirectory, "captures");
        _flight = flight;
    }

    public string Status => Volatile.Read(ref _status);
    public bool IsObserving => Status is "starting" or "observing" or "automatic" or "heartbeat-failed" or "stop-pending";
    public void Pulse() => Interlocked.Exchange(ref _uiPulse, Environment.TickCount64);
    public void SetFlightProvider(Func<FlightRecord> provider)
    { Volatile.Write(ref _flight, provider); Volatile.Write(ref _serializedFlight, null); }
    public void SetSerializedFlightProvider(Func<SerializedFlightRecord> provider)
    { Volatile.Write(ref _serializedFlight, provider); Volatile.Write(ref _flight, null); }
    public void PreserveObserverForFatalExit() => _fatalExit = true;
    public void ObserveIncident(RuntimeIncidentSession incident)
    {
        if (_incident is not null) { _incident.FailureRecorded -= OnFailureRecorded; }
        _incident = incident;
        _incident.FailureRecorded += OnFailureRecorded;
    }
    private void OnFailureRecorded(object? sender, EventArgs args) => Interlocked.Exchange(ref _pendingFailure, 1);

    public DiagnosticCaptureCoverage ReadCoverage()
    {
        List<CaptureRunCoverage> runs = [];
        int unavailable = 0;
        string collection = "not-collected";
        try
        {
            IncidentStorage.ValidateDirectory(_root);
            if (Directory.Exists(_root))
            {
                collection = "collected";
                foreach (string directory in Directory.EnumerateDirectories(_root).Where(d => Guid.TryParseExact(Path.GetFileName(d), "N", out _)).Take(16))
                {
                    try
                    {
                        IncidentStorage.ValidateDirectory(directory);
                        CaptureRunCoverage? record = IncidentStorage.Read(Path.Combine(directory, "capture-status.json"),
                            IncidentJsonContext.Default.CaptureRunCoverage);
                        if (record is not null && record.CaptureId == Path.GetFileName(directory)) { runs.Add(record); }
                        else { unavailable++; }
                    }
                    catch (Exception error) when (IncidentStorage.IsStorageFailure(error)) { unavailable++; }
                }
            }
        }
        catch (Exception error) when (IncidentStorage.IsStorageFailure(error)) { collection = "collection-failed"; }
        return new(Status, _recoveries, runs.ToArray())
        { CollectionStatus = unavailable > 0 ? "partial" : collection, UnavailableRuns = unavailable };
    }

    public Task ArmNextLaunchAsync(bool enabled, CancellationToken token) =>
        DiagnosticStartupRequest.SetAsync(_root, enabled, DateTimeOffset.UtcNow, token);

    public async Task ResumeStartupAsync(CancellationToken token)
    {
        try
        {
            if (DiagnosticStartupRequest.Consume(_root, DateTimeOffset.UtcNow))
            { _ = await StartAsync(token).ConfigureAwait(false); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { SetStatus("startup-request-unavailable"); }
    }

    public Task<string> StartAsync(CancellationToken token) => StartCoreAsync(false, token);
    public Task<string> StartAutomaticAsync(CancellationToken token) => StartCoreAsync(true, token);

    internal TimeSpan StartupBudget { get; init; } = TimeSpan.FromSeconds(5);

    private async Task<string> StartCoreAsync(bool automatic, CancellationToken token, long? recoveryEpoch = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        CancellationToken callerToken = token;
        using CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (StartupBudget == TimeSpan.Zero) { await budget.CancelAsync().ConfigureAwait(false); }
        else { budget.CancelAfter(StartupBudget); }
        token = budget.Token;
        try
        {
            if (recoveryEpoch is not null && recoveryEpoch != Interlocked.Read(ref _stopEpoch)) { return Status; }
            if (_observer is { HasExited: false }) { return Status; }
            _automatic = automatic;
            _stopRequested = false;
            if (_heartbeat is not null) { await _heartbeat.ConfigureAwait(false); }
            _observer?.Dispose();
            _observer = null;
            Volatile.Write(ref _status, "starting");
            using FileStream storeLease = DiagnosticCaptureStore.Lock(_root);
            if (!DiagnosticCaptureStore.Reserve(_root))
            { return SetStatus("capacity-limit"); }
            string id = Guid.NewGuid().ToString("N");
            _directory = Path.Combine(_root, id);
            Directory.CreateDirectory(_directory);
            using Process current = Process.GetCurrentProcess();
            long ticks = current.StartTime.ToUniversalTime().Ticks;
            IncidentState state = new(1, id, current.Id, ticks,
                Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
                DateTimeOffset.UtcNow, false, "capture-requested", 0, [])
            { Environment = IncidentEnvironmentCapture.Capture(), AutomaticCapture = automatic };
            await IncidentStorage.WriteAsync(Path.Combine(_directory, "state.json"), state,
                IncidentJsonContext.Default.IncidentState, token).ConfigureAwait(false);
            string runner = Path.Combine(_directory, "capture.exe");
            await using (FileStream source = File.OpenRead(_helper))
            await using (FileStream destination = new(runner, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                65536, FileOptions.Asynchronous))
            {
                if (source.Length > 24L * 1024 * 1024) { throw new IOException("Capture helper exceeds reserved capacity."); }
                await source.CopyToAsync(destination, token).ConfigureAwait(false);
            }
            ProcessStartInfo start = new(runner) { UseShellExecute = false, CreateNoWindow = true };
            foreach (string value in new[] { "--diagnostic-capture", _directory,
                current.Id.ToString(CultureInfo.InvariantCulture), ticks.ToString(CultureInfo.InvariantCulture) })
            { start.ArgumentList.Add(value); }
            _observer = Process.Start(start) ?? throw new IOException("Observer could not start.");
            long deadline = Environment.TickCount64 + 5000;
            while (!_observer.HasExited && Environment.TickCount64 < deadline)
            {
                if (File.Exists(Path.Combine(_directory, "capture-ready.json")))
                {
                    Pulse();
                    SetStatus(automatic ? "automatic" : "observing");
                    _heartbeat = WriteHeartbeatAsync(_observer, _directory, _lifetime.Token);
                    return Status;
                }
                await Task.Delay(25, token).ConfigureAwait(false);
            }
            await StopObserverAsync().ConfigureAwait(false);
            ScheduleRecovery();
            return SetStatus(_observer is { HasExited: false } ? "stop-pending" : "start-failed");
        }
        catch (OperationCanceledException)
        {
            await StopObserverAsync().ConfigureAwait(false);
            if (!callerToken.IsCancellationRequested)
            {
                ScheduleRecovery();
                return SetStatus(_observer is { HasExited: false } ? "stop-pending" : "startup-timeout");
            }
            SetStatus(_observer is { HasExited: false } ? "stop-pending" : "cancelled");
            throw;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or InvalidOperationException or System.Security.SecurityException or BadImageFormatException)
        {
            await StopObserverAsync().ConfigureAwait(false);
            ScheduleRecovery();
            return SetStatus(_observer is { HasExited: false } ? "stop-pending" : "start-failed");
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            _stopRequested = true;
            Interlocked.Increment(ref _stopEpoch);
            await StopObserverAsync().ConfigureAwait(false);
            SetStatus(_observer is { HasExited: false } ? "stop-pending" : "stopped");
        }
        finally { _gate.Release(); }
    }

    private async Task StopObserverAsync()
    {
        if (_observer is not { HasExited: false }) { return; }
        try
        {
            if (_directory is not null) { await File.WriteAllTextAsync(Path.Combine(_directory, "capture-stop"), "stop").ConfigureAwait(false); }
            await _observer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or TimeoutException)
        {
            // Never kill a debugger before it confirms DebugSetProcessKillOnExit(false).
            if (!_observer.HasExited && _directory is not null && File.Exists(Path.Combine(_directory, "capture-safe")))
            {
                try
                {
                    _observer.Kill();
                    await _observer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                }
                catch (Exception stopError) when (stopError is System.ComponentModel.Win32Exception or InvalidOperationException or TimeoutException)
                { SetStatus("stop-pending"); }
            }
        }
        if (_observer.HasExited && _heartbeat is not null) { await _heartbeat.ConfigureAwait(false); }
    }

    public async Task<string?> ExportLatestAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { return await DiagnosticCaptureStore.ExportLatestAsync(_root, token).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task<ConsentedMemoryAttachment?> PrepareConsentedUploadAsync(CancellationToken token, DateTimeOffset? windowEnd = null)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_observer is { HasExited: false } && _directory is not null)
            {
                string request = Path.Combine(_directory, "capture-request");
                await File.WriteAllTextAsync(request, "user-report", token).ConfigureAwait(false);
                long deadline = Environment.TickCount64 + 22000;
                while (!_observer.HasExited && File.Exists(request) && Environment.TickCount64 < deadline)
                { await Task.Delay(100, token).ConfigureAwait(false); }
            }
            DateTimeOffset end = windowEnd ?? DateTimeOffset.UtcNow;
            if (DateTimeOffset.UtcNow > end) { end = DateTimeOffset.UtcNow; }
            // Finalized dump files are immutable. The store lease prevents pruning while copying them.
            // Keep our observer attached; only allow its own active directory past the lease check.
            string? active = _observer is { HasExited: false } && _directory is not null ? Path.GetFileName(_directory) : null;
            string? path = await DiagnosticCaptureStore.ExportWindowAsync(_root, end, token, active).ConfigureAwait(false);
            return path is null ? null : await ConsentedMemoryAttachment.CreateAsync(path, token, end).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task WriteHeartbeatAsync(Process observer, string directory, CancellationToken token)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
        FlightSnapshotWriter flightWriter = new(Path.Combine(directory, "flight-record.json"));
        try
        {
            do
            {
                if (observer.HasExited)
                {
                    SetStatus(observer.ExitCode == 0 ? "finished" : "observer-failed");
                    if (observer.ExitCode != 0) { ScheduleRecovery(); }
                    return;
                }
                string? request = null;
                try
                {
                    await DiagnosticHeartbeat.WriteAsync(Path.Combine(directory, "capture-heartbeat"),
                        Interlocked.Read(ref _uiPulse), Environment.TickCount64, token).ConfigureAwait(false);
                    Func<FlightRecord>? flight = Volatile.Read(ref _flight);
                    request = Interlocked.Exchange(ref _pendingFailure, 0) != 0 ? "functional-failure" : null;
                    SerializedFlightRecord? serialized = Volatile.Read(ref _serializedFlight)?.Invoke();
                    FlightRecord? sample = serialized?.Record ?? flight?.Invoke();
                    if (sample is not null)
                    {
                        string? sampledRequest = _faultDetector.Observe(sample, Environment.TickCount64);
                        request ??= sampledRequest;
                        await flightWriter.WriteAsync(sample, serialized?.Json, request is not null, token).ConfigureAwait(false);
                    }
                    if (request is not null)
                    {
                        // A single coalescing request slot, owned by this sampler; no queue or per-frame I/O.
                        await File.WriteAllTextAsync(Path.Combine(directory, "capture-request"), request, token).ConfigureAwait(false);
                    }
                    if (Status == "heartbeat-failed") { SetStatus(_automatic ? "automatic" : "observing"); }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    SetStatus("heartbeat-failed");
                    // A transient file conflict must not terminate future heartbeat/snapshot attempts.
                    if (request is not null) { Interlocked.Exchange(ref _pendingFailure, 1); }
                }
            } while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        { SetStatus("heartbeat-failed"); }
    }

    private string SetStatus(string value) { Volatile.Write(ref _status, value); return value; }

    private void ScheduleRecovery()
    {
        if (!_automatic || _stopRequested || _disposed || _fatalExit || _recoveries >= 2) { return; }
        _recoveries++;
        long epoch = Interlocked.Read(ref _stopEpoch);
        // At most two recovery tasks per service lifetime, never per frame or repeated fault event.
        _recovery = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), _lifetime.Token).ConfigureAwait(false);
                if (!_stopRequested && !_disposed && !_fatalExit)
                { _ = await StartCoreAsync(true, _lifetime.Token, epoch).ConfigureAwait(false); }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (ObjectDisposedException) { }
        });
    }

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        if (_incident is not null) { _incident.FailureRecorded -= OnFailureRecorded; }
        _lifetime.Cancel();
        // Managed stack unwinding must not detach before the runtime emits its final exception event.
        if (!_fatalExit) { StopAsync(CancellationToken.None).GetAwaiter().GetResult(); }
        if (_recovery is not null)
        {
            try { _recovery.WaitAsync(TimeSpan.FromSeconds(4)).GetAwaiter().GetResult(); }
            catch (Exception error) when (error is OperationCanceledException or TimeoutException) { }
        }
        _observer?.Dispose();
        _lifetime.Dispose();
        _gate.Dispose();
    }
}
