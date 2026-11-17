using System.Text.Json;
using System.Text.Json.Serialization;

namespace VRPhoneScreenOverlay.Diagnostics;

public enum FlightComponent { Connection, Video, Audio, Input, SteamVr, Playspace }
public sealed record FlightChannel(FlightComponent Component, string State, string Reason,
    long Completed = 0, long Dropped = 0, double DelayMilliseconds = 0, long Bytes = 0, string Stage = "none")
{
    // Null means the producer did not provide measurement validity (older channels).
    public long? DelaySampleCount { get; init; }
    public bool? HasDelaySamples => DelaySampleCount is long count ? count > 0 && double.IsFinite(DelayMilliseconds) : null;
}
public sealed record SerializedFlightRecord(FlightRecord Record, ReadOnlyMemory<byte> Json);
public sealed record FlightSample(long Sequence, DateTimeOffset At, FlightChannel[] Channels);
public sealed record FlightRecord(string SessionId, int Capacity, long Evicted, long CollectionFailures,
    long PersistenceFailures, FlightSample[] Samples);

/// <summary>One periodic sampler, 120 samples maximum, evict-oldest, no per-frame producers.</summary>
public sealed class RuntimeFlightRecorder : IDisposable
{
    public const int Capacity = 120;
    private readonly RuntimeIncidentSession _session;
    private readonly Func<FlightChannel[]> _sample;
    private readonly Queue<FlightSample> _samples = new(Capacity);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private readonly TimeSpan _period;
    private SerializedFlightRecord _latest;
    private long _sequence;
    private long _evicted;
    private long _collectionFailures;
    private long _persistenceFailures;
    private bool _failureSaved;
    private bool _disposed;

    public RuntimeFlightRecorder(RuntimeIncidentSession session, Func<FlightChannel[]> sample)
        : this(session, sample, TimeSpan.FromSeconds(1)) { }

    internal RuntimeFlightRecorder(RuntimeIncidentSession session, Func<FlightChannel[]> sample, TimeSpan period)
    {
        _session = session;
        _sample = sample;
        _period = period;
        FlightRecord initial = Snapshot();
        _latest = new(initial, IncidentStorage.Serialize(initial, FlightJsonContext.Default.FlightRecord));
        _worker = RunAsync(_stop.Token);
    }

    public FlightRecord Latest => Volatile.Read(ref _latest).Record;
    public SerializedFlightRecord LatestSerialized => Volatile.Read(ref _latest);

    private async Task RunAsync(CancellationToken token)
    {
        using PeriodicTimer timer = new(_period);
        try
        {
            do
            {
                FlightChannel[] channels;
                try { channels = Normalize(_sample()); }
                catch (Exception)
                {
                    // Diagnostic sampling must not fail the application when a provider is shutting down.
                    // Preserve the count only; provider exception text can contain private state.
                    _collectionFailures++;
                    continue;
                }
                if (_samples.Count == Capacity) { _samples.Dequeue(); _evicted++; }
                _samples.Enqueue(new(++_sequence, DateTimeOffset.UtcNow, channels));
                FlightRecord record = Snapshot();
                byte[] json = JsonSerializer.SerializeToUtf8Bytes(record, FlightJsonContext.Default.FlightRecord);
                while (json.Length > IncidentStorage.MaximumBytes)
                {
                    _samples.Dequeue();
                    _evicted++;
                    record = Snapshot();
                    json = JsonSerializer.SerializeToUtf8Bytes(record, FlightJsonContext.Default.FlightRecord);
                }
                bool failed = channels.Any(c => c.State is "Faulted" or "Failed");
                Volatile.Write(ref _latest, new SerializedFlightRecord(record, json));
                if (failed && !_failureSaved)
                {
                    _failureSaved = await _session.SaveFlightBytesAsync("flight-failure.json", json, durable: true, token).ConfigureAwait(false);
                    if (!_failureSaved) { _persistenceFailures++; }
                }
                if (_sequence % 2 == 0 || failed)
                {
                    if (!await _session.SaveFlightBytesAsync("flight-record.json", json, durable: false, token).ConfigureAwait(false))
                    { _persistenceFailures++; }
                }
            } while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    internal static FlightChannel[] Normalize(FlightChannel[] channels) => channels.Take(6).Select(c => c with
    {
        State = Token(c.State),
        Reason = Token(c.Reason),
        Stage = Token(c.Stage),
        DelaySampleCount = c.DelaySampleCount is long count
            ? double.IsFinite(c.DelayMilliseconds) && c.DelayMilliseconds >= 0 ? Math.Max(0, count) : 0
            : null,
        DelayMilliseconds = double.IsFinite(c.DelayMilliseconds) ? Math.Clamp(c.DelayMilliseconds, 0, 600000) : 0
    }).ToArray();

    private static string Token(string value) => value.Length is > 0 and <= 80 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') ? value : "invalid-token";

    private FlightRecord Snapshot() => new(_session.SessionId, Capacity, _evicted, _collectionFailures,
        _persistenceFailures, _samples.ToArray());

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        _stop.Cancel();
        try
        {
            _worker.WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(2));
            _ = _session.SaveFlightAsync("flight-record.json", Snapshot(), deadline.Token).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception error) when (error is OperationCanceledException or TimeoutException) { }
        finally { _stop.Dispose(); }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(FlightRecord))]
internal sealed partial class FlightJsonContext : JsonSerializerContext;
