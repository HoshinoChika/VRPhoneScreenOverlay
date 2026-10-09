namespace VRPhoneScreenOverlay.SteamVR;

public sealed record OpenVrVideoOptions(int Resolution, int Bitrate, int FrameRate);
public enum OpenVrVideoApplyState { Idle, Applying, Applied, Failed }

/// <summary>One pending request owned by the session worker. Reject new requests while busy;
/// counters expose accepted/rejected traffic. Rendering only reads immutable snapshots.</summary>
public sealed class OpenVrVideoSettingsChannel
{
    private static readonly IReadOnlyList<int> _defaultResolutions = Array.AsReadOnly<int>([100]);
    private readonly object _gate = new();
    private OpenVrVideoOptions? _pending;
    private OpenVrVideoOptions _current = new(100, 16, 60);
    private IReadOnlyList<int> _resolutions = _defaultResolutions;
    private OpenVrVideoApplyState _state;
    public IReadOnlyList<int> Bitrates { get; private set; } = Array.AsReadOnly<int>([16]);
    public IReadOnlyList<int> FrameRates { get; private set; } = Array.AsReadOnly<int>([60]);

    public void ConfigureChoices(IReadOnlyList<int> bitrates, IReadOnlyList<int> frameRates)
    {
        Bitrates = Array.AsReadOnly(bitrates.Order().ToArray());
        FrameRates = Array.AsReadOnly(frameRates.Order().ToArray());
    }

    public void ClearResult()
    {
        lock (_gate) { if (_state != OpenVrVideoApplyState.Applying) { _state = OpenVrVideoApplyState.Idle; } }
    }
    public long Accepted { get; private set; }
    public long Rejected { get; private set; }
    public OpenVrVideoOptions Current { get { lock (_gate) { return _current; } } }
    public IReadOnlyList<int> Resolutions { get { lock (_gate) { return _resolutions; } } }
    public OpenVrVideoApplyState State { get { lock (_gate) { return _state; } } }

    public void Publish(OpenVrVideoOptions current, IReadOnlyList<int> resolutions)
    {
        lock (_gate)
        {
            _current = current;
            if (!_resolutions.SequenceEqual(resolutions)) { _resolutions = Array.AsReadOnly(resolutions.ToArray()); }
        }
    }

    public bool Request(OpenVrVideoOptions value)
    {
        lock (_gate)
        {
            if (_state == OpenVrVideoApplyState.Applying) { Rejected++; return false; }
            _pending = value;
            _state = OpenVrVideoApplyState.Applying;
            Accepted++;
            return true;
        }
    }

    public OpenVrVideoOptions? Take()
    {
        lock (_gate) { var value = _pending; _pending = null; return value; }
    }

    public void Complete(bool success)
    {
        lock (_gate) { _state = success ? OpenVrVideoApplyState.Applied : OpenVrVideoApplyState.Failed; }
    }
}
