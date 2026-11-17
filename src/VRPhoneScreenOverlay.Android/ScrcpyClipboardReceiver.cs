using System.Buffers.Binary;
using System.Text;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Android;

/// <summary>Reads only change notifications. Never requests the phone's existing clipboard.</summary>
internal sealed record ClipboardConnectionEnd(string ReasonCode, DateTimeOffset At, long FailureCount);

internal sealed class ScrcpyClipboardReceiver : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _reader;
    public ScrcpyClipboardReceiver(Stream stream, IPhoneClipboardSink sink, Action<ClipboardConnectionEnd>? observe = null)
    {
        _reader = Task.Run(async () =>
        {
            sink.SetConnection(true);
            string reason = "CLIPBOARD_STOPPED";
            try { await ReadAsync(stream, sink, _stop.Token).ConfigureAwait(false); }
            catch (Exception exception) when (exception is IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException or DecoderFallbackException)
            {
                reason = _stop.IsCancellationRequested ? "CLIPBOARD_STOPPED" : exception switch
                {
                    EndOfStreamException => "CLIPBOARD_STREAM_ENDED",
                    DecoderFallbackException => "CLIPBOARD_UTF8_INVALID",
                    InvalidDataException data when data.Message == "CLIPBOARD_MESSAGE_UNSUPPORTED" => "CLIPBOARD_MESSAGE_UNSUPPORTED",
                    InvalidDataException => "CLIPBOARD_FRAME_INVALID",
                    OperationCanceledException => "CLIPBOARD_READ_CANCELLED",
                    ObjectDisposedException => "CLIPBOARD_STREAM_CLOSED",
                    _ => "CLIPBOARD_READ_FAILED"
                };
            }
            finally
            {
                sink.SetConnection(false);
                try { observe?.Invoke(new(reason, DateTimeOffset.UtcNow, reason == "CLIPBOARD_STOPPED" ? 0 : 1)); }
                catch (Exception) { /* Diagnostic observers must not change clipboard/control semantics. */ }
            }
        });
    }
    internal static async Task ReadAsync(Stream stream, IPhoneClipboardSink sink, CancellationToken token)
    {
        byte[] header = new byte[8];
        var utf8 = new UTF8Encoding(false, true);
        while (true)
        {
            await stream.ReadExactlyAsync(header.AsMemory(0, 1), token).ConfigureAwait(false);
            int length;
            switch (header[0])
            {
                case 0:
                    await stream.ReadExactlyAsync(header.AsMemory(0, 4), token).ConfigureAwait(false);
                    length = BinaryPrimitives.ReadInt32BigEndian(header);
                    if (length is < 0 or > 262139) { throw new InvalidDataException("CLIPBOARD_FRAME_INVALID"); }
                    byte[] bytes = new byte[length];
                    try
                    {
                        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
                        sink.Receive(utf8.GetString(bytes));
                    }
                    finally { Array.Clear(bytes); }
                    break;
                case 1:
                    await stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
                    break;
                case 2:
                    await stream.ReadExactlyAsync(header.AsMemory(0, 4), token).ConfigureAwait(false);
                    length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2));
                    byte[] ignored = new byte[length];
                    await stream.ReadExactlyAsync(ignored, token).ConfigureAwait(false);
                    break;
                default: throw new InvalidDataException("CLIPBOARD_MESSAGE_UNSUPPORTED");
            }
        }
    }
    internal void RequestStop()
    {
        try { _stop.Cancel(); }
        catch (ObjectDisposedException) { /* A late owner stop is already complete. */ }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        try { await _reader.ConfigureAwait(false); }
        finally { _stop.Dispose(); }
    }
}
