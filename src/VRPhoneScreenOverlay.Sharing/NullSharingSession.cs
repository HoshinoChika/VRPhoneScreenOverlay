using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Sharing;

public sealed class NullSharingSession : ISharingSession
{
    private bool _disposed;

    public SharingSessionSnapshot Snapshot { get; private set; } = new(
        SharingMode.Disabled,
        SharingRole.None,
        0,
        SharingReasonCodes.Disabled);

    public ValueTask StartAsync(
        SharingMode mode,
        SharingRole role,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (mode != SharingMode.Disabled || role != SharingRole.None)
        {
            throw new InvalidOperationException(
                "Sharing transports are intentionally disabled in the foundation build.");
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Snapshot = Snapshot with
        {
            Mode = SharingMode.Disabled,
            Role = SharingRole.None,
            ViewerCount = 0,
            StateCode = SharingReasonCodes.Disabled,
        };
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
