using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Android;

public interface IAndroidConnectionLogSink : IAsyncDisposable
{
    public string? CurrentLogPath { get; }
    public DiagnosticLogHealth Health => DiagnosticLogHealth.Unavailable;

    public bool TryWrite(AndroidConnectionLogEntry entry);
}

public sealed record AndroidConnectionLogEntry(
    DateTimeOffset Timestamp,
    string EventName,
    string ReasonCode,
    string Message,
    string? DeviceKey = null,
    AndroidConnectionState? State = null,
    int? DeviceCount = null,
    long? DurationMilliseconds = null,
    string? ToolVersion = null,
    string? DeviceModel = null,
    string? AndroidVersion = null,
    int? AndroidSdk = null,
    string? CpuAbi = null,
    int? VideoWidth = null,
    int? VideoHeight = null,
    long? PacketCount = null,
    long? PayloadBytes = null,
    long? CommandSequence = null,
    string? CommandKind = null,
    int? QueueDepth = null,
    long? ReplacedMoveCount = null,
    string? ExceptionType = null,
    string? ExceptionMessage = null)
{
    public AdbCommandDiagnostic? AdbCommand { get; init; }
    public AndroidScanDiagnostic? Scan { get; init; }
    public AdbServerSummary? AdbServer { get; init; }
    public ComponentRecoveryEvidence? ComponentRecovery { get; init; }
    public InputDispatchDiagnostic? InputDispatch { get; init; }
    public ServerOutputDiagnostic? ServerOutput { get; init; }
    public string? ServerSessionId { get; init; }
    public PhoneInputPathDiagnostic? InputPath { get; init; }
    public InputRoutingDiagnostic? InputRouting { get; init; }
    public ControlGeometryDiagnostic? ControlGeometry { get; init; }
    public BindingSnapshotRetentionDiagnostic? BindingSnapshotRetention { get; init; }
    public SteamVrStartupDiagnostic? SteamVrStartup { get; init; }
    public int? StartupAttempt { get; init; }
    public bool? StartupPending { get; init; }
    public bool? ManifestLaunchObserved { get; init; }
    public VideoQualityDiagnostic? VideoQuality { get; init; }
    public int? SamplingIntervalSeconds { get; init; }
    public string? SamplingReason { get; init; }
    public DiagnosticSamplingWindow? SamplingWindow { get; init; }
}

public sealed record AndroidScanDiagnostic(int RawDevices, int SupportedDevices, int UsbDevices,
    int NetworkDevices, int ReadyDevices, int UnauthorizedDevices, int OfflineDevices,
    string ViewedMethod, int ReadyForViewedMethod, bool AutomaticSelectionSuppressed);

internal sealed class JsonLineAndroidConnectionLog : IAndroidConnectionLogSink
{
    internal const int Capacity = 512;
    private readonly Channel<AndroidConnectionLogEntry> _entries;
    private readonly CancellationTokenSource _stopSource = new();
    private readonly Task _writerTask;
    private readonly Func<CancellationToken, ValueTask<Stream>>? _openStream;
    private long _accepted;
    private long _flushed;
    private long _dropped;
    private long _rejected;
    private long _writeFailures;
    private long _lastFlushTicks;
    private string _state = "starting";
    private Exception? _failure;
    private readonly object _summaryGate = new();
    // At most 16 code groups; evict the least-recently observed group and count it.
    private readonly Dictionary<(string Event, string Reason), DiagnosticLogErrorSummary> _errors = [];
    private long _evictedSummaries;
    private readonly AdaptiveDiagnosticSampling _sampling = new();
    private readonly Queue<AndroidConnectionLogEntry> _priority = new();
    private long _sampledOut, _priorityDropped;
    private long _retentionFailures;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private bool _disposed;

    public JsonLineAndroidConnectionLog() : this(null, null) { }

