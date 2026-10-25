using System.Runtime.ExceptionServices;

namespace VRPhoneScreenOverlay.Media;

/// <summary>One input producer; at most 64 native input permits, never an unbounded event queue.</summary>
internal sealed class MftAsyncFlow : IDisposable
{
    private const int _capacity = 64;
    private readonly SemaphoreSlim _input = new(0, _capacity);
    private readonly object _gate = new();
    private ExceptionDispatchInfo? _failure;
    private TaskCompletionSource? _drained;
    private bool _closed;
    private bool _draining;
    private bool _drainComplete;
    private long _inputEvents;
    private long _inputConsumed;
    public long InputEvents => Interlocked.Read(ref _inputEvents);
    public long InputConsumed => Interlocked.Read(ref _inputConsumed);

    public void GrantInput(uint streamId)
    {
        lock (_gate)
        {
            if (_closed || _draining) { return; }
            if (streamId != 0) { throw Error("MEDIA_MFT_INPUT_STREAM_UNSUPPORTED", "解码器请求了不支持的输入流"); }
            if (_input.CurrentCount >= _capacity) { throw Error("MEDIA_MFT_INPUT_PERMIT_LIMIT", "解码器输入许可超过上限"); }
            _input.Release();
            Interlocked.Increment(ref _inputEvents);
        }
    }

    public async ValueTask WaitForInputAsync(TimeSpan timeout, CancellationToken token)
    {
        ThrowIfUnavailable();
        if (!await _input.WaitAsync(timeout, token).ConfigureAwait(false))
        { throw Error("MEDIA_MFT_INPUT_TIMEOUT", "等待解码器输入许可超时"); }
        ThrowIfUnavailable();
        Interlocked.Increment(ref _inputConsumed);
    }

    public void BeginDrain()
    {
        lock (_gate)
        {
            ThrowIfUnavailable();
            _draining = true;
            _drainComplete = false;
            while (_input.Wait(0)) { }
            _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void CompleteDrain()
    {
        lock (_gate)
        {
            if (!_closed && _draining) { _drainComplete = true; _drained?.TrySetResult(); }
        }
    }

    public async ValueTask WaitForDrainAsync(TimeSpan timeout, CancellationToken token)
    {
        Task drain;
        lock (_gate) { drain = _drained?.Task ?? throw new InvalidOperationException("Drain was not started."); }
        try { await drain.WaitAsync(timeout, token).ConfigureAwait(false); }
        catch (TimeoutException error) { throw new MediaDecoderException("MEDIA_MFT_DRAIN_TIMEOUT", "等待解码器排空超时", error); }
        ThrowIfFailed();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (!_drainComplete) { throw Error("MEDIA_MFT_DRAIN_INCOMPLETE", "解码器没有确认排空完成"); }
        }
    }

    public void ResetAfterFlush()
    {
        lock (_gate)
        {
            ThrowIfFailed();
            ObjectDisposedException.ThrowIf(_closed, this);
            while (_input.Wait(0)) { }
            _drained?.TrySetException(Error("MEDIA_MFT_DRAIN_FLUSHED", "解码器排空被清空操作中断"));
            _drained = null;
            _draining = false;
        }
    }

    public void Fail(Exception error)
    {
        lock (_gate)
        {
            _failure ??= ExceptionDispatchInfo.Capture(error);
            CloseCore();
        }
    }

    public void Close()
    {
        lock (_gate) { CloseCore(); }
    }

    private void CloseCore()
    {
        if (_closed) { return; }
        _closed = true;
        if (_input.CurrentCount == 0) { _input.Release(); } // Wake the single producer.
        _drained?.TrySetResult();
    }

    private void ThrowIfUnavailable()
    {
        lock (_gate)
        {
            ThrowIfFailed();
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_draining) { throw Error("MEDIA_MFT_INPUT_DURING_DRAIN", "排空期间不能提交解码输入"); }
        }
    }

    public void ThrowIfFailed() { lock (_gate) { _failure?.Throw(); } }
    public void Dispose() => _input.Dispose(); // Owner closes and joins its producer before disposal.
    private static MediaDecoderException Error(string reason, string message) => new(reason, message);
}
