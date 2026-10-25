using System.Diagnostics;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Input;

public delegate ValueTask PhoneInputCommandSender(PhoneInputCommand command, CancellationToken cancellationToken);
public delegate ValueTask<PhoneInputWriteResult> PhoneInputResultSender(PhoneInputCommand command, CancellationToken cancellationToken);

public sealed record PhoneInputDispatcherSnapshot(long SentCommands, long ReplacedPointerMoves,
    int ReliableQueueDepth, double LastQueueDelayMilliseconds, bool IsFaulted)
{
    public InputDispatchDiagnostic? Details { get; init; }
}

public class InputDispatchException(string reasonCode, string message, Exception? inner = null) : InvalidOperationException(message, inner)
{
    public string ReasonCode { get; } = reasonCode;
}
public sealed class InputQueueRejectedException(string reasonCode) : InputDispatchException(reasonCode, "Input was not accepted by the bounded queue.");
public sealed class InputCommandSuppressedException(string reasonCode) : InputDispatchException(reasonCode, "Input was suppressed before its control write.");

public sealed class PhoneInputDispatcher : IAsyncDisposable
{
    public const int ReliableQueueCapacity = 64;
    private readonly object _gate = new();
    // 64 reliable entries + one terminal. Reserved termination preserves earlier
    // pointer order; termination can preempt buttons only after its Down is sent.
    // A queued Down keeps its earlier commands. Later Down admission is blocked.
    private readonly LinkedList<QueuedCommand> _reliable = new();
    private readonly SemaphoreSlim _available = new(0, 1);
    private readonly CancellationTokenSource _stopSource = new();
    private readonly PhoneInputResultSender _sender;
    private readonly Action? _abort;
    private readonly Action<InputDispatchDiagnostic>? _onFault;
    private readonly Task _worker, _watchdog;
    private Task? _disposeTask;
    private QueuedCommand? _latestMove, _terminal, _inFlight;
    private long _inFlightAt;
    private PhoneInputCommand? _activePointer;
    private bool _gestureOpen;
    private long _logicalPointer;
    private long _largestSequence;
    private long _sentCommands, _replacedMoves, _rejected, _reserved, _coalesced, _droppedMoves, _terminalAttempts, _terminalWrites;
    private long _discardedReliable, _discardedTerminals;
    private long _acceptedReliable, _acceptedMoves, _lastAcceptedSequence;
    private long _maintenance, _suppressed;
    private string? _lastSuppression;
    private double _lastQueueDelay;
    private InputDispatchException? _fault;
    private string? _failureType;
    private int? _failureHResult;
    private string? _secondaryFailureType;
    private int? _secondaryFailureHResult;
    private bool _pointerUnconfirmed;
    private string _termination = "none";
    private bool _stopping, _disposed, _aborted, _workerCompleted;
    private int _resourcesReleased;
    private Task _cancellation = Task.CompletedTask;

    public PhoneInputDispatcher(PhoneInputCommandSender sender, Action? abortPendingIo = null,
        Action<InputDispatchDiagnostic>? onFault = null)
        : this(Adapt(sender), abortPendingIo, onFault) { }

    public static PhoneInputDispatcher CreateWithResults(PhoneInputResultSender sender, Action? abortPendingIo = null,
        Action<InputDispatchDiagnostic>? onFault = null) => new(sender, abortPendingIo, onFault);

    private static PhoneInputResultSender Adapt(PhoneInputCommandSender sender)
    {
        ArgumentNullException.ThrowIfNull(sender);
        return async (command, token) => { await sender(command, token).ConfigureAwait(false); return new(true); };
    }

    private PhoneInputDispatcher(PhoneInputResultSender sender, Action? abortPendingIo,
        Action<InputDispatchDiagnostic>? onFault)
    {
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _abort = abortPendingIo;
        _onFault = onFault;
        _worker = RunAsync(_stopSource.Token);
        _watchdog = WatchAsync(_stopSource.Token);
    }

