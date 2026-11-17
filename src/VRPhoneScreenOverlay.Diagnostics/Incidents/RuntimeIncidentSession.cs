using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Diagnostics;

/// <summary>Owns a bounded, durable lifecycle journal and a separate exit observer.</summary>
public sealed class RuntimeIncidentSession : IIncidentRecorder, IDisposable
{
    public const int CheckpointCapacity = 64;
    private readonly SemaphoreSlim _writer = new(1, 1);
    private readonly string _directory;
    private readonly FileStream? _lease;
    private IncidentState _state;
    private long _sequence;
    private long _storageFailures;
    private IncidentFailureHistory _failures = new(0, []);
    private Func<DiagnosticRuntimeHealth>? _healthProvider;
    private bool _disposed;
    private int _shutdownArmed;
    private long _nextRetention;

    private RuntimeIncidentSession(string directory, IncidentState state, FileStream? lease)
    {
        _directory = directory;
        _state = state;
        _lease = lease;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    public string SessionId => _state.SessionId;
    public event EventHandler? FailureRecorded;
    public string StorageStatus { get; private set; } = "ready";
    public long StorageFailures => Interlocked.Read(ref _storageFailures);
    internal async ValueTask<bool> SaveFlightAsync(string name, FlightRecord record, CancellationToken token)
    {
        try
        {
            await IncidentStorage.WriteAsync(Path.Combine(_directory, name), record,
                FlightJsonContext.Default.FlightRecord, token).ConfigureAwait(false);
            return true;
        }
        catch (Exception error) when (IncidentStorage.IsStorageFailure(error)) { StorageFailed(); return false; }
    }
    internal async ValueTask<bool> SaveFlightBytesAsync(string name, ReadOnlyMemory<byte> data, bool durable, CancellationToken token)
    {
        try
        {
            await IncidentStorage.WriteBytesAsync(Path.Combine(_directory, name), data, token, durable).ConfigureAwait(false);
            return true;
        }
        catch (Exception error) when (IncidentStorage.IsStorageFailure(error)) { StorageFailed(); return false; }
    }
    internal void SetHealthProvider(Func<DiagnosticRuntimeHealth> provider) => _healthProvider = provider;

    public static async ValueTask<RuntimeIncidentSession> StartAsync(
        string? root = null, string? monitorExecutable = null, CancellationToken cancellationToken = default)
    {
        root ??= IncidentStorage.DefaultRoot;
        string id = Guid.NewGuid().ToString("N");
        string directory = Path.Combine(Path.GetFullPath(root), id);
        using Process process = Process.GetCurrentProcess();
        string build = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        IncidentState state = new(1, id, Environment.ProcessId, process.StartTime.ToUniversalTime().Ticks,
            build, DateTimeOffset.UtcNow, false, "starting", 0, [])
        { Environment = IncidentEnvironmentCapture.Capture() };
        FileStream? lease = null;
        string status = "ready";
        try
        {
            IncidentStorage.ValidateDirectory(directory);
            Directory.CreateDirectory(directory);
            lease = new FileStream(Path.Combine(directory, "owner.lock"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            IncidentStorage.Prune(root);
        }
        catch (Exception error) when (IncidentStorage.IsStorageFailure(error)) { status = "storage-unavailable"; }
        RuntimeIncidentSession session = new(directory, state, lease) { StorageStatus = status };
        try
        {
            await session.CheckpointAsync(new("process", "started"), cancellationToken).ConfigureAwait(false);
            await session.StartMonitorAsync(monitorExecutable ?? Path.Combine(AppContext.BaseDirectory,
                AppIdentity.MaintenanceExecutableName), cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch { session.Dispose(); throw; }
    }

    public async ValueTask CheckpointAsync(IncidentCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (_disposed) { return; }
        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IncidentCheckpoint safe = checkpoint with
            {
                Stage = Bound(DiagnosticSanitizer.SanitizeText(checkpoint.Stage), 80),
                Phase = Bound(DiagnosticSanitizer.SanitizeText(checkpoint.Phase), 40),
                Backend = checkpoint.Backend is null ? null : Bound(DiagnosticSanitizer.SanitizeText(checkpoint.Backend), 256),
                Geometry = checkpoint.Geometry is { } geometry ? geometry with
                {
                    Stage = Bound(DiagnosticSanitizer.SanitizeText(geometry.Stage ?? "unknown"), 64),
                    Layout = Bound(DiagnosticSanitizer.SanitizeText(geometry.Layout ?? "unknown"), 64)
                } : null,
            };
            IncidentEntry[] entries = [.. _state.Checkpoints.TakeLast(CheckpointCapacity - 1),
                new(++_sequence, DateTimeOffset.UtcNow, safe)];
            _state = _state with
            {
                Checkpoints = entries,
                DiscardedCheckpoints = _state.DiscardedCheckpoints + (_state.Checkpoints.Length == CheckpointCapacity ? 1 : 0)
            };
            await PersistAsync(cancellationToken).ConfigureAwait(false);
            await AppendTimelineAsync(new(DateTimeOffset.UtcNow, "checkpoint", safe), cancellationToken).ConfigureAwait(false);
        }
        finally { _writer.Release(); }
    }

    public async Task ArmShutdownAsync()
    {
        if (Interlocked.Exchange(ref _shutdownArmed, 1) != 0) { return; }
        IncidentShutdownIntent intent = new(SessionId, _state.ProcessId, _state.ProcessStartTicks, Environment.TickCount64);
        Task write = Task.Run(async () =>
        {
            using CancellationTokenSource budget = new(TimeSpan.FromMilliseconds(250));
            await IncidentStorage.WriteAsync(Path.Combine(_directory, "shutdown-intent.json"), intent,
                IncidentJsonContext.Default.IncidentShutdownIntent, budget.Token).ConfigureAwait(false);
        });
        try { await write.WaitAsync(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false); }
        catch (Exception error) when (error is TimeoutException or OperationCanceledException || IncidentStorage.IsStorageFailure(error))
        {
            _ = write.ContinueWith(done => { _ = done.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    public async ValueTask CompleteAsync(CancellationToken cancellationToken = default)
    {
        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _state = _state with { CleanShutdown = true };
            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _writer.Release(); }
    }

    public void RecordFailure(Exception error, string kind)
    {
        ArgumentNullException.ThrowIfNull(error);
        NotifyFailure();
        try
        {
            IncidentFailure failure = IncidentExceptionCapture.Capture(error, kind, null);
            IncidentStorage.WriteFatal(Path.Combine(_directory, "failure.json"), failure);
        }
        catch (Exception failure) when (IncidentStorage.IsStorageFailure(failure)) { StorageFailed(); }
    }

    public async ValueTask FailureAsync(Exception exception, string kind, string? attemptId, CancellationToken cancellationToken)
    {
        if (_disposed) { return; }
        NotifyFailure();
        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            IncidentFailure failure = IncidentExceptionCapture.Capture(exception, kind, attemptId);
            // Eight bounded chains, dropping oldest with an explicit omission count.
            _failures = new(_failures.OmittedFailures + (_failures.Failures.Length == 8 ? 1 : 0),
                [.. _failures.Failures.TakeLast(7), failure]);
            // UTF-8/JSON escaping and long generated method names also obey the byte budget.
            while (_failures.Failures.Length > 1 &&
                System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(_failures, IncidentJsonContext.Default.IncidentFailureHistory).Length > IncidentStorage.MaximumBytes)
            { _failures = new(_failures.OmittedFailures + 1, _failures.Failures[1..]); }
            await IncidentStorage.WriteAsync(Path.Combine(_directory, "failures.json"), _failures,
                IncidentJsonContext.Default.IncidentFailureHistory, cancellationToken).ConfigureAwait(false);
            await AppendTimelineAsync(new(DateTimeOffset.UtcNow, "failure", Failure: failure), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (IncidentStorage.IsStorageFailure(failure)) { StorageFailed(); }
        finally { _writer.Release(); }
    }

    internal async ValueTask<string> SaveHealthAsync(DiagnosticRuntimeHealth health, CancellationToken token, bool durable = true)
    {
        try
        {
            if (Environment.TickCount64 >= _nextRetention)
            {
                _nextRetention = Environment.TickCount64 + 60000;
                IncidentStorage.Prune(Path.GetDirectoryName(_directory)!);
                foreach (string file in Directory.EnumerateFiles(_directory, "timeline*.jsonl"))
                {
                    if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow - DiagnosticRetentionPolicy.LocalWindow)
                    { IncidentStorage.TryDelete(file); }
                }
                if (Path.GetDirectoryName(_directory) == IncidentStorage.DefaultRoot)
                {
                    DiagnosticLocalFiles.PruneArchives(Path.Combine(DiagnosticLocalFiles.DiagnosticsDirectory, "local"));
                    DiagnosticLocalFiles.PruneArchives(Path.Combine(DiagnosticLocalFiles.DiagnosticsDirectory, "outgoing"));
                }
            }
            await IncidentStorage.WriteAsync(Path.Combine(_directory, "health.json"), health,
                IncidentJsonContext.Default.DiagnosticRuntimeHealth, token, durable).ConfigureAwait(false);
            return "ready";
        }
        catch (Exception error) when (IncidentStorage.IsStorageFailure(error)) { StorageFailed(); return "write-failed"; }
    }

    private async Task AppendTimelineAsync(IncidentTimelineEntry entry, CancellationToken token)
    {
        try
        {
            string path = Path.Combine(_directory, $"timeline-{DateTime.UtcNow:yyyyMMddHH}.jsonl");
            if (File.Exists(path) && new FileInfo(path).Length > 2 * 1024 * 1024)
            { File.Move(path, Path.Combine(_directory, $"timeline-{DateTime.UtcNow:yyyyMMddHH}-{Guid.NewGuid():N}.jsonl")); }
            byte[] data = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(entry, IncidentJsonContext.Default.IncidentTimelineEntry);
            // The source-generated context is indented; one compact JSON object per line.
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(data);
            string line = System.Text.Json.JsonSerializer.Serialize(document.RootElement);
            await using FileStream stream = new(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
            await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(line + "\n"), token).ConfigureAwait(false);
        }
        catch (Exception error) when (IncidentStorage.IsStorageFailure(error)) { StorageFailed(); }
    }

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        _lease?.Dispose();
        _writer.Dispose();
    }

    private async ValueTask PersistAsync(CancellationToken token)
    {
        try
        {
            _state = _state with { StorageFailures = StorageFailures, HealthAtCheckpoint = _healthProvider?.Invoke() };
            await IncidentStorage.WriteAsync(Path.Combine(_directory, "state.json"), _state,
                IncidentJsonContext.Default.IncidentState, token).ConfigureAwait(false);
            StorageStatus = "ready";
        }
        catch (Exception error) when (IncidentStorage.IsStorageFailure(error)) { StorageFailed(); }
    }

    private void StorageFailed()
    {
        Interlocked.Increment(ref _storageFailures);
        StorageStatus = "write-failed";
    }

    private void NotifyFailure()
    {
        try { FailureRecorded?.Invoke(this, EventArgs.Empty); }
        catch (Exception)
        {
            // A diagnostic subscriber must never replace the original application failure.
            StorageFailed();
        }
    }

    private async ValueTask StartMonitorAsync(string executable, CancellationToken token)
    {
        using CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromSeconds(3));
        try { await StartMonitorCoreAsync(executable, budget.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            _state = _state with { MonitorStatus = "startup-timeout" };
            await CheckpointAsync(new("exit-observer", "startup-timeout"), token).ConfigureAwait(false);
        }
    }

    private async ValueTask StartMonitorCoreAsync(string executable, CancellationToken token)
    {
        string status = "storage-unavailable";
        if (StorageStatus == "ready")
        {
            try
            {
                // The observer must not keep the install directory locked during an update.
                // Published maintenance is a self-contained single file; stage a private copy.
                string runner = Path.Combine(_directory, "monitor.exe");
                await using (FileStream source = new(executable, FileMode.Open, FileAccess.Read, FileShare.Read,
                    65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    if (source.Length > 64L * 1024 * 1024) { throw new IOException("Monitor executable exceeds capacity."); }
                    await using FileStream destination = new(runner, FileMode.CreateNew, FileAccess.ReadWrite,
                        FileShare.None, 65536, FileOptions.Asynchronous);
                    await source.CopyToAsync(destination, token).ConfigureAwait(false);
                    await destination.FlushAsync(token).ConfigureAwait(false);
                    source.Position = 0;
                    destination.Position = 0;
                    byte[] expected = await SHA256.HashDataAsync(source, token).ConfigureAwait(false);
                    byte[] actual = await SHA256.HashDataAsync(destination, token).ConfigureAwait(false);
                    if (!CryptographicOperations.FixedTimeEquals(expected, actual)) { throw new IOException("Monitor copy verification failed."); }
                }
                ProcessStartInfo start = new()
                {
                    FileName = runner,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                start.ArgumentList.Add("--diagnostic-monitor");
                start.ArgumentList.Add(_directory);
                start.ArgumentList.Add(_state.ProcessId.ToString(CultureInfo.InvariantCulture));
                start.ArgumentList.Add(_state.ProcessStartTicks.ToString(CultureInfo.InvariantCulture));
                using Process? monitor = Process.Start(start);
                status = "handshake-timeout";
                long deadline = Environment.TickCount64 + 2000;
                try
                {
                    while (monitor is not null && !monitor.HasExited && Environment.TickCount64 < deadline)
                    {
                        if (File.Exists(Path.Combine(_directory, "monitor-ready.json"))) { status = "observing"; break; }
                        await Task.Delay(25, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    if (monitor is not null && !monitor.HasExited) { monitor.Kill(entireProcessTree: true); }
                    throw;
                }
                if (monitor is null || monitor.HasExited) { status = "start-failed"; }
            }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.SecurityException or BadImageFormatException)
            { status = "start-failed"; }
        }
        _state = _state with { MonitorStatus = status };
        await CheckpointAsync(new("exit-observer", status), token).ConfigureAwait(false);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception error) { RecordFailure(error, "unhandled-exception"); }
    }

    private static string Bound(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];
}
