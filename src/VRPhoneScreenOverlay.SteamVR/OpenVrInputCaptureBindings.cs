using System.Text.Json.Nodes;

namespace VRPhoneScreenOverlay.SteamVR;

internal static class OpenVrInputCaptureBindings
{
    public const string ActionSet = "/actions/phonecapture";
    private const string _button = ActionSet + "/in/button";
    private const string _scalar = ActionSet + "/in/scalar";
    private const string _axis = ActionSet + "/in/axis";

    public static void Add(JsonNode binding, OpenVrControllerHand hand, JsonNode? fallback = null, JsonNode? profile = null)
    {
        string handName = hand == OpenVrControllerHand.Left ? "left" : "right";
        string prefix = "/user/hand/" + handName;
        JsonNode? previous = binding["bindings"]?[ActionSet]?["sources"]?.DeepClone();
        JsonArray sources = [];
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> components = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> physicalSlots = new(StringComparer.OrdinalIgnoreCase);
        // Selected user/default semantics win. Fallback fills absent physical
        // controls; driver metadata can additionally expose raw analog slots.
        AddExistingSources(binding, prefix, sources, paths, components, physicalSlots, false);
        if (fallback is not null) { AddExistingSources(fallback, prefix, sources, paths, components, physicalSlots, true); }
        if (profile?["input_source"] is JsonObject inputs)
        {
            foreach ((string path, JsonNode? description) in inputs)
            {
                string? side = description?["side"]?.GetValue<string>();
                string? mode = description?["type"]?.GetValue<string>();
                if (!path.StartsWith("/input/", StringComparison.Ordinal) ||
                    path.Contains("..", StringComparison.Ordinal) ||
                    (side is not null && side != handName) ||
                    mode is not ("button" or "trigger" or "joystick" or "trackpad"))
                {
                    continue;
                }
                string slot = mode is "joystick" or "trackpad" ? "position" : mode == "trigger" ? "pull" : "click";
                if (!physicalSlots.Add(prefix + path + "|" + slot)) { continue; }
                JsonObject? existingMode = sources.OfType<JsonObject>().FirstOrDefault(source =>
                    source["path"]?.GetValue<string>() == prefix + path && source["mode"]?.GetValue<string>() == mode);
                if (existingMode?["inputs"] is JsonObject existingInputs)
                {
                    existingInputs[slot] = new JsonObject { ["output"] = OutputForSlot(slot) };
                    continue;
                }
                sources.Add(new JsonObject
                {
                    ["path"] = prefix + path,
                    ["mode"] = mode,
                    ["inputs"] = new JsonObject { [slot] = new JsonObject { ["output"] = OutputForSlot(slot) } },
                });
                paths.Add(prefix + path);
            }
        }
        if (previous is JsonArray)
        {
            JsonObject retained = new() { ["bindings"] = new JsonObject { ["/actions/main"] = new JsonObject { ["sources"] = previous } } };
            AddExistingSources(retained, prefix, sources, paths, components, physicalSlots, false, true);
        }
        binding["bindings"]![ActionSet] = new JsonObject { ["sources"] = sources };
    }

    private static void AddExistingSources(JsonNode binding, string prefix, JsonArray sources, HashSet<string> paths,
        HashSet<string> components, HashSet<string> physicalSlots, bool missingPathsOnly, bool missingSlotsOnly = false)
    {
        if (binding["bindings"] is not JsonObject sets) { return; }
        IEnumerable<JsonNode?> ordered = new[] { sets["/actions/main"] }.Concat(
            sets.Where(pair => pair.Key is not "/actions/main" and not ActionSet).Select(pair => pair.Value));
        HashSet<string> selectedPaths = new(paths, StringComparer.OrdinalIgnoreCase);
        foreach (JsonNode? set in ordered)
        {
            if (set?["sources"] is not JsonArray existing) { continue; }
            foreach (JsonNode? source in existing)
            {
                string? path = source?["path"]?.GetValue<string>();
                if (path is null || !path.StartsWith(prefix + "/input/", StringComparison.Ordinal) ||
                    source?["inputs"] is not JsonObject inputs || (missingPathsOnly && selectedPaths.Contains(path))) { continue; }
                JsonObject copy = (JsonObject)source.DeepClone();
                OpenVrBindingParameterPolicy.KeepSamplingParameters(copy);
                string mode = copy["mode"]?.GetValue<string>() ?? "unknown";
                string parameters = copy["parameters"]?.ToJsonString() ?? "";
                JsonObject captured = [];
                foreach ((string slot, JsonNode? input) in inputs)
                {
                    if (input?["output"] is not null)
                    {
                        string physicalSlot = slot is "long" or "double" ? "click" : slot;
                        if (missingSlotsOnly && physicalSlots.Contains(path + "|" + physicalSlot)) { continue; }
                        if (!components.Add(path + "|" + mode + "|" + physicalSlot + "|" + parameters)) { continue; }
                        captured[physicalSlot] = new JsonObject { ["output"] = OutputForSlot(physicalSlot) };
                        physicalSlots.Add(path + "|" + physicalSlot);
                    }
                }
                if (captured.Count > 0)
                {
                    copy["inputs"] = captured;
                    sources.Add(copy);
                    paths.Add(path);
                }
            }
        }
    }

    private static string OutputForSlot(string slot) => slot switch
    {
        "position" => _axis,
        "pull" or "value" or "force" => _scalar,
        _ => _button,
    };
}
