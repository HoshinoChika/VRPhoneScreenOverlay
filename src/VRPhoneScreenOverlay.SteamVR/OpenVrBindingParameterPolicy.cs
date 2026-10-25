using System.Text.Json.Nodes;

namespace VRPhoneScreenOverlay.SteamVR;

internal static class OpenVrBindingParameterPolicy
{
    // Derived observers/capture describe physical sampling, not long/double-press
    // actions or duplicate haptic feedback. Main user sources remain untouched.
    private static readonly HashSet<string> _samplingKeys = new(StringComparer.Ordinal)
    {
        "click_activate_threshold", "click_deactivate_threshold", "touch_activate_threshold", "touch_deactivate_threshold",
        "force_input", "deadzone_pct", "maxzone_pct", "exponent", "invert_x", "invert_y"
    };

    internal static void KeepSamplingParameters(JsonObject source)
    {
        if (source["parameters"] is not JsonObject parameters) { source.Remove("parameters"); return; }
        foreach (string key in parameters.Select(pair => pair.Key).ToArray())
        { if (!_samplingKeys.Contains(key)) { parameters.Remove(key); } }
        if (parameters.Count == 0) { source.Remove("parameters"); }
    }

    internal static void ApplyKnownGenericTrigger(JsonObject binding, string controllerType)
    {
        // Cosmos has no dedicated VRPSO layout: keep the existing generic layout,
        // apply only this verified trigger rule. Do not extend it to unknown types.
        if (controllerType != "vive_cosmos_controller" || binding["bindings"]?["/actions/main"]?["sources"] is not JsonArray sources) { return; }
        foreach (JsonNode? source in sources)
        {
            if (source?["path"]?.GetValue<string>()?.EndsWith("/input/trigger", StringComparison.Ordinal) != true) { continue; }
            source["mode"] = "button";
            source["parameters"] = new JsonObject { ["click_activate_threshold"] = "0.65", ["click_deactivate_threshold"] = "0.6" };
        }
    }
}
