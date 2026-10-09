using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

internal static class OpenVrInputCaptureProfiles
{
    // Runs in the bounded binding-helper process before manifest submission.
    // Reads driver metadata without modifying any driver or user binding file.
    public static async Task EnrichAsync(CancellationToken cancellationToken, OpenVrControllerHand? preparedHand = null)
    {
        CVRSystem system = OpenVR.System;
        OpenVrControllerHand hand = OpenVrControllerPreferences.Load();
        uint device = system.GetTrackedDeviceIndexForControllerRole(hand == OpenVrControllerHand.Left
            ? ETrackedControllerRole.LeftHand : ETrackedControllerRole.RightHand);
        if (device == OpenVR.k_unTrackedDeviceIndexInvalid) { return; }
        string type = ReadProperty(system, device, ETrackedDeviceProperty.Prop_ControllerType_String);
        string profileResource = ReadProperty(system, device, ETrackedDeviceProperty.Prop_InputProfilePath_String);
        if (string.IsNullOrEmpty(type) || type.Length > 128 || type.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not '-') ||
            string.IsNullOrEmpty(profileResource)) { return; }
        StringBuilder resolved = new(4096);
        EVRInitError resourceError = EVRInitError.None;
        nint resourceInterface = OpenVR.GetGenericInterface("FnTable:" + OpenVR.IVRResources_Version, ref resourceError);
        if (resourceError != EVRInitError.None || resourceInterface == 0) { return; }
        CVRResources resources = new(resourceInterface);
        uint length = resources.GetResourceFullPath(profileResource, string.Empty, resolved, (uint)resolved.Capacity);
        if (length == 0 || length > resolved.Capacity) { return; }
        string runtimePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VRPhoneScreenOverlay", "steamvr", "bindings", type + ".json");
        string temporary = runtimePath + ".capture-new";
        try
        {
            string profilePath = resolved.ToString();
            if (!File.Exists(runtimePath) || !File.Exists(profilePath) ||
                new FileInfo(profilePath).Length > 1_048_576 || new FileInfo(runtimePath).Length > 1_048_576) { return; }
            JsonNode? profile = JsonNode.Parse(await File.ReadAllTextAsync(profilePath, cancellationToken).ConfigureAwait(false));
            JsonNode? binding = JsonNode.Parse(await File.ReadAllTextAsync(runtimePath, cancellationToken).ConfigureAwait(false));
            if (binding is null || profile is null) { return; }
            OpenVrInputCaptureBindings.Add(binding, preparedHand ?? hand, profile: profile);
            await File.WriteAllTextAsync(temporary, binding.ToJsonString(), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, runtimePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            JsonException or InvalidOperationException or OperationCanceledException)
        {
            // Metadata is optional: retain the already prepared binding-derived set.
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private static string ReadProperty(CVRSystem system, uint device, ETrackedDeviceProperty property)
    {
        ETrackedPropertyError error = ETrackedPropertyError.TrackedProp_Success;
        StringBuilder value = new(4096);
        system.GetStringTrackedDeviceProperty(device, property, value, (uint)value.Capacity, ref error);
        return error == ETrackedPropertyError.TrackedProp_Success ? value.ToString() : string.Empty;
    }
}
