namespace VRPhoneScreenOverlay.Contracts;

public enum SharingMode
{
    Disabled,
    FreePeerToPeer,
    PaidServerRelay,
}

public enum SharingRole
{
    None,
    Host,
    ReadOnlyViewer,
}

public sealed record SharingSessionSnapshot(
    SharingMode Mode,
    SharingRole Role,
    int ViewerCount,
    string StateCode);

public interface ISharingSession : IAsyncDisposable
{
    public SharingSessionSnapshot Snapshot { get; }

    public ValueTask StartAsync(SharingMode mode, SharingRole role, CancellationToken cancellationToken);

    public ValueTask StopAsync(CancellationToken cancellationToken);
}
