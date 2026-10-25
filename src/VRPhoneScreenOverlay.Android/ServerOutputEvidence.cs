using System.Text;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Android;

// Two bounded readers share one tail (80 lines, 16 KiB UTF-8). Oldest lines are
// evicted. Each reader retains at most 1024 characters of its current line.
// Error groups come only from the fixed classifier below, never from raw output.
internal sealed class ServerOutputEvidence
{
    internal const int MaximumLineCharacters = 1024;
    internal const int MaximumTailBytes = 16 * 1024;
    private const int _maximumLines = 80;
    private static readonly string[] _knownExceptionTypes =
    [
        "SecurityException", "IllegalStateException", "IllegalArgumentException", "NullPointerException",
        "NoSuchMethodException", "NoSuchFieldException", "InvocationTargetException", "IOException",
        "EOFException", "SocketException", "DeadObjectException", "CodecException", "RuntimeException",
        "OutOfMemoryError", "AssertionError", "LinkageError"
    ];
    private readonly object _gate = new();
    private readonly Queue<(string Text, int Bytes)> _tail = new(_maximumLines);
    private readonly Dictionary<string, ServerOutputError> _errors = [];
    private long _revision, _characters, _lines, _stdout, _stderr, _truncated, _omitted;
    private int _tailBytes;
    private bool _stdoutCompleted, _stderrCompleted;
    private string? _stdoutFailure, _stderrFailure;

    internal ServerOutputDiagnostic Snapshot
    {
        get
        {
            lock (_gate)
            {
                return new(_revision, DateTimeOffset.UtcNow, _characters, _lines, _stdout, _stderr,
                    _truncated, _omitted, _tailBytes, MaximumTailBytes, MaximumLineCharacters,
                    _stdoutCompleted, _stderrCompleted, _stdoutFailure, _stderrFailure,
                    _errors.Values.OrderBy(error => error.Category, StringComparer.Ordinal).ToArray());
            }
        }
    }

    internal string GetTail()
    {
        lock (_gate)
        {
            return (_omitted + _truncated > 0 ? $"tail-truncated={_omitted + _truncated}; " : "") +
                string.Join(" | ", _tail.Select(line => line.Text));
        }
    }

    internal async Task ReadAsync(StreamReader reader, bool standardError, CancellationToken token)
    {
        char[] buffer = new char[4096];
        StringBuilder line = new(MaximumLineCharacters);
        bool clipped = false;
        string? failure = null;
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
            {
                lock (_gate) { _characters += count; _revision++; }
                for (int index = 0; index < count; index++)
                {
                    char character = buffer[index];
                    if (character == '\n')
                    {
                        AddLine(line.ToString(), clipped, standardError);
                        line.Clear(); clipped = false;
                    }
                    else if (line.Length < MaximumLineCharacters) { line.Append(character); }
                    else { clipped = true; }
                }
            }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // Cancellation/closed pipes are visible as incomplete capture, never as proof of no rejection.
            failure = token.IsCancellationRequested ? "capture-cancelled" : error.GetType().FullName;
        }
        finally
        {
            if (line.Length > 0 || clipped) { AddLine(line.ToString(), clipped, standardError); }
            lock (_gate)
            {
                if (standardError) { _stderrCompleted = failure is null; _stderrFailure = failure; }
                else { _stdoutCompleted = failure is null; _stdoutFailure = failure; }
                _revision++;
            }
        }
    }

    private void AddLine(string line, bool clipped, bool standardError)
    {
        lock (_gate)
        {
            _lines++; _revision++;
            if (standardError) { _stderr++; } else { _stdout++; }
            if (clipped) { _truncated++; }
            string? category = Classify(line);
            if (category is not null)
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                _errors[category] = _errors.TryGetValue(category, out ServerOutputError? previous)
                    ? previous with { ObservedLines = previous.ObservedLines + 1, LastAt = now }
                    : new(category, 1, now, now);
                ServerOutputError updated = _errors[category];
                _errors[category] = updated with
                {
                    KnownExceptionTypes = updated.KnownExceptionTypes.Union(
                        _knownExceptionTypes.Where(type => line.Contains(type, StringComparison.Ordinal)))
                        .Order(StringComparer.Ordinal).ToArray()
                };
            }
            string trimmed = line.Trim();
            if (trimmed.Length == 0) { return; }
            int bytes = Encoding.UTF8.GetByteCount(trimmed);
            while (_tail.Count > 0 && (_tail.Count >= _maximumLines || _tailBytes + bytes > MaximumTailBytes))
            { _tailBytes -= _tail.Dequeue().Bytes; _omitted++; }
            _tail.Enqueue((trimmed, bytes));
            _tailBytes += bytes;
        }
    }

    internal static string? Classify(string line)
    {
        if (line.Contains("INJECT_EVENTS permission", StringComparison.Ordinal) &&
            (line.Contains("requires", StringComparison.OrdinalIgnoreCase) ||
             line.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
             line.Contains("SecurityException", StringComparison.Ordinal))) { return "input-permission-denied"; }
        if (line.Contains("SecurityException", StringComparison.Ordinal)) { return "android-security-exception"; }
        if (line.Contains("Control protocol error", StringComparison.Ordinal)) { return "control-protocol-error"; }
        if (line.Contains("Controller error", StringComparison.Ordinal)) { return "controller-error"; }
        if (line.Contains("Could not invoke method", StringComparison.Ordinal)) { return "reflection-error"; }
        if (line.Contains("Input events are not supported", StringComparison.Ordinal)) { return "input-not-supported"; }
        if (line.Contains("MediaCodec", StringComparison.Ordinal) && line.Contains("Exception", StringComparison.Ordinal)) { return "codec-exception"; }
        if (_knownExceptionTypes.Any(type => line.Contains(type, StringComparison.Ordinal))) { return "java-exception"; }
        if (line.Contains("[server] ERROR:", StringComparison.Ordinal) || line.Contains("[ERROR]", StringComparison.Ordinal) ||
            line.Contains("Exception in thread", StringComparison.Ordinal)) { return "other-server-error"; }
        if (line.Contains("[server] WARN:", StringComparison.Ordinal)) { return "server-warning"; }
        return null;
    }
}
