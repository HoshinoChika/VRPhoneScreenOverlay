namespace VRPhoneScreenOverlay.Contracts;

/// <summary>
/// Implemented by every exception that carries a stable reason code, so callers can surface a
/// failure without knowing which module produced it and without one catch block per module.
/// </summary>
public interface IReasonCoded
{
    /// <summary>Stable machine readable reason code. Never localised, never re-spelled.</summary>
    public string ReasonCode { get; }
}