    internal JsonLineAndroidConnectionLog(string? logPath, Func<CancellationToken, ValueTask<Stream>>? openStream)
    {
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string logDirectory = Path.Combine(localData, AppIdentity.LocalDataFolderName, "logs");
        CurrentLogPath = logPath ?? Path.Combine(
            logDirectory,
            $"android-{DateTimeOffset.Now:yyyyMMdd}.jsonl");
        _openStream = openStream;
        _entries = Channel.CreateBounded<AndroidConnectionLogEntry>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        }, _ => Interlocked.Increment(ref _dropped));
        _writerTask = WriteLoopAsync(_stopSource.Token);
    }

    public string? CurrentLogPath { get; }

    public DiagnosticLogHealth Health
    {
        get
        {
            long accepted = Interlocked.Read(ref _accepted);
            long flushed = Interlocked.Read(ref _flushed);
            long dropped = Interlocked.Read(ref _dropped);
            long ticks = Interlocked.Read(ref _lastFlushTicks);
            Exception? failure = Volatile.Read(ref _failure);
            DiagnosticLogErrorSummary[] summaries;
            long evicted;
            lock (_summaryGate) { summaries = _errors.Values.ToArray(); evicted = _evictedSummaries; }
            return new(Volatile.Read(ref _state), Capacity, accepted, flushed, dropped,
                Interlocked.Read(ref _rejected), Math.Max(0, accepted - flushed - dropped),
                Interlocked.Read(ref _writeFailures), ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero),
                failure?.GetType().FullName, failure?.HResult)
            {
                ErrorSummaries = summaries,
                EvictedErrorSummaries = evicted,
                SampledOut = Interlocked.Read(ref _sampledOut),
                PriorityDropped = Interlocked.Read(ref _priorityDropped),
                RetentionFailures = Interlocked.Read(ref _retentionFailures)
            };
        }
    }

    public bool TryWrite(AndroidConnectionLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (_disposed || Volatile.Read(ref _state) == "write-failed")
        { Interlocked.Increment(ref _rejected); return false; }
        lock (_summaryGate)
        {
            bool error = _sampling.IsNewError(entry, IsError(entry));
            if (error) { SummarizeError(entry); }
            if (!_sampling.ShouldWrite(ref entry, error))
            { Interlocked.Increment(ref _sampledOut); return true; }
            if (!_disposed && (error || entry.EventName is "control_metrics" or "control_connected" or
                "control_stopped" or "control_geometry" or "state_changed" or "video_connected" or
                "video_stopped" or "steamvr_startup_setting"))
            {
                if (_priority.Count == 256) { _priority.Dequeue(); Interlocked.Increment(ref _priorityDropped); }
                _priority.Enqueue(entry);
            }
        }
        if (!_disposed && _entries.Writer.TryWrite(entry)) { Interlocked.Increment(ref _accepted); return true; }
        Interlocked.Increment(ref _rejected);
        return false;
    }

    private static bool IsError(AndroidConnectionLogEntry entry)
    {
        string reason = entry.ReasonCode;
        return !(entry.ExceptionType is null && !reason.Contains("FAILED", StringComparison.Ordinal) &&
            !reason.Contains("FAILURE", StringComparison.Ordinal) && !reason.Contains("MISMATCH", StringComparison.Ordinal) &&
            !reason.Contains("TIMEOUT", StringComparison.Ordinal) && !reason.Contains("UNEXPECTED", StringComparison.Ordinal) &&
            !reason.Contains("CONFLICT", StringComparison.Ordinal) && !reason.Contains("INCOMPLETE", StringComparison.Ordinal) &&
            !reason.Contains("OUTPUT_LIMIT", StringComparison.Ordinal) && !reason.Contains("ENDPOINT_INVALID", StringComparison.Ordinal) &&
            !reason.EndsWith("_INPUT_PERMISSION_DENIED", StringComparison.Ordinal) &&
            !reason.EndsWith("_SERVER_OUTPUT_ERROR", StringComparison.Ordinal));
    }

    private void SummarizeError(AndroidConnectionLogEntry entry)
    {
        if (!IsError(entry)) { return; }
        string reason = entry.ReasonCode;
        string name = entry.EventName;
        if (name.Length > 128) { name = name[..128]; }
        if (reason.Length > 128) { reason = reason[..128]; }
        lock (_summaryGate)
        {
            var key = (name, reason);
            if (_errors.TryGetValue(key, out DiagnosticLogErrorSummary? prior))
            { _errors[key] = prior with { Count = prior.Count + 1, LastAt = entry.Timestamp }; return; }
            if (_errors.Count == 16)
            {
                _errors.Remove(_errors.MinBy(pair => pair.Value.LastAt).Key);
                _evictedSummaries++;
            }
            _errors[key] = new(name, reason, 1, entry.Timestamp, entry.Timestamp);
        }
    }

    private async ValueTask<Stream> OpenStreamAsync(CancellationToken token)
    {
        if (_openStream is not null) { return await _openStream(token).ConfigureAwait(false); }
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(CurrentLogPath!)!);
        return new FileStream(CurrentLogPath!, FileMode.Append, FileAccess.Write,
            FileShare.ReadWrite, 16 * 1024, FileOptions.Asynchronous);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _entries.Writer.TryComplete();
        try
        {
            await _writerTask.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Volatile.Write(ref _state, "shutdown-timeout");
            await _stopSource.CancelAsync().ConfigureAwait(false);
        }
        finally
        {
            await _stopSource.CancelAsync().ConfigureAwait(false);
            _stopSource.Dispose();
        }
    }

    private async Task WriteLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_openStream is null)
            {
                await WriteBoundedFilesAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            await using Stream stream = await OpenStreamAsync(cancellationToken).ConfigureAwait(false);
            await using StreamWriter writer = new(stream);
            Volatile.Write(ref _state, "writing");
            while (await _entries.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                int batch = 0;
                while (batch < 64 && _entries.Reader.TryRead(out AndroidConnectionLogEntry? entry))
                {
                    if (entry is null)
                    {
                        continue;
                    }

                    string line = JsonSerializer.Serialize(entry, _serializerOptions);
                    await writer.WriteLineAsync(line.AsMemory(), cancellationToken)
                        .ConfigureAwait(false);
                    batch++;
                }

                if (batch > 0)
                {
                    await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                    Interlocked.Add(ref _flushed, batch);
                    Interlocked.Exchange(ref _lastFlushTicks, DateTimeOffset.UtcNow.Ticks);
                }
            }
            Volatile.Write(ref _state, "stopped");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
            Volatile.Write(ref _failure, error);
            Interlocked.Increment(ref _writeFailures);
            Volatile.Write(ref _state, "write-failed");
            _entries.Writer.TryComplete();
        }
    }

    private async Task WriteBoundedFilesAsync(CancellationToken token)
    {
        BoundedDiagnosticLogFiles normal = new(CurrentLogPath!);
        BoundedDiagnosticLogFiles priority = new(Path.Combine(Path.GetDirectoryName(CurrentLogPath!)!, "android-events.jsonl"));
        Volatile.Write(ref _state, "writing");
        while (await _entries.Reader.WaitToReadAsync(token).ConfigureAwait(false))
        {
            List<string> batch = [];
            while (batch.Count < 64 && _entries.Reader.TryRead(out AndroidConnectionLogEntry? entry))
            { batch.Add(JsonSerializer.Serialize(entry, _serializerOptions)); }
            List<string> events = [];
            lock (_summaryGate)
            {
                while (_priority.TryDequeue(out AndroidConnectionLogEntry? entry))
                { events.Add(JsonSerializer.Serialize(entry, _serializerOptions)); }
            }
            await priority.AppendAsync(events, token).ConfigureAwait(false);
            await normal.AppendAsync(batch, token).ConfigureAwait(false);
            Interlocked.Exchange(ref _retentionFailures, normal.CleanupFailures + priority.CleanupFailures);
            Interlocked.Add(ref _flushed, batch.Count);
            Interlocked.Exchange(ref _lastFlushTicks, DateTimeOffset.UtcNow.Ticks);
        }
        Volatile.Write(ref _state, "stopped");
    }
}

internal sealed class NullAndroidConnectionLog : IAndroidConnectionLogSink
{
    public static NullAndroidConnectionLog Instance { get; } = new();

    private NullAndroidConnectionLog()
    {
    }

    public string? CurrentLogPath => null;

    public bool TryWrite(AndroidConnectionLogEntry entry) => false;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
