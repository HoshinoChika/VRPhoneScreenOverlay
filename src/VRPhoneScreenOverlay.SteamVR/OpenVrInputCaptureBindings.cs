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
        JsonArray sources = [];
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        // Driver metadata supplies controls not bound to any phone action.
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
                sources.Add(new JsonObject
                {
                    ["path"] = prefix + path,
                    ["mode"] = mode,
                    ["inputs"] = new JsonObject { [slot] = new JsonObject { ["output"] = OutputForSlot(slot) } },
                });
                paths.Add(prefix + path);
            }
        }
        AddExistingSources(binding, prefix, sources, paths);
        if (fallback is not null) { AddExistingSources(fallback, prefix, sources, paths); }
        binding["bindings"]![ActionSet] = new JsonObject { ["sources"] = sources };
    }

    private static void AddExistingSources(JsonNode binding, string prefix, JsonArray sources, HashSet<string> paths)
    {
        if (binding["bindings"] is not JsonObject sets) { return; }
        foreach ((string _, JsonNode? set) in sets)
        {
            if (set?["sources"] is not JsonArray existing) { continue; }
            foreach (JsonNode? source in existing)
            {
                string? path = source?["path"]?.GetValue<string>();
                if (path is null || !path.StartsWith(prefix + "/input/", StringComparison.Ordinal) ||
                    source?["inputs"] is not JsonObject inputs || !paths.Add(path)) { continue; }
                JsonObject copy = (JsonObject)source.DeepClone();
                JsonObject captured = [];
                foreach ((string slot, JsonNode? input) in inputs)
                {
                    if (input?["output"] is not null)
                    {
                        captured[slot] = new JsonObject { ["output"] = OutputForSlot(slot) };
                    }
                }
                if (captured.Count > 0)
                {
                    copy["inputs"] = captured;
                    copy.Remove("parameters");
                    sources.Add(copy);
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
