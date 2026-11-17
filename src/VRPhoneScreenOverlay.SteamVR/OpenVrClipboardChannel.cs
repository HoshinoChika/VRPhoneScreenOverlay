namespace VRPhoneScreenOverlay.SteamVR;

public enum ClipboardPanelAction { Copy, Parse, Expand, Up, Down, Close }
public sealed record ClipboardPanelHit(int X, int Y, int Width, int Height, ClipboardPanelAction Action, Guid EntryId = default);
public sealed record ClipboardPanelFrame(byte[] Pixels, IReadOnlyList<ClipboardPanelHit> Hits, int MaximumScroll, int Scroll = 0);

/// <summary>Single command slot, reject newest on overflow; frames are replaced, never queued.</summary>
public sealed class OpenVrClipboardChannel
{
    private ClipboardPanelFrame? _frame;
    private ClipboardPanelHit? _pending;
    private ClipboardPanelHit? _pressed;
    // Capacity one, latest absolute position wins; owned by the VR producer and presentation consumer.
    private int _scrollRequest = -1;
    private long _replacedScrollRequests;
    public long ReplacedScrollRequests => Interlocked.Read(ref _replacedScrollRequests);
    public void RequestScroll(int position)
    {
        if (Interlocked.Exchange(ref _scrollRequest, Math.Max(0, position)) >= 0)
        { Interlocked.Increment(ref _replacedScrollRequests); }
    }
    public int TakeScroll() => Interlocked.Exchange(ref _scrollRequest, -1);
    public Func<bool>? PhoneConnected { get; init; }
    public bool IsPhoneConnected => PhoneConnected?.Invoke() ?? true;
    private long _rejected;
    public ClipboardPanelFrame? Frame => Volatile.Read(ref _frame);
    public ClipboardPanelHit? Pressed => Volatile.Read(ref _pressed);
    public long RejectedCommands => Interlocked.Read(ref _rejected);
    public void SetPressed(ClipboardPanelHit? hit) => Volatile.Write(ref _pressed, hit);
    public void Publish(ClipboardPanelFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Pixels.Length != OpenVrTranslationChannel.Width * OpenVrTranslationChannel.Height * 4)
        { throw new ArgumentException("Incorrect clipboard surface size.", nameof(frame)); }
        Volatile.Write(ref _frame, frame);
    }
    public bool Submit(ClipboardPanelHit command)
    {
        if (Interlocked.CompareExchange(ref _pending, command, null) is null) { return true; }
        Interlocked.Increment(ref _rejected); return false;
    }
    public ClipboardPanelHit? Take() => Interlocked.Exchange(ref _pending, null);
}
