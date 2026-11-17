namespace VRPhoneScreenOverlay.SteamVR;

public enum TranslationPanelAction { Answer, Translate, Explain, Close, ScrollUp, ScrollDown, ToggleRecord, SoundToggle }

public sealed record TranslationPanelHit(int X, int Y, int Width, int Height, TranslationPanelAction Action, int RecordIndex = -1);

/// <summary>Immutable-by-ownership pixels and hit rectangles published by presentation.</summary>
public sealed record TranslationPanelFrame(byte[] Pixels, IReadOnlyList<TranslationPanelHit> Hits, long Revision, int MaximumScroll = 0, int Scroll = 0);

/// <summary>Capacity-one command mailbox; overflow rejects newest. No image or command backlog.</summary>
public sealed class OpenVrTranslationChannel
{
    public OpenVrScreenshotChannel Screenshot { get; } = new();
    public const int Width = 768;
    public const int Height = 1024;
    public const float WidthMeters = 0.48f;
    public const float Aspect = Width / (float)Height;
    private TranslationPanelFrame? _frame;
    private TranslationPanelHit? _pending;
    private TranslationPanelHit? _pressed;
    public TranslationPanelHit? Pressed => Volatile.Read(ref _pressed);
    public void SetPressed(TranslationPanelHit? hit) => Volatile.Write(ref _pressed, hit);
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
    public long RejectedCommands => Interlocked.Read(ref _rejected);
    public TranslationPanelFrame? Frame => Volatile.Read(ref _frame);
    public void Publish(TranslationPanelFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Pixels.Length != Width * Height * 4) { throw new ArgumentException("Incorrect translation surface dimensions.", nameof(frame)); }
        Volatile.Write(ref _frame, frame);
    }
    public bool Submit(TranslationPanelHit hit)
    {
        if (Interlocked.CompareExchange(ref _pending, hit, null) is null) { return true; }
        Interlocked.Increment(ref _rejected);
        return false;
    }
    public TranslationPanelHit? Take() => Interlocked.Exchange(ref _pending, null);
}
