namespace VRPhoneScreenOverlay.SteamVR;

internal sealed class OpenVrGeometryRevisionGate
{
    private readonly object _gate = new();
    private long _frame, _notified, _applied, _noticeFrame, _requiredFrame;
    internal void FramePresented() { lock (_gate) { _frame++; } }
    internal void Notify(long revision)
    {
        lock (_gate)
        {
            if (revision == _notified) { return; }
            _notified = revision; _noticeFrame = _frame;
        }
    }
    internal bool Apply(out long revision)
    {
        lock (_gate)
        {
            revision = _notified;
            if (_applied == revision) { return false; }
            _applied = revision; _requiredFrame = _noticeFrame + 1; return true;
        }
    }
    internal bool HasFreshFrame { get { lock (_gate) { return _frame >= _requiredFrame; } } }
}
