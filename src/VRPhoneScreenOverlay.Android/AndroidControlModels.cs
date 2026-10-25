using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Android;

public interface IAndroidControlSession : IAsyncDisposable
{
    public string DeviceKey { get; }
    public ServerOutputDiagnostic? ServerOutput => null;
    public ControlGeometryDiagnostic? Geometry => null;
    public bool SupportsGeometryRefresh => false;
    public async ValueTask<PhoneInputWriteResult> SendWithResultAsync(PhoneInputCommand command, CancellationToken cancellationToken)
    {
        await SendAsync(command, cancellationToken).ConfigureAwait(false);
        return new(true);
    }

    public ValueTask SendAsync(
        PhoneInputCommand command,
        CancellationToken cancellationToken);
    // Abort only this session's transport to release a stuck write; no shared ADB shutdown.
    public void AbortPendingOperations() { }
}

public interface IAndroidControlSessionFactory
{
    public ValueTask<IAndroidControlSession> OpenAsync(
        string? deviceKey,
        CancellationToken cancellationToken);
}
