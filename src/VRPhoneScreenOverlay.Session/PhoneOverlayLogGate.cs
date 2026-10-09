using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Session;

/// <summary>
/// Decides which per-frame observations are worth a log entry.
/// </summary>
/// <remarks>
/// The overlay loop runs at video frame rate, so logging any of this unconditionally would write
/// thousands of identical lines per minute. Each method reports only genuine transitions, and
/// remembers the last value it accepted.
/// </remarks>
public sealed class PhoneOverlayLogGate
{
    private string? _lastInteractionReasonCode;
    private bool _lastWorldAnchored;
    private bool _lastGrabbed;
    private bool _lastHovered;
    private bool _lastControllerPoseValid;
    private string? _lastBindingReasonCode;
    private int _lastSubmittedWidth;
    private int _lastSubmittedHeight;
    // Starts at zero so the first quality sample is written one interval into the session,
    // not on the very first frame.
    private TimeSpan _lastQualityLogAt = TimeSpan.Zero;

    /// <summary>True when any tracked field of the controller interaction changed.</summary>
    public bool ShouldLogInteraction(OpenVrPhoneInteractionSnapshot interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        bool changed = !string.Equals(
                interaction.ReasonCode,
                _lastInteractionReasonCode,
                StringComparison.Ordinal) ||
            interaction.WorldAnchored != _lastWorldAnchored ||
            interaction.Grabbed != _lastGrabbed ||
            interaction.Hovered != _lastHovered ||
            interaction.ControllerPoseValid != _lastControllerPoseValid;
        if (!changed)
        {
            return false;
        }

        _lastInteractionReasonCode = interaction.ReasonCode;
        _lastWorldAnchored = interaction.WorldAnchored;
        _lastGrabbed = interaction.Grabbed;
        _lastHovered = interaction.Hovered;
        _lastControllerPoseValid = interaction.ControllerPoseValid;
        return true;
    }

    /// <summary>True when the controller binding health reason code changed.</summary>
    public bool ShouldLogBindingHealth(string reasonCode)
    {
        if (string.Equals(reasonCode, _lastBindingReasonCode, StringComparison.Ordinal))
        {
            return false;
        }

        _lastBindingReasonCode = reasonCode;
        return true;
    }

    /// <summary>True when the submitted frame size changed.</summary>
    public bool ShouldLogSubmittedSize(int width, int height)
    {
        if (width == _lastSubmittedWidth && height == _lastSubmittedHeight)
        {
            return false;
        }

        _lastSubmittedWidth = width;
        _lastSubmittedHeight = height;
        return true;
    }

    /// <summary>True when <paramref name="interval"/> has passed since the last quality sample.</summary>
    /// <param name="elapsed">Elapsed time since the session started.</param>
    /// <param name="interval">Minimum gap between quality log entries.</param>
    public bool ShouldLogQualitySample(TimeSpan elapsed, TimeSpan interval)
    {
        if (elapsed - _lastQualityLogAt < interval)
        {
            return false;
        }

        _lastQualityLogAt = elapsed;
        return true;
    }
}
