using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Channels;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Android;

public interface IAndroidConnectionLogSink : IAsyncDisposable
{
    public string? CurrentLogPath { get; }

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
    string? ExceptionMessage = null);

internal sealed class JsonLineAndroidConnectionLog : IAndroidConnectionLogSink
{
    private readonly Channel<AndroidConnectionLogEntry> _entries;
    private readonly CancellationTokenSource _stopSource = new();
    private readonly Task _writerTask;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };
    private bool _disposed;

    public JsonLineAndroidConnectionLog()
    {
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string logDirectory = Path.Combine(localData, AppIdentity.LocalDataFolderName, "logs");
        Directory.CreateDirectory(logDirectory);
        CurrentLogPath = Path.Combine(
            logDirectory,
            $"android-{DateTimeOffset.Now:yyyyMMdd}.jsonl");
        _entries = Channel.CreateBounded<AndroidConnectionLogEntry>(new BoundedChannelOptions(512)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        _writerTask = WriteLoopAsync(_stopSource.Token);
    }

    public string? CurrentLogPath { get; }

    public bool TryWrite(AndroidConnectionLogEntry entry) =>
        !_disposed && _entries.Writer.TryWrite(entry);

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
            await _writerTask.ConfigureAwait(false);
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
            await using FileStream stream = new(
                CurrentLogPath!,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite,
                bufferSize: 16 * 1024,
                useAsync: true);
            await using StreamWriter writer = new(stream);
            while (await _entries.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                bool wroteEntry = false;
                while (_entries.Reader.TryRead(out AndroidConnectionLogEntry? entry))
                {
                    if (entry is null)
                    {
                        continue;
                    }

                    string line = JsonSerializer.Serialize(entry, _serializerOptions);
                    await writer.WriteLineAsync(line.AsMemory(), cancellationToken)
                        .ConfigureAwait(false);
                    wroteEntry = true;
                }

                if (wroteEntry)
                {
                    await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
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
