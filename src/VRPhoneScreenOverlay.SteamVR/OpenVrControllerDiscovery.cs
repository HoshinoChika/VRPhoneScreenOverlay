using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

public sealed record ControllerDiscoverySnapshot(bool Connected, string? ControllerType, string ReasonCode)
{
    public int? ProbeProcessId { get; init; }
    public double? ProbeElapsedMilliseconds { get; init; }
    public int? ProbeExitCode { get; init; }
    public string? ProbeStage { get; init; }
}

public static class OpenVrControllerDiscovery
{
    private static readonly ControllerProbeCoordinator _coordinator = new();
    public static Task<ControllerDiscoverySnapshot> ReadAsync(OpenVrControllerHand hand, CancellationToken cancellationToken) =>
        _coordinator.ReadAsync(hand,
            () => OpenVrRuntimeAvailability.IsRunning() && OpenVrRuntimeAvailability.IsNamespaceAvailable(),
            ReadExisting, ReadIsolatedAsync, Environment.TickCount64, cancellationToken);

    private static ControllerDiscoverySnapshot? ReadExisting(OpenVrControllerHand hand)
    {
        if (!Monitor.TryEnter(OpenVrRuntimeHost.ApiGate, 100))
        { return new(false, null, "CONTROLLER_PROBE_BUSY"); }
        try { return OpenVrRuntimeHost.CurrentSystem is { } system ? ReadSystem(system, hand) : null; }
        finally { Monitor.Exit(OpenVrRuntimeHost.ApiGate); }
    }

    private static async Task<ControllerDiscoverySnapshot> ReadIsolatedAsync(OpenVrControllerHand hand, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OpenVrRuntimeAvailability.IsRunning()) { return new(false, null, "CONTROLLER_RUNTIME_UNAVAILABLE"); }
        try { await BindingToolComponents.Current().EnsureAsync(cancellationToken).ConfigureAwait(false); }
        catch (BindingToolComponentException error) { return new(false, null, error.ReasonCode); }
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
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(10));
        bool started = false;
        long began = Stopwatch.GetTimestamp();
        ControllerDiscoverySnapshot Finish(ControllerDiscoverySnapshot value) => value with
        {
            ProbeProcessId = started ? process.Id : null,
            ProbeElapsedMilliseconds = Stopwatch.GetElapsedTime(began).TotalMilliseconds,
            ProbeExitCode = started && process.HasExited ? process.ExitCode : null,
            ProbeStage = value.ReasonCode == "CONTROLLER_PROBE_TIMEOUT" ? "timeout-before-result" : "result"
        };
        try
        {
            if (!process.Start()) { return Finish(new(true, null, "CONTROLLER_PROBE_FAILED")); }
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
            if (length > 1024) { return Finish(new(true, null, "CONTROLLER_PROBE_INVALID")); }
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            ControllerDiscoverySnapshot? result = JsonSerializer.Deserialize<ControllerDiscoverySnapshot>(buffer.AsSpan(0, length));
            if (process.ExitCode != 0 || result is null || (result.ControllerType is { } type && !OpenVrGenericBinding.ValidType(type)))
            { return Finish(new(true, null, "CONTROLLER_PROBE_INVALID")); }
            return Finish(result);
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Finish(new(true, null, "CONTROLLER_PROBE_TIMEOUT"));
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return Finish(new(true, null, "CONTROLLER_PROBE_FAILED")); }
        finally
        {
            if (started && !process.HasExited)
            {
                process.Kill(entireProcessTree: false);
                using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(2));
                await process.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
            }
        }
    }

    // Background initialization only inside the isolated helper. Never submits an
    // action manifest, activates bindings, or launches an absent runtime.
    public static ControllerDiscoverySnapshot Inspect(OpenVrControllerHand hand)
    {
        if (!OpenVrRuntimeAvailability.IsRunning() || !OpenVrRuntimeAvailability.IsNamespaceAvailable())
        { return new(false, null, "CONTROLLER_RUNTIME_UNAVAILABLE"); }
        bool initialized = false;
        try
        {
            EVRInitError error = EVRInitError.None;
            CVRSystem system = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Background);
            if (error != EVRInitError.None) { return new(true, null, "CONTROLLER_PROBE_FAILED"); }
            initialized = true;
            return ReadSystem(system, hand);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { return new(true, null, "CONTROLLER_PROBE_FAILED"); }
        finally { if (initialized) { OpenVR.Shutdown(); } }
    }

    private static ControllerDiscoverySnapshot ReadSystem(CVRSystem system, OpenVrControllerHand hand)
    {
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
}
