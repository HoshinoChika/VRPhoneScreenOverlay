using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Session;

public static class ControllerBindingGuideService
{
    public static IReadOnlyList<ControllerBindingChoice> Choices => OpenVrBindingGuide.Choices;
    public static string ResolveControllerType(string selectionKey) => OpenVrBindingGuide.ResolveControllerType(selectionKey);
    public static Task<ControllerDiscoverySnapshot> DetectAsync(OpenVrControllerHand hand, CancellationToken cancellationToken)
        => OpenVrControllerDiscovery.ReadAsync(hand, cancellationToken);

    public static string SelectDetectedGroup(ControllerDiscoverySnapshot snapshot, string? selectedKey)
    {
        if (!snapshot.Connected) { return "pico_controller"; }
        if (snapshot.ControllerType == "pico_controller_ice") { return "pico_controller"; }
        ControllerBindingChoice[] matches = Choices.Where(choice => choice.ControllerType == snapshot.ControllerType).ToArray();
        if (matches.Length == 1) { return matches[0].Key; }
        if (matches.FirstOrDefault(choice => choice.Key == selectedKey) is { } selected) { return selected.Key; }
        return OpenVrBindingGuide.GenericKey;
    }

    public static async ValueTask<ControllerBindingGuide> ReadDetectedAsync(ControllerDiscoverySnapshot snapshot, string? selectedKey,
        CancellationToken cancellationToken)
    {
        string key = SelectDetectedGroup(snapshot, selectedKey);
        string type = key == OpenVrBindingGuide.GenericKey ? key : snapshot.ControllerType ?? key;
        ControllerBindingGuide guide = await ReadAsync(type, cancellationToken).ConfigureAwait(false);
        return guide with { SelectionKey = key, Unrecognized = snapshot.Connected && key == OpenVrBindingGuide.GenericKey };
    }
    public static async ValueTask<ControllerBindingGuide> ReadAsync(string controllerType, CancellationToken cancellationToken) =>
        await Task.Run(async () => await OpenVrBindingGuide.ReadAsync(controllerType, cancellationToken)
            .ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
}
