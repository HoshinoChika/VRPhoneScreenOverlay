using System.Runtime.ExceptionServices;
using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.Media;

namespace VRPhoneScreenOverlay.Session;

internal sealed record DecodedStreamFrame(DecodedVideoFrame Frame, int Generation, int SourceWidth,
    int SourceHeight, string Backend, long? EstimatedCaptureTimestamp) : IDisposable
{
    public void Dispose() => Frame.Dispose();
}

/// <summary>
/// One encoded-input worker per session. Output has capacity one, replacing and disposing
/// the older decoded frame; encoded packets retain the upstream reader's keyframe policy.
/// </summary>
internal sealed class LatestDecodedVideoFrameReader : IAsyncDisposable
{
    private readonly LatestAndroidVideoFrameReader _encoded;
    private readonly Func<AndroidLatestVideoFrame, IH264VideoDecoder> _create;
    private readonly CancellationTokenSource _stop;
    private readonly SemaphoreSlim _available = new(0, 1);
    private readonly object _gate = new();
    private readonly Task _producer;
    private readonly int _maximumPackets;
    private long _submittedPackets;
    private DecodedStreamFrame? _latest;
    private ExceptionDispatchInfo? _failure;
    private bool _completed;
    private bool _disposed;
    private int _generation = -1;
    private long _replaced;
    private long _staleOutputs;
    private long _lastOutputSequence;
    private long _omittedTimestamps;
    private long _nativeReplaced;
    private IH264VideoDecoder? _active;
    private string _lastDiagnostic = "not-created";
    private Exception? _cleanupFailure;
    private int _coordinationReleased;
    public long ReplacedFrames => Interlocked.Read(ref _replaced);
    public long StaleOutputFrames => Interlocked.Read(ref _staleOutputs);
    public long SubmittedPackets => Interlocked.Read(ref _submittedPackets);
    public long OmittedTimestamps => Interlocked.Read(ref _omittedTimestamps);
    public long NativeReplacedFrames => Interlocked.Read(ref _nativeReplaced) + (Volatile.Read(ref _active)?.ReplacedOutputFrames ?? 0);
    public string DiagnosticState => $"{Volatile.Read(ref _active)?.DiagnosticState ?? _lastDiagnostic}; staleOutputs={StaleOutputFrames}";

    public LatestDecodedVideoFrameReader(LatestAndroidVideoFrameReader encoded,
        Func<AndroidLatestVideoFrame, IH264VideoDecoder> create, CancellationToken token, int maximumPackets = 0)
    {
        _encoded = encoded;
        _create = create;
        ArgumentOutOfRangeException.ThrowIfNegative(maximumPackets);
        _maximumPackets = maximumPackets;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        _producer = Task.Run(() => ProduceAsync(_stop.Token), CancellationToken.None);
    }

    public async ValueTask<DecodedStreamFrame?> ReadAsync(CancellationToken token)
    {
        while (true)
        {
            await _available.WaitAsync(token).ConfigureAwait(false);
            lock (_gate)
            {
                _failure?.Throw();
                if (_latest is not null)
                {
                    DecodedStreamFrame result = _latest;
                    _latest = null;
                    if (_completed) { Signal(); }
                    return result;
                }
                if (_completed) { return null; }
            }
        }
    }

