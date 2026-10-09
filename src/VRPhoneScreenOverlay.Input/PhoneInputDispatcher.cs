using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Input;

public delegate ValueTask PhoneInputCommandSender(
    PhoneInputCommand command,
    CancellationToken cancellationToken);

public sealed record PhoneInputDispatcherSnapshot(
    long SentCommands,
    long ReplacedPointerMoves,
    int ReliableQueueDepth,
    double LastQueueDelayMilliseconds,
    bool IsFaulted);

public sealed class PhoneInputDispatcher : IAsyncDisposable
{
    public const int ReliableQueueCapacity = 64;

    private readonly object _gate = new();
    private readonly Queue<QueuedCommand> _reliable = new(ReliableQueueCapacity);
    private readonly SemaphoreSlim _available = new(0, 1);
    private readonly CancellationTokenSource _stopSource = new();
    private readonly PhoneInputCommandSender _sender;
    private readonly Task _worker;
    private QueuedCommand? _latestMove;
    private PhoneInputCommand? _activePointer;
    private long _sentCommands;
    private long _replacedMoves;
    private double _lastQueueDelayMilliseconds;
    private Exception? _fault;
    private bool _stopping;
    private bool _disposed;

    public PhoneInputDispatcher(PhoneInputCommandSender sender)
    {
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _worker = RunAsync(_stopSource.Token);
    }

    public PhoneInputDispatcherSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return new PhoneInputDispatcherSnapshot(
                    _sentCommands,
                    _replacedMoves,
                    _reliable.Count,
                    _lastQueueDelayMilliseconds,
                    _fault is not null);
            }
        }
    }

    public ValueTask EnqueueAsync(
        PhoneInputCommand command,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        QueuedCommand queued = new(
            command,
            command.Kind == PhoneInputCommandKind.PointerMove
                ? null
                : new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        bool wake;
        lock (_gate)
        {
            if (_fault is not null)
            {
                return ValueTask.FromException(_fault);
            }

            if (_stopping)
            {
                return ValueTask.FromException(new InvalidOperationException(
                    "The phone input dispatcher is stopping."));
            }

            wake = _reliable.Count == 0 && _latestMove is null;
            if (command.Kind == PhoneInputCommandKind.PointerMove)
            {
                if (_latestMove is not null)
                {
                    _replacedMoves++;
                }

                _latestMove = queued;
            }
            else
            {
                if ((command.Kind is PhoneInputCommandKind.PointerUp or
                    PhoneInputCommandKind.PointerCancel) &&
                    _latestMove is not null)
                {
                    if (_reliable.Count < ReliableQueueCapacity - 1)
                    {
                        _reliable.Enqueue(_latestMove);
                    }
                    else
                    {
                        _replacedMoves++;
                    }

                    _latestMove = null;
                }

                if (_reliable.Count == ReliableQueueCapacity)
                {
                    return ValueTask.FromException(new InvalidOperationException(
                        "The reliable phone input queue is full."));
                }

                _reliable.Enqueue(queued);
            }
        }

        if (wake)
        {
            TryWake();
        }

        return queued.Completion is null
            ? ValueTask.CompletedTask
            : new ValueTask(queued.Completion.Task.WaitAsync(cancellationToken));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_gate)
        {
            _stopping = true;
            _latestMove = null;
            while (_reliable.Count > 0)
            {
                QueuedCommand pending = _reliable.Dequeue();
                pending.Completion?.TrySetCanceled();
            }

            if (_activePointer is PhoneInputCommand active)
            {
                PhoneInputCommand cancel = active with
                {
                    Sequence = active.Sequence + 1,
                    Kind = PhoneInputCommandKind.PointerCancel,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                _reliable.Enqueue(new QueuedCommand(cancel, null));
            }
        }

        TryWake();
        Task completed = await Task.WhenAny(_worker, Task.Delay(TimeSpan.FromSeconds(1)))
            .ConfigureAwait(false);
        if (completed != _worker)
        {
            await _stopSource.CancelAsync().ConfigureAwait(false);
        }

        try
        {
            await _worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stopSource.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
        }
        finally
        {
            _stopSource.Dispose();
            _available.Dispose();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await _available.WaitAsync(cancellationToken).ConfigureAwait(false);
                while (TryTake(out QueuedCommand queued))
                {
                    try
                    {
                        await _sender(queued.Command, cancellationToken).ConfigureAwait(false);
                        MarkSent(queued.Command);
                        queued.Completion?.TrySetResult();
                    }
                    catch (Exception exception)
                    {
                        queued.Completion?.TrySetException(exception);
                        Fault(exception);
                        throw;
                    }
                }

                lock (_gate)
                {
                    if (_stopping && _reliable.Count == 0 && _latestMove is null)
                    {
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private bool TryTake(out QueuedCommand queued)
    {
        lock (_gate)
        {
            if (_reliable.Count > 0)
            {
                queued = _reliable.Dequeue();
                return true;
            }

            if (_latestMove is not null)
            {
                queued = _latestMove;
                _latestMove = null;
                return true;
            }

            queued = null!;
            return false;
        }
    }

    private void MarkSent(PhoneInputCommand command)
    {
        lock (_gate)
        {
            _sentCommands++;
            _lastQueueDelayMilliseconds = Math.Max(
                0,
                (DateTimeOffset.UtcNow - command.CreatedAt).TotalMilliseconds);
            _activePointer = command.Kind switch
            {
                PhoneInputCommandKind.PointerDown or PhoneInputCommandKind.PointerMove => command,
                PhoneInputCommandKind.PointerUp or PhoneInputCommandKind.PointerCancel => null,
                _ => _activePointer,
            };
        }
    }

    private void Fault(Exception exception)
    {
        lock (_gate)
        {
            _fault = exception;
            _stopping = true;
            _latestMove = null;
            while (_reliable.Count > 0)
            {
                _reliable.Dequeue().Completion?.TrySetException(exception);
            }
        }
    }

    private void TryWake()
    {
        try
        {
            _available.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    private sealed record QueuedCommand(
        PhoneInputCommand Command,
        TaskCompletionSource? Completion);
}
