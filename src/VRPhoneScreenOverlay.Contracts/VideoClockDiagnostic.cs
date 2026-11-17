namespace VRPhoneScreenOverlay.Contracts;

/// <summary>Clock mapping evidence, not a physical display latency guarantee.</summary>
public readonly record struct VideoClockDiagnostic(double RoundTripMilliseconds, double AnchorAgeMilliseconds)
{
    public double MidpointHalfWindowMilliseconds => RoundTripMilliseconds / 2;
    public bool DriftMeasured { get; } = false;
}