    private async Task ProduceAsync(CancellationToken token)
    {
        IH264VideoDecoder? decoder = null;
        EventHandler? handler = null;
        GenerationOutput? output = null;
        try
        {
            while ((_maximumPackets == 0 || SubmittedPackets < _maximumPackets) &&
                await _encoded.ReadLatestAsync(token).ConfigureAwait(false) is { } packet)
            {
                using (packet)
                {
                    if (packet.Generation != _generation)
                    {
                        ReleaseDecoder();
                        lock (_gate)
                        {
                            _generation = packet.Generation;
                            _lastOutputSequence = 0;
                            _latest?.Dispose();
                            _latest = null;
                        }
                        decoder = _create(packet);
                        Volatile.Write(ref _active, decoder);
                        output = new(this, decoder, packet.Generation, packet.Width, packet.Height);
                        handler = output.OnAvailable;
                        decoder.OutputAvailable += handler;
                    }
                    output!.Remember(packet.PresentationTimeMicroseconds, packet.EstimatedCaptureTimestamp);
                    Interlocked.Increment(ref _submittedPackets);
                    DecodedVideoFrame? frame = await decoder!.DecodePacketAsync(packet.Payload,
                        packet.PresentationTimeMicroseconds, packet.IsKeyFrame, token).ConfigureAwait(false);
                    if (frame is not null) { output.Publish(frame); }
                    output.OnAvailable(null, EventArgs.Empty);
                }
            }
            if (decoder is not null)
            {
                DecodedVideoFrame? last = await decoder.DrainAsync(token).ConfigureAwait(false);
                if (last is not null) { output!.Publish(last); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { Fail(error); }
        finally
        {
            try { ReleaseDecoder(); }
            catch (Exception error) { _cleanupFailure = error; Fail(error); }
            lock (_gate) { _completed = true; if (!_disposed) { Signal(); } }
        }

        void ReleaseDecoder()
        {
            IH264VideoDecoder? owned = decoder;
            decoder = null;
            if (owned is null) { return; }
            _lastDiagnostic = owned.DiagnosticState;
            Volatile.Write(ref _active, null);
            owned.OutputAvailable -= handler;
            Interlocked.Add(ref _nativeReplaced, owned.ReplacedOutputFrames);
            owned.Dispose();
        }
    }

    private void Publish(DecodedStreamFrame frame)
    {
        lock (_gate)
        {
            if (_disposed || _failure is not null || frame.Generation != _generation) { frame.Dispose(); return; }
            if (frame.Frame.OutputSequence > 0)
            {
                // The return path may be descheduled after taking a frame while
                // an output event publishes its successor. Keep the watermark
                // after ReadAsync consumes the slot so that late frames cannot
                // move presentation backwards even when no newer frame waits.
                if (frame.Frame.OutputSequence <= _lastOutputSequence)
                {
                    frame.Dispose();
                    Interlocked.Increment(ref _staleOutputs);
                    Interlocked.Increment(ref _replaced);
                    return;
                }
                _lastOutputSequence = frame.Frame.OutputSequence;
            }
            if (_latest is not null) { _latest.Dispose(); Interlocked.Increment(ref _replaced); }
            _latest = frame;
            Signal();
        }
    }

    private void Fail(Exception error)
    {
        lock (_gate)
        {
            // One terminal failure, not an ever-growing exception chain from repeated
            // output notifications. Cleanup failure is retained separately below.
            _failure ??= ExceptionDispatchInfo.Capture(error);
            if (!_disposed) { Signal(); }
        }
    }

    private void Signal() { if (_available.CurrentCount == 0) { _available.Release(); } }

    public async ValueTask DisposeAsync()
    {
        lock (_gate) { if (_disposed) { return; } _disposed = true; _latest?.Dispose(); _latest = null; }
        await _stop.CancelAsync().ConfigureAwait(false);
        try { await _producer.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); }
        catch (TimeoutException error)
        {
            _ = _producer.ContinueWith(_ => ReleaseCoordination(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw new MediaDecoderException("MEDIA_DECODE_WORKER_STOP_TIMEOUT", "解码输入线程未及时停止，原生资源仍由该线程持有", error);
        }
        ReleaseCoordination();
        if (_cleanupFailure is not null) { throw new MediaDecoderException(MediaReasonCodes.H264CleanupFailed, "解码器清理失败", _cleanupFailure); }
    }

    private void ReleaseCoordination()
    {
        if (Interlocked.Exchange(ref _coordinationReleased, 1) != 0) { return; }
        _available.Dispose();
        _stop.Dispose();
    }

    private sealed class GenerationOutput(LatestDecodedVideoFrameReader owner, IH264VideoDecoder decoder,
        int generation, int width, int height)
    {
        // Metadata only: capacity 128, oldest timestamp removed. A miss means latency unavailable.
        private readonly SortedDictionary<long, long?> _timestamps = [];
        private readonly object _timestampsGate = new();
        public void Remember(long pts, long? captured)
        {
            lock (_timestampsGate)
            {
                _timestamps[pts] = captured;
                while (_timestamps.Count > 128)
                { _timestamps.Remove(_timestamps.First().Key); Interlocked.Increment(ref owner._omittedTimestamps); }
            }
        }
        public void Publish(DecodedVideoFrame frame)
        {
            long? captured;
            lock (_timestampsGate) { _timestamps.Remove(frame.PresentationTimeMicroseconds, out captured); }
            owner.Publish(new(frame, generation, width, height, decoder.BackendName, captured));
        }
        public void OnAvailable(object? sender, EventArgs args)
        {
            try
            {
                DecodedVideoFrame? frame = decoder.TakeOutput();
                if (frame is not null) { Publish(frame); }
            }
            catch (Exception error) { owner.Fail(error); }
        }
    }
}
