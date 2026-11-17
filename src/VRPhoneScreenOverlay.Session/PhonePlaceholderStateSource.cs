using VRPhoneScreenOverlay.Android;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Session;

/// <summary>Latest-only immutable snapshots; the VR input poller never takes phone/media locks.</summary>
public sealed class PhonePlaceholderStateSource : IDisposable
{
    private readonly IAndroidConnectionService _phone;
    private readonly IPhoneMediaSessionCoordinator _media;
    private AndroidConnectionSnapshot? _phoneSnapshot;
    private PhoneMediaSessionSnapshot? _mediaSnapshot;

    public PhonePlaceholderStateSource(IAndroidConnectionService phone, IPhoneMediaSessionCoordinator media)
    {
        _phone = phone;
        _media = media;
        phone.StateChanged += OnPhoneChanged;
        media.StateChanged += OnMediaChanged;
        // A concurrent notification wins over the initialization read.
        Interlocked.CompareExchange(ref _phoneSnapshot, phone.Snapshot, null);
        Interlocked.CompareExchange(ref _mediaSnapshot, media.Snapshot, null);
    }

    public OpenVrPhonePlaceholderState State => PhonePlaceholderPresentation.Select(
        Volatile.Read(ref _phoneSnapshot)?.IsReady == true,
        Volatile.Read(ref _mediaSnapshot)?.State ?? PhoneMediaSessionState.Stopped);

    private void OnPhoneChanged(object? sender, AndroidConnectionChangedEventArgs args) => Volatile.Write(ref _phoneSnapshot, args.Snapshot);
    private void OnMediaChanged(object? sender, PhoneMediaSessionChangedEventArgs args) => Volatile.Write(ref _mediaSnapshot, args.Snapshot);
    public void Dispose()
    {
        _phone.StateChanged -= OnPhoneChanged;
        _media.StateChanged -= OnMediaChanged;
    }
}
