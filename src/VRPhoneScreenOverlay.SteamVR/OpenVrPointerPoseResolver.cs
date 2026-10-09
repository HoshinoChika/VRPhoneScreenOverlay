using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

internal sealed class OpenVrPointerPoseResolver
{
    private HmdMatrix34_t _controllerToPointer;
    private bool _calibrated;

    public bool TryResolve(
        bool actionPoseValid,
        HmdMatrix34_t actionPose,
        bool controllerPoseValid,
        HmdMatrix34_t controllerPose,
        out HmdMatrix34_t pointerPose)
    {
        if (actionPoseValid)
        {
            pointerPose = actionPose;
            if (controllerPoseValid)
            {
                _controllerToPointer = OpenVrTransformMath.Multiply(
                    OpenVrTransformMath.InverseRigid(controllerPose),
                    actionPose);
                _calibrated = true;
            }

            return true;
        }

        if (_calibrated && controllerPoseValid)
        {
            pointerPose = OpenVrTransformMath.Multiply(
                controllerPose,
                _controllerToPointer);
            return true;
        }

        pointerPose = default;
        return false;
    }
}
