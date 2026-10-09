using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Android;

public interface IAndroidControlSession : IAsyncDisposable
{
    public string DeviceKey { get; }

    public ValueTask SendAsync(
        PhoneInputCommand command,
        CancellationToken cancellationToken);
}

public interface IAndroidControlSessionFactory
{
    public ValueTask<IAndroidControlSession> OpenAsync(
        string? deviceKey,
        CancellationToken cancellationToken);
}
