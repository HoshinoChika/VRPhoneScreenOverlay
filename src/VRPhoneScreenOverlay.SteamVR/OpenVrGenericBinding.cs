using System.Text.Json.Nodes;

namespace VRPhoneScreenOverlay.SteamVR;

internal static class OpenVrGenericBinding
{
    internal static bool ValidType(string type) => type.Length is > 0 and <= 128 &&
        type.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    internal static string Create(string controllerType)
    {
        if (!ValidType(controllerType)) { throw new InvalidDataException("Invalid controller type."); }
        JsonObject binding = JsonNode.Parse(OpenVrBuiltInBindings.Bindings["pico_controller"])!.AsObject();
        binding["controller_type"] = controllerType;
        binding["name"] = "VRPhoneScreen Overlay - Generic Default";
        binding["description"] = "Generic controller defaults using the PICO layout";
        return binding.ToJsonString();
    }
}
