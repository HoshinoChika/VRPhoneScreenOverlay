namespace VRPhoneScreenOverlay.Diagnostics;

/// <summary>Consumes sampled counters; static pictures alone are never classified as a video stall.</summary>
internal sealed class DiagnosticFaultDetector
{
    private readonly Dictionary<FlightComponent, string> _states = [];
    private long _frames;
    private long _bytes;
    private long _stalledSince;
    private long _nextRequest;
    private bool _pendingFailure;

    internal string? Observe(FlightRecord record, long now)
    {
        FlightSample? latest = record.Samples.LastOrDefault();
        if (latest is null) { return null; }
        string? request = null;
        foreach (FlightChannel channel in latest.Channels)
        {
            if (!Enum.IsDefined(channel.Component)) { continue; }
            string previous = _states.GetValueOrDefault(channel.Component, "unknown");
            string signature = channel.State + ":" + channel.Reason;
            _states[channel.Component] = signature;
            bool failed = channel.State is "Faulted" or "Failed" ||
                channel.Reason.EndsWith("_FAILED", StringComparison.Ordinal) || channel.Reason.EndsWith("_TIMEOUT", StringComparison.Ordinal);
            if (failed && previous != signature)
            { _pendingFailure = true; }
            if (channel.Component != FlightComponent.Video) { continue; }
            if (channel.State is "Running" or "Starting" && channel.Completed == _frames && channel.Bytes > _bytes)
            {
                if (_stalledSince == 0) { _stalledSince = now; }
                else if (now - _stalledSince >= 5000) { request ??= "pipeline-stall"; }
            }
            else { _stalledSince = 0; }
            _frames = channel.Completed;
            _bytes = channel.Bytes;
        }
        if (_pendingFailure) { request = "functional-failure"; }
        if (request is null || now < _nextRequest) { return null; }
        _pendingFailure = false;
        _nextRequest = now + 5000;
        return request;
    }
}
