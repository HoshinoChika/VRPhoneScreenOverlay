namespace VRPhoneScreenOverlay.Android;

public enum AndroidAudioCodec
{
    Opus,
    Aac,
    Flac,
    Raw,
}

public enum AndroidAudioStreamItemKind
{
    Configuration,
    Media,
}

public sealed record AndroidAudioOptions
{
    public AndroidAudioCodec Codec { get; init; } = AndroidAudioCodec.Opus;

    public bool PowerOnDevice { get; init; }
}

public interface IAndroidAudioSession : IAsyncDisposable
{
    public string DeviceKey { get; }

    public string DeviceName { get; }

    public AndroidAudioCodec Codec { get; }

    public ValueTask<AndroidAudioStreamItem> ReadNextAsync(CancellationToken cancellationToken);
}

public interface IAndroidAudioSessionFactory
{
    public ValueTask<IAndroidAudioSession> OpenAsync(
        string? deviceKey,
        AndroidAudioOptions options,
        CancellationToken cancellationToken);
}

public sealed class AndroidAudioStreamItem : IDisposable
{
    private IDisposable? _owner;

    internal AndroidAudioStreamItem(
        AndroidAudioStreamItemKind kind,
        long? presentationTimeMicroseconds,
        ReadOnlyMemory<byte> payload,
        IDisposable owner)
    {
        Kind = kind;
        PresentationTimeMicroseconds = presentationTimeMicroseconds;
        Payload = payload;
        _owner = owner;
    }

    public AndroidAudioStreamItemKind Kind { get; }

    public long? PresentationTimeMicroseconds { get; }

    public ReadOnlyMemory<byte> Payload { get; private set; }

    public void Dispose()
    {
        IDisposable? owner = Interlocked.Exchange(ref _owner, null);
        owner?.Dispose();
        Payload = ReadOnlyMemory<byte>.Empty;
    }
}