    public PhoneInputDispatcherSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return new(_sentCommands, _replacedMoves, _reliable.Count, _lastQueueDelay, _fault is not null)
                {
                    Details = new(ReliableQueueCapacity, _reliable.Count, _terminal is null ? 0 : 1,
                    _rejected, _reserved, _coalesced, _droppedMoves, _terminalAttempts, _terminalWrites, _termination,
                    _pointerUnconfirmed || _activePointer is not null || _inFlight?.Command.Kind == PhoneInputCommandKind.PointerDown,
                    _inFlight?.Command.Kind.ToString(), _fault?.ReasonCode, _failureType, _failureHResult, _workerCompleted)
                    {
                        SecondaryFailureType = _secondaryFailureType,
                        SecondaryFailureHResult = _secondaryFailureHResult,
                        DiscardedReliable = _discardedReliable,
                        DiscardedTerminals = _discardedTerminals,
                        AcceptedReliable = _acceptedReliable,
                        AcceptedMoves = _acceptedMoves,
                        LastAcceptedSequence = _lastAcceptedSequence,
                        MaintenanceCompletions = _maintenance,
                        SuppressedCommands = _suppressed,
                        LastSuppressionReason = _lastSuppression
                    }
                };
            }
        }
    }

    public ValueTask EnqueueAsync(PhoneInputCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task completion;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_fault is not null) { return ValueTask.FromException(_fault); }
            if (_stopping) { return Reject("PHONE_INPUT_STOPPING"); }
            _largestSequence = Math.Max(_largestSequence, command.Sequence);
            bool terminal = IsTerminal(command.Kind);
            if (command.Kind == PhoneInputCommandKind.PointerMove)
            {
                if (!_gestureOpen || command.PointerId != _logicalPointer) { _droppedMoves++; return ValueTask.CompletedTask; }
                if (_latestMove is not null) { _replacedMoves++; }
                _latestMove = new(command, null);
                _acceptedMoves++; _lastAcceptedSequence = command.Sequence;
                TryWake();
                return ValueTask.CompletedTask;
            }
            if (command.Kind == PhoneInputCommandKind.PointerDown && (_gestureOpen || _terminal is not null))
            { return Reject("PHONE_INPUT_GESTURE_BUSY"); }
            if (terminal && _gestureOpen && command.PointerId != _logicalPointer) { return Reject("PHONE_INPUT_POINTER_MISMATCH"); }
            if (terminal && _terminal is not null)
            {
                if (_terminal.Command.PointerId != command.PointerId) { return Reject("PHONE_INPUT_TERMINAL_BUSY"); }
                _coalesced++;
                _acceptedReliable++; _lastAcceptedSequence = command.Sequence;
                if (command.Kind == PhoneInputCommandKind.PointerCancel && _terminal.Command.Kind != PhoneInputCommandKind.PointerCancel)
                { _terminal = _terminal with { Command = command }; }
                return WaitFor(_terminal.Completion!.Task, cancellationToken);
            }
            if (!terminal && _reliable.Count >= ReliableQueueCapacity) { return Reject("PHONE_INPUT_QUEUE_FULL"); }
            QueuedCommand queued = new(command, new(TaskCreationOptions.RunContinuationsAsynchronously));
            if (terminal)
            {
                if (_latestMove is not null)
                {
                    if (_reliable.Count < ReliableQueueCapacity - 1) { _reliable.AddLast(_latestMove); }
                    else { _replacedMoves++; _droppedMoves++; }
                    _latestMove = null;
                }
                _gestureOpen = false;
                if (_reliable.Count == ReliableQueueCapacity) { _terminal = queued; _reserved++; }
                else { _reliable.AddLast(queued); }
            }
            else
            {
                _reliable.AddLast(queued);
                if (command.Kind == PhoneInputCommandKind.PointerDown) { _gestureOpen = true; _logicalPointer = command.PointerId; }
            }
            completion = queued.Completion!.Task;
            _acceptedReliable++; _lastAcceptedSequence = command.Sequence;
            TryWake();
        }
        return WaitFor(completion, cancellationToken);
    }

    private ValueTask Reject(string reason) { _rejected++; return ValueTask.FromException(new InputQueueRejectedException(reason)); }
    private static ValueTask WaitFor(Task completion, CancellationToken token) => new(token.CanBeCanceled ? completion.WaitAsync(token) : completion);
    private static bool IsTerminal(PhoneInputCommandKind kind) => kind is PhoneInputCommandKind.PointerUp or PhoneInputCommandKind.PointerCancel;
    private static bool IsPointer(PhoneInputCommandKind kind) => kind is PhoneInputCommandKind.PointerDown or PhoneInputCommandKind.PointerMove or PhoneInputCommandKind.PointerUp or PhoneInputCommandKind.PointerCancel;

    public ValueTask DisposeAsync() { lock (_gate) { return new(_disposeTask ??= DisposeCoreAsync()); } }
    private async Task DisposeCoreAsync()
    {
        lock (_gate)
        {
            _disposed = true; _stopping = true; _gestureOpen = false;
            DropPending(cancel: true);
            TryWake();
        }
        try { await _worker.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            RequestCancellation();
            try { await _worker.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
            catch (TimeoutException) { Fail(new InputDispatchException("PHONE_INPUT_SHUTDOWN_TIMEOUT", "Owned sender did not finish shutdown."), abort: true); }
        }
        RequestCancellation();
        Task ownership = Task.WhenAll(_worker, _watchdog, _cancellation);
        if (ownership.IsCompleted) { CompleteOwnership(ownership); }
        else
        {
            // Lifecycle only: a non-cooperative sender retains these objects until it finishes.
            _ = ownership.ContinueWith(CompleteOwnership, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            while (true)
            {
                await _available.WaitAsync(token).ConfigureAwait(false);
                while (TryTake(out QueuedCommand queued))
                {
                    try
                    {
                        using CancellationTokenSource? terminal = queued.ShutdownTerminal ? new(TimeSpan.FromMilliseconds(500)) : null;
                        PhoneInputWriteResult result = await _sender(queued.Command, terminal?.Token ?? token).ConfigureAwait(false);
                        lock (_gate)
                        {
                            if (result.Written) { _sentCommands++; }
                            if (queued.Command.Kind == PhoneInputCommandKind.SynchronizeDisplayGeometry) { _maintenance++; }
                            if (result.SuppressionReason is not null) { _suppressed++; _lastSuppression = result.SuppressionReason; }
                            _lastQueueDelay = Math.Max(0, (DateTimeOffset.UtcNow - queued.Command.CreatedAt).TotalMilliseconds);
                            if (!_aborted)
                            {
                                if (result.PointerTerminated)
                                {
                                    _activePointer = null; _termination = "sent-by-geometry";
                                    // A later accepted Down owns the logical gesture and its latest MOVE.
                                    if (queued.Command.Kind != PhoneInputCommandKind.PointerDown &&
                                        !_reliable.Any(item => item.Command.Kind == PhoneInputCommandKind.PointerDown))
                                    {
                                        _gestureOpen = false;
                                        if (_latestMove is not null) { _latestMove = null; _droppedMoves++; }
                                    }
                                }
                                if (result.Written)
                                {
                                    if (queued.Command.Kind == PhoneInputCommandKind.PointerDown) { _activePointer = queued.Command; _termination = "active"; }
                                    else if (queued.Command.Kind == PhoneInputCommandKind.PointerMove && _activePointer is not null) { _activePointer = queued.Command; }
                                    else if (IsTerminal(queued.Command.Kind)) { _activePointer = null; _termination = "sent"; }
                                }
                                else if (IsTerminal(queued.Command.Kind))
                                {
                                    _termination = _activePointer is null ? "already-terminated" : "active";
                                    if (_activePointer is { } active && _terminal is null &&
                                        !_reliable.Any(item => IsTerminal(item.Command.Kind) || item.Command.Kind == PhoneInputCommandKind.PointerDown))
                                    { _gestureOpen = true; _logicalPointer = active.PointerId; }
                                }
                                if (result.SuppressionReason is not null && queued.Command.Kind == PhoneInputCommandKind.PointerDown &&
                                    !_reliable.Any(item => item.Command.Kind == PhoneInputCommandKind.PointerDown))
                                {
                                    _gestureOpen = false;
                                    if (_latestMove is not null) { _latestMove = null; _droppedMoves++; }
                                    if (_activePointer is null) { _termination = "suppressed"; }
                                }
                            }
                            if (result.Written && IsTerminal(queued.Command.Kind)) { _terminalWrites++; }
                            _inFlight = null;
                        }
                        if (result.SuppressionReason is { } reason) { queued.Completion?.TrySetException(new InputCommandSuppressedException(reason)); }
                        else { queued.Completion?.TrySetResult(); }
                    }
                    catch (Exception error)
                    {
                        InputDispatchException failure = error as InputDispatchException ?? new(
                            queued.ShutdownTerminal ? "PHONE_INPUT_TERMINATION_FAILED" : "PHONE_INPUT_SEND_FAILED", "Input transport write failed.", error);
                        Fail(failure, abort: true);
                        return;
                    }
                }
                lock (_gate) { if (_stopping || _fault is not null) { return; } }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { lock (_gate) { _inFlight = null; _workerCompleted = true; } }
    }

    private bool TryTake(out QueuedCommand queued)
    {
        lock (_gate)
        {
            queued = null!;
            if (_aborted || _fault is not null) { return false; }
            if (_stopping)
            {
                if (_activePointer is not { } active) { return false; }
                queued = new(active with
                {
                    Sequence = _largestSequence < long.MaxValue ? _largestSequence + 1 : long.MaxValue,
                    Kind = PhoneInputCommandKind.PointerCancel,
                    CreatedAt = DateTimeOffset.UtcNow
                }, null, true);
            }
            else if (_terminal is not null)
            {
                LinkedListNode<QueuedCommand>? pointer = _reliable.First;
                if (_activePointer is not null)
                { while (pointer is not null && !IsPointer(pointer.Value.Command.Kind)) { pointer = pointer.Next; } }
                if (pointer is not null) { queued = pointer.Value; _reliable.Remove(pointer); }
                else { queued = _terminal; _terminal = null; }
            }
            else if (_reliable.First is { } first) { queued = first.Value; _reliable.RemoveFirst(); }
            else if (_latestMove is not null) { queued = _latestMove; _latestMove = null; }
            else { return false; }
            _inFlight = queued;
            _inFlightAt = Stopwatch.GetTimestamp();
            if (IsTerminal(queued.Command.Kind)) { _terminalAttempts++; _termination = "sending"; }
            return true;
        }
    }

    private async Task WatchAsync(CancellationToken token)
    {
        try
        {
            using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(100));
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                QueuedCommand? expired;
                lock (_gate)
                {
                    if (_workerCompleted) { return; }
                    TimeSpan budget = _inFlight?.ShutdownTerminal == true ? TimeSpan.FromMilliseconds(500) :
                        _inFlight is { } current && IsPointer(current.Command.Kind) ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(5);
                    expired = _inFlight is not null && Stopwatch.GetElapsedTime(_inFlightAt) >= budget ? _inFlight : null;
                }
                if (expired is not null && Fail(new InputDispatchException(expired.ShutdownTerminal ? "PHONE_INPUT_TERMINATION_TIMEOUT" : "PHONE_INPUT_SEND_TIMEOUT",
                    "Owned input write exceeded its deadline."), abort: true, expected: expired)) { return; }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private bool Fail(InputDispatchException error, bool abort, QueuedCommand? expected = null)
    {
        bool notify;
        bool shouldAbort;
        lock (_gate)
        {
            if (expected is not null && !ReferenceEquals(expected, _inFlight)) { return false; }
            if (expected is not null && _fault is not null) { return true; }
            notify = _fault is null;
            _fault ??= error;
            if (notify)
            {
                _failureType = error.InnerException?.GetType().FullName ?? error.GetType().FullName;
                _failureHResult = error.InnerException?.HResult ?? error.HResult;
            }
            else { _secondaryFailureType = error.InnerException?.GetType().FullName ?? error.GetType().FullName; _secondaryFailureHResult = error.InnerException?.HResult ?? error.HResult; }
            if (_activePointer is not null || _inFlight?.Command.Kind == PhoneInputCommandKind.PointerDown || _termination == "sending")
            { _termination = "unconfirmed"; _pointerUnconfirmed = true; }
            _activePointer = null; _gestureOpen = false; _stopping = true;
            _inFlight?.Completion?.TrySetException(_fault);
            DropPending(cancel: false);
            shouldAbort = abort && !_aborted;
            if (abort) { _aborted = true; }
        }
        if (shouldAbort)
        {
            try { _abort?.Invoke(); }
            catch (Exception cleanup) { lock (_gate) { _secondaryFailureType = cleanup.GetType().FullName; _secondaryFailureHResult = cleanup.HResult; } }
        }
        RequestCancellation();
        if (notify)
        {
            try { _onFault?.Invoke(Snapshot.Details!); }
            catch (Exception) { /* Observers cannot change ownership or replace the original transport fault. */ }
        }
        return true;
    }

    private void DropPending(bool cancel)
    {
        if (_latestMove is not null) { _latestMove = null; _droppedMoves++; }
        foreach (QueuedCommand pending in _reliable)
        {
            if (pending.Command.Kind == PhoneInputCommandKind.PointerMove) { _droppedMoves++; }
            else { _discardedReliable++; }
            if (IsTerminal(pending.Command.Kind)) { _discardedTerminals++; }
            if (cancel) { pending.Completion?.TrySetCanceled(); } else { pending.Completion?.TrySetException(_fault!); }
        }
        _reliable.Clear();
        if (_terminal is not null) { _discardedTerminals++; }
        if (cancel) { _terminal?.Completion?.TrySetCanceled(); } else { _terminal?.Completion?.TrySetException(_fault!); }
        _terminal = null;
    }
    private void RequestCancellation() { lock (_gate) { if (!_stopSource.IsCancellationRequested) { _cancellation = _stopSource.CancelAsync(); } } }
    private void CompleteOwnership(Task ownership)
    {
        if (ownership.Exception is { } error)
        {
            lock (_gate)
            {
                _fault ??= new InputDispatchException("PHONE_INPUT_CANCELLATION_FAILED", "Input cancellation callback failed.", error);
                _secondaryFailureType = error.GetBaseException().GetType().FullName;
                _secondaryFailureHResult = error.GetBaseException().HResult;
            }
        }
        ReleaseResources();
    }
    private void ReleaseResources()
    { if (Interlocked.Exchange(ref _resourcesReleased, 1) != 0) { return; } _stopSource.Dispose(); _available.Dispose(); }
    private void TryWake()
    {
        try { _available.Release(); }
        catch (SemaphoreFullException) { }
        catch (ObjectDisposedException) { }
    }
    private sealed record QueuedCommand(PhoneInputCommand Command, TaskCompletionSource? Completion, bool ShutdownTerminal = false);
}
