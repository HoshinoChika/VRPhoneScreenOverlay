using System.Runtime.InteropServices;
using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

internal readonly record struct OpenVrPlayspaceInputSample(
    bool DashboardVisible,
    bool DragPressed,
    bool ResetPressed,
    bool IsValid)
{
    // Read the existing semantic actions from the existing selected hand. A
    // disabled/unavailable action set must not masquerade as a physical release.
    public static OpenVrPlayspaceInputSample Read(
        CVRInput input,
        ulong leftDrag,
        ulong rightDrag,
        ulong resetOffsets,
        ulong inputSource,
        bool dashboard)
    {
        InputDigitalActionData_t left = default;
        InputDigitalActionData_t right = default;
        InputDigitalActionData_t reset = default;
        uint size = (uint)Marshal.SizeOf<InputDigitalActionData_t>();
        EVRInputError leftError = input.GetDigitalActionData(leftDrag, ref left, size, inputSource);
        EVRInputError rightError = input.GetDigitalActionData(rightDrag, ref right, size, inputSource);
        EVRInputError resetError = input.GetDigitalActionData(resetOffsets, ref reset, size, inputSource);
        return new OpenVrPlayspaceInputSample(
            dashboard,
            (left.bActive && left.bState) || (right.bActive && right.bState),
            reset.bActive && reset.bState,
            leftError == EVRInputError.None && rightError == EVRInputError.None &&
                resetError == EVRInputError.None && (left.bActive || right.bActive || reset.bActive));
    }
}
