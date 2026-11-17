namespace VRPhoneScreenOverlay.SteamVR;

// One operation slot, no queue; extra clicks are rejected and counted.
public sealed class OpenVrScreenshotChannel
{
    private int _busy;
    private int _pending;
    private long _rejected;
    private byte[]? _pixels;
    private volatile bool _available;
    public bool Available { get => _available; set => _available = value; }
    public bool Busy => Volatile.Read(ref _busy) != 0;
    public long Rejected => Interlocked.Read(ref _rejected);
    public byte[]? Pixels => Volatile.Read(ref _pixels);
    public void Publish(byte[] pixels) => Volatile.Write(ref _pixels, pixels);
    public bool Submit()
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) { Interlocked.Increment(ref _rejected); return false; }
        Volatile.Write(ref _pending, 1); return true;
    }
    public bool Take() => Interlocked.Exchange(ref _pending, 0) != 0;
    public void Complete() => Volatile.Write(ref _busy, 0);
}
