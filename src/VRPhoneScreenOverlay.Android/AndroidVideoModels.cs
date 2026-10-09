namespace VRPhoneScreenOverlay.Android;

public enum AndroidVideoCodec
{
    H264,
    H265,
    Av1,
    Vp8,
    Vp9,
}

public enum AndroidVideoStreamItemKind
{
    Session,
    Configuration,
    Media,
}

public sealed record AndroidVideoOptions
{
    public int ResolutionPercent { get; init; } = 100;

    public int MaximumFramesPerSecond { get; init; } = 60;

    public int VideoBitrateBitsPerSecond { get; init; } = 16_000_000;

    public int MaximumSize { get; init; }

    public bool PowerOnDevice { get; init; } = true;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ResolutionPercent, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ResolutionPercent, 100);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumFramesPerSecond, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumFramesPerSecond, 120);
        ArgumentOutOfRangeException.ThrowIfLessThan(VideoBitrateBitsPerSecond, 1_000_000);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(VideoBitrateBitsPerSecond, 100_000_000);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumSize);
    }
}

public interface IAndroidVideoSession : IAsyncDisposable
{
    public string DeviceKey { get; }

    public string DeviceName { get; }

    public AndroidVideoCodec Codec { get; }

    public ValueTask<AndroidVideoStreamItem> ReadNextAsync(CancellationToken cancellationToken);
}

public interface IAndroidVideoSessionFactory
{
    public ValueTask<IAndroidVideoSession> OpenAsync(
        string? deviceKey,
        AndroidVideoOptions options,
        CancellationToken cancellationToken);
}

public interface IAndroidVideoProbeService
{
    public ValueTask<AndroidVideoProbeResult> ProbeAsync(
        string? deviceKey,
        CancellationToken cancellationToken);
}

public sealed record AndroidVideoProbeResult(
    AndroidVideoCodec Codec,
    int Width,
    int Height,
    int MediaPacketCount,
    long PayloadBytes,
    bool ConfigurationReceived,
    bool KeyFrameReceived);

public sealed class AndroidVideoStreamItem : IDisposable
{
    private IDisposable? _owner;

    internal AndroidVideoStreamItem(
        AndroidVideoStreamItemKind kind,
        int width,
        int height,
        bool clientResized,
        long? presentationTimeMicroseconds,
        bool keyFrame,
        ReadOnlyMemory<byte> payload,
        IDisposable? owner)
    {
        Kind = kind;
        Width = width;
        Height = height;
        ClientResized = clientResized;
        PresentationTimeMicroseconds = presentationTimeMicroseconds;
        IsKeyFrame = keyFrame;
        Payload = payload;
        _owner = owner;
    }

    public AndroidVideoStreamItemKind Kind { get; }

    public int Width { get; }

    public int Height { get; }

    public bool ClientResized { get; }

    public long? PresentationTimeMicroseconds { get; }

    internal long? EstimatedCaptureTimestamp { get; set; }

    public bool IsKeyFrame { get; }

    public ReadOnlyMemory<byte> Payload { get; private set; }

    public void Dispose()
    {
        IDisposable? owner = Interlocked.Exchange(ref _owner, null);
        owner?.Dispose();
        Payload = ReadOnlyMemory<byte>.Empty;
    }
}
