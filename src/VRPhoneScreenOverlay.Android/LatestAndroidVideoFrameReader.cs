using System.Runtime.ExceptionServices;

namespace VRPhoneScreenOverlay.Android;

public sealed class LatestAndroidVideoFrameReader : IAsyncDisposable
{
    private const int _encodedFrameCapacity = 32;
    private readonly IAndroidVideoSession _session;
    private readonly CancellationTokenSource _stopSource;
    private readonly SemaphoreSlim _available = new(0, 1);
    private readonly object _gate = new();
    private readonly Task _producer;
    private readonly Queue<AndroidLatestVideoFrame> _frames = new(_encodedFrameCapacity);
    private ExceptionDispatchInfo? _failure;
    private bool _completed;
    private bool _disposed;
    private long _droppedFrames;
    private long _encodedPayloadBytes;

    public LatestAndroidVideoFrameReader(
        IAndroidVideoSession session,
        CancellationToken cancellationToken)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _stopSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _producer = Task.Run(() => ProduceAsync(_stopSource.Token), CancellationToken.None);
    }

    public long DroppedFrames => Interlocked.Read(ref _droppedFrames);

    public long EncodedPayloadBytes => Interlocked.Read(ref _encodedPayloadBytes);

    public async ValueTask<AndroidLatestVideoFrame?> ReadLatestAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        while (true)
        {
            await _available.WaitAsync(cancellationToken).ConfigureAwait(false);
            AndroidLatestVideoFrame? frame = null;
            ExceptionDispatchInfo? failure;
            bool completed;
            lock (_gate)
            {
                if (_frames.Count > 0)
                {
                    frame = _frames.Dequeue();
                    if (_frames.Count > 0 || _completed || _failure is not null)
                    {
                        SignalAvailable();
                    }
                }
                failure = _failure;
                completed = _completed;
            }

            if (frame is not null)
            {
                return frame;
            }

            failure?.Throw();
            if (completed)
            {
                return null;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _stopSource.CancelAsync().ConfigureAwait(false);
        try
        {
            await _producer.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stopSource.IsCancellationRequested)
        {
        }

        lock (_gate)
        {
            ClearFrames();
        }

        _available.Dispose();
        _stopSource.Dispose();
    }

    private async Task ProduceAsync(CancellationToken cancellationToken)
    {
        int width = 0;
        int height = 0;
        int generation = 0;
        byte[]? configuration = null;
        bool waitingForKeyFrame = true;
        bool resynchronizing = false;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AndroidVideoStreamItem item = await _session.ReadNextAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (item.Kind == AndroidVideoStreamItemKind.Session)
                {
                    width = item.Width;
                    height = item.Height;
                    generation++;
                    configuration = null;
                    waitingForKeyFrame = true;
                    resynchronizing = false;
                    item.Dispose();
                    continue;
                }

                if (item.Kind == AndroidVideoStreamItemKind.Configuration)
                {
                    configuration = item.Payload.ToArray();
                    item.Dispose();
                    continue;
                }

                Interlocked.Add(ref _encodedPayloadBytes, item.Payload.Length);

                if (width < 1 || height < 1 || configuration is null)
                {
                    item.Dispose();
                    continue;
                }

                if (waitingForKeyFrame && !item.IsKeyFrame)
                {
                    item.Dispose();
                    Interlocked.Increment(ref _droppedFrames);
                    continue;
                }

                if (waitingForKeyFrame)
                {
                    if (resynchronizing)
                    {
                        generation++;
                    }

                    waitingForKeyFrame = false;
                    resynchronizing = false;
                }

                try
                {
                    if (!TryEnqueue(item, width, height, configuration, ref generation))
                    {
                        waitingForKeyFrame = true;
                        resynchronizing = true;
                    }
                    item = null!;
                }
                finally
                {
                    item?.Dispose();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                _failure = ExceptionDispatchInfo.Capture(exception);
            }
        }
        finally
        {
            lock (_gate)
            {
                _completed = true;
                SignalAvailable();
            }
        }
    }

    // Owns each accepted packet. Capacity is 32 encoded frames; overflow discards
    // the old dependency chain. A keyframe at the boundary can restart immediately.
    private bool TryEnqueue(AndroidVideoStreamItem item, int width, int height,
        byte[] configuration, ref int generation)
    {
        lock (_gate)
        {
            if (_frames.Count >= _encodedFrameCapacity)
            {
                int dropped = _frames.Count;
                ClearFrames();
                if (!item.IsKeyFrame)
                {
                    item.Dispose();
                    Interlocked.Add(ref _droppedFrames, dropped + 1);
                    return false;
                }
                generation++;
                Interlocked.Add(ref _droppedFrames, dropped);
            }

#pragma warning disable CA2000 // Ownership transfers to the bounded queue and then the consumer.
            _frames.Enqueue(new AndroidLatestVideoFrame(generation, width, height, configuration, item));
#pragma warning restore CA2000
            SignalAvailable();
            return true;
        }
    }

    private void ClearFrames()
    {
        while (_frames.TryDequeue(out AndroidLatestVideoFrame? frame))
        {
            frame.Dispose();
        }
    }

    private void SignalAvailable()
    {
        if (_available.CurrentCount == 0)
        {
            _available.Release();
        }
    }
}

public sealed class AndroidLatestVideoFrame(
    int generation,
    int width,
    int height,
    byte[] configuration,
    AndroidVideoStreamItem item) : IDisposable
{
    private AndroidVideoStreamItem? _item = item;

    public int Generation { get; } = generation;

    public int Width { get; } = width;

    public int Height { get; } = height;

    public ReadOnlyMemory<byte> Configuration { get; } = configuration;

    public long PresentationTimeMicroseconds =>
        (_item ?? throw new ObjectDisposedException(nameof(AndroidLatestVideoFrame)))
        .PresentationTimeMicroseconds ?? 0;

    public long? EstimatedCaptureTimestamp =>
        (_item ?? throw new ObjectDisposedException(nameof(AndroidLatestVideoFrame)))
        .EstimatedCaptureTimestamp;

    public bool IsKeyFrame =>
        (_item ?? throw new ObjectDisposedException(nameof(AndroidLatestVideoFrame))).IsKeyFrame;

    public ReadOnlyMemory<byte> Payload =>
        (_item ?? throw new ObjectDisposedException(nameof(AndroidLatestVideoFrame))).Payload;

    public void Dispose()
    {
        AndroidVideoStreamItem? owned = Interlocked.Exchange(ref _item, null);
        owned?.Dispose();
    }
}
