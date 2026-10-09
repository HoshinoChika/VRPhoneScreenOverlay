using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

internal static class OpenVrRuntimeHost
{
    private static readonly object _gate = new();
    private static readonly OpenVrActionUpdateCoordinator _actionUpdates = new();
    private static readonly OpenVrRuntimeEvents _events = new();
    private static CVRSystem? _system;
    private static bool _actionManifestSubmitted;

    internal static object ApiGate => _gate;
    internal static CVRSystem? CurrentSystem => _system;

    internal static OpenVrActionUpdateCoordinator ActionUpdates => _actionUpdates;
    internal static OpenVrRuntimeEvents Events => _events;

    internal static ETrackedControllerRole InputHandRole(OpenVrControllerHand hand) =>
        RelativeHandRole(hand, OpenVrBindingRecovery.DiagnosticSnapshot.ControllerHand);

    internal static ETrackedControllerRole RelativeHandRole(OpenVrControllerHand desired, OpenVrControllerHand prepared) =>
        desired == prepared ? ETrackedControllerRole.RightHand : ETrackedControllerRole.LeftHand;

    internal static EVRInputError SetInputHand(OpenVrControllerHand hand) => OpenVR.Input.SetDominantHand(InputHandRole(hand));

    public static CVRSystem GetOrStart(ref EVRInitError initializationError)
    {
        lock (_gate)
        {
            if (_system is not null)
            {
                initializationError = EVRInitError.None;
                return _system;
            }

            // Overlay initialization may launch SteamVR. Never invoke it while the
            // runtime is absent; the existing worker retries after SteamVR starts.
            if (!OpenVrRuntimeAvailability.IsRunning() || !OpenVrRuntimeAvailability.IsNamespaceAvailable())
            {
                initializationError = EVRInitError.Init_NoServerForBackgroundApp;
                return null!;
            }

            CVRSystem system = OpenVR.Init(
                ref initializationError,
                EVRApplicationType.VRApplication_Overlay);
            if (initializationError == EVRInitError.None)
            {
                _system = system;
            }

            return system;
        }
    }

    public static EVRInputError EnsureActionManifestSubmitted()
    {
        lock (_gate)
        {
            if (_actionManifestSubmitted)
            {
                return EVRInputError.None;
            }

            EVRInputError error = OpenVrInputManifest.RegisterAndSubmit();
            if (error is EVRInputError.None or EVRInputError.IPCError)
            {
                _actionManifestSubmitted = true;
                // Runtime copies are already prepared for the persisted hand.
                // Reset SteamVR's relative flip on every new runtime session.
                _ = OpenVR.Input.SetDominantHand(ETrackedControllerRole.RightHand);
            }

            return error;
        }
    }

    public static void Shutdown()
    {
        lock (_gate)
        {
            if (_system is null)
            {
                return;
            }

            OpenVR.Shutdown();
            _system = null;
            _actionManifestSubmitted = false;
            _events.Reset();
        }
    }
}
