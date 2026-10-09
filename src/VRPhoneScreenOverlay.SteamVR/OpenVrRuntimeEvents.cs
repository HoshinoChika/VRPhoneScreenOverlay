using System.Runtime.InteropServices;
using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

// Accessed only under OpenVrRuntimeHost.ApiGate. Events are drained in bounded
// batches; origin notifications coalesce into a revision and binding failures
// into one pending flag. No additional event queue or per-event task is created.
internal sealed class OpenVrRuntimeEvents
{
    private bool _bindingFailurePending;
    public long OriginRevision { get; private set; }
    public bool RoomSetupActive { get; private set; }
    private int _bindingChanges;
    public long BindingRevision { get; private set; }
    public bool BindingChanging => _bindingChanges > 0;
    public void BeginBindingChange() { _bindingChanges++; BindingRevision++; }
    public void EndBindingChange() { _bindingChanges = Math.Max(0, _bindingChanges - 1); BindingRevision++; }

    public void Poll(CVRSystem system)
    {
        VREvent_t value = default;
        uint size = (uint)Marshal.SizeOf<VREvent_t>();
        for (int index = 0; index < 32 && system.PollNextEvent(ref value, size); index++)
        {
            Observe((EVREventType)value.eventType);
        }
    }

    internal void Observe(EVREventType type)
    {
        switch (type)
        {
            case EVREventType.VREvent_Input_BindingLoadFailed:
                _bindingFailurePending = true;
                break;
            case EVREventType.VREvent_StandingZeroPoseReset:
            case EVREventType.VREvent_SeatedZeroPoseReset:
                OriginRevision++;
                break;
            case EVREventType.VREvent_ChaperoneRoomSetupStarting:
                RoomSetupActive = true;
                OriginRevision++;
                break;
            case EVREventType.VREvent_ChaperoneRoomSetupFinished:
                RoomSetupActive = false;
                OriginRevision++;
                break;
        }
    }

    public bool TakeBindingFailure()
    {
        bool pending = _bindingFailurePending;
        _bindingFailurePending = false;
        return pending;
    }

    public void Reset()
    {
        _bindingChanges = 0;
        BindingRevision++;
        _bindingFailurePending = false;
        RoomSetupActive = false;
        OriginRevision++;
    }
}
