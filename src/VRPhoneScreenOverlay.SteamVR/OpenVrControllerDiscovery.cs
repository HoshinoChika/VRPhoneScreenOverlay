using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

public sealed record ControllerDiscoverySnapshot(bool Connected, string? ControllerType, string ReasonCode);

public static class OpenVrControllerDiscovery
{
    public static async Task<ControllerDiscoverySnapshot> ReadAsync(OpenVrControllerHand hand, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OpenVrRuntimeAvailability.IsRunning()) { return new(false, null, "CONTROLLER_RUNTIME_UNAVAILABLE"); }
        return await ReadProbeAsync(Path.Combine(AppContext.BaseDirectory, "VRPhoneScreenOverlay.SteamVR.BindingTool.exe"),
            "inspect-controller " + (hand == OpenVrControllerHand.Left ? "left" : "right"), cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<ControllerDiscoverySnapshot> ReadProbeAsync(string executable, string arguments,
        CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using Process process = new()
        {
            StartInfo = new()
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
            },
        };
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(3));
        bool started = false;
        try
        {
            if (!process.Start()) { return new(true, null, "CONTROLLER_PROBE_FAILED"); }
            started = true;
            // One probe at a time, fixed 1 KiB response, no output queue.
            char[] buffer = new char[1025];
            int length = 0;
            while (length < buffer.Length)
            {
                int read = await process.StandardOutput.ReadAsync(buffer.AsMemory(length), deadline.Token).ConfigureAwait(false);
                if (read == 0) { break; }
                length += read;
            }
            if (length > 1024) { return new(true, null, "CONTROLLER_PROBE_INVALID"); }
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            ControllerDiscoverySnapshot? result = JsonSerializer.Deserialize<ControllerDiscoverySnapshot>(buffer.AsSpan(0, length));
            if (process.ExitCode != 0 || result is null || (result.ControllerType is { } type && !OpenVrGenericBinding.ValidType(type)))
            { return new(true, null, "CONTROLLER_PROBE_INVALID"); }
            return result;
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(true, null, "CONTROLLER_PROBE_TIMEOUT");
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return new(true, null, "CONTROLLER_PROBE_FAILED"); }
        finally
        {
            if (started && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(2));
                await process.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
            }
        }
    }

    // Utility initialization only inside the isolated helper. Never submits an
    // action manifest, activates bindings, or launches an absent runtime.
    public static ControllerDiscoverySnapshot Inspect(OpenVrControllerHand hand)
    {
        if (!OpenVrRuntimeAvailability.IsRunning()) { return new(false, null, "CONTROLLER_RUNTIME_UNAVAILABLE"); }
        bool initialized = false;
        try
        {
            EVRInitError error = EVRInitError.None;
            CVRSystem system = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Utility);
            if (error != EVRInitError.None) { return new(true, null, "CONTROLLER_PROBE_FAILED"); }
            initialized = true;
            uint device = system.GetTrackedDeviceIndexForControllerRole(hand == OpenVrControllerHand.Left
                ? ETrackedControllerRole.LeftHand : ETrackedControllerRole.RightHand);
            if (device == OpenVR.k_unTrackedDeviceIndexInvalid || !system.IsTrackedDeviceConnected(device))
            {
                device = OpenVR.k_unTrackedDeviceIndexInvalid;
                for (uint index = 0; index < OpenVR.k_unMaxTrackedDeviceCount; index++)
                {
                    if (system.IsTrackedDeviceConnected(index) && system.GetTrackedDeviceClass(index) == ETrackedDeviceClass.Controller)
                    { device = index; break; }
                }
            }
            if (device == OpenVR.k_unTrackedDeviceIndexInvalid) { return new(false, null, "CONTROLLER_NOT_CONNECTED"); }
            ETrackedPropertyError propertyError = ETrackedPropertyError.TrackedProp_Success;
            StringBuilder type = new(129);
            system.GetStringTrackedDeviceProperty(device, ETrackedDeviceProperty.Prop_ControllerType_String, type, 129, ref propertyError);
            return propertyError == ETrackedPropertyError.TrackedProp_Success && OpenVrGenericBinding.ValidType(type.ToString())
                ? new(true, type.ToString(), "CONTROLLER_DETECTED") : new(true, null, "CONTROLLER_TYPE_UNKNOWN");
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { return new(true, null, "CONTROLLER_PROBE_FAILED"); }
        finally { if (initialized) { OpenVR.Shutdown(); } }
    }
}
