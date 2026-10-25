using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Session;

internal sealed class PhoneInputRoutingEvidence
{
    private readonly object _gate = new();
    private long _received, _forwarded, _notReady, _noGeometry;
    private string _decision = "none";
    private string? _kind;
    private DateTimeOffset? _at;

    internal bool TryRoute(PhoneInputCommandKind kind, PhoneControlState state, int width, int height)
    {
        lock (_gate)
        {
            _received++; _kind = kind.ToString(); _at = DateTimeOffset.UtcNow;
            if (state != PhoneControlState.Ready && !(state == PhoneControlState.Faulted && kind == PhoneInputCommandKind.WakeScreen))
            { _notReady++; _decision = "control-not-ready"; return false; }
            bool positioned = kind is PhoneInputCommandKind.PointerDown or PhoneInputCommandKind.PointerMove or
                PhoneInputCommandKind.PointerUp or PhoneInputCommandKind.PointerCancel or PhoneInputCommandKind.Scroll;
            if (positioned && (width < 1 || height < 1))
            { _noGeometry++; _decision = "video-geometry-missing"; return false; }
            _forwarded++; _decision = "forwarded-to-control"; return true;
        }
    }

    internal InputRoutingDiagnostic Snapshot
    {
        get { lock (_gate) { return new(_received, _forwarded, _notReady, _noGeometry, _decision, _kind, _at); } }
    }
}
