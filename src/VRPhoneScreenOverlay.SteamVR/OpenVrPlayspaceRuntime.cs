using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

internal interface IOpenVrPlayspaceRuntime
{
    public uint ControllerIndex(ETrackedControllerRole role);
    public bool ReadWorkingBasis(ref HmdMatrix34_t basis);
    public void RevertWorkingCopy();
    public void SetWorkingBasis(ref HmdMatrix34_t basis);
    public void ShowPreview();
    public void HidePreview();
}

internal sealed class OpenVrPlayspaceRuntime(CVRSystem system, CVRChaperoneSetup setup) : IOpenVrPlayspaceRuntime
{
    public uint ControllerIndex(ETrackedControllerRole role) => system.GetTrackedDeviceIndexForControllerRole(role);
    public bool ReadWorkingBasis(ref HmdMatrix34_t basis) => setup.GetWorkingStandingZeroPoseToRawTrackingPose(ref basis);
    public void RevertWorkingCopy() => setup.RevertWorkingCopy();
    public void SetWorkingBasis(ref HmdMatrix34_t basis) => setup.SetWorkingStandingZeroPoseToRawTrackingPose(ref basis);
    public void ShowPreview() => setup.ShowWorkingSetPreview();
    public void HidePreview() => setup.HideWorkingSetPreview();
}
