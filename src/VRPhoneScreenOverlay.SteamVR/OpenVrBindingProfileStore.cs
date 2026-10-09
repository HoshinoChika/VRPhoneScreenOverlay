using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VRPhoneScreenOverlay.SteamVR;

internal enum OpenVrBindingProfileSource
{
    PackagedDefault,
    CachedUser,
    SavedUser,
}

internal sealed record OpenVrPreparedBindingProfile(
    string ControllerType,
    OpenVrBindingProfileSource Source,
    string RuntimePath);

internal sealed record OpenVrSelectedBindingProfile(
    string ControllerType, OpenVrBindingProfileSource Source, string Path, string? PackagedPath);

internal sealed record OpenVrPreparedInputManifest(
    string ActionManifestPath,
    IReadOnlyList<OpenVrPreparedBindingProfile> Bindings);

public sealed record OpenVrBindingProfileDiagnostic(
    string ControllerType,
    string Source);

public sealed record OpenVrBindingPreparationSnapshot(
    bool Prepared,
    string? Revision,
    OpenVrControllerHand ControllerHand,
    IReadOnlyList<OpenVrBindingProfileDiagnostic> Profiles)
{
    public static OpenVrBindingPreparationSnapshot NotPrepared { get; } = new(
        false,
        null,
        OpenVrControllerHand.Right,
        []);
}

internal sealed class OpenVrBindingProfileStore(
    string packagedActionManifestPath,
    string savedBindingDirectory,
    string runtimeDirectory,
    OpenVrControllerHand controllerHand = OpenVrControllerHand.Right)
{
    private const string _mainActionSetPath = "/actions/main";
    private const string _legacyPhoneButtonActionSetPath = "/actions/phonebuttons";
    private const string _pointerActionSetPath = "/actions/pointer";
    private const string _phoneButtonStateActionSetPath = "/actions/phonebuttonstate";
    private const string _phonePointerActionPath =
        "/actions/pointer/in/phonepointerpose";
    private const string _phoneButtonStateActionPath =
        "/actions/phonebuttonstate/in/anyphoneinputpressed";
    private static readonly string[] _requiredActionOutputs =
    [
        "/actions/main/in/phoneoverlaygrab",
        "/actions/main/in/phoneoverlaytouch",
        "/actions/main/in/phonepointerpose",
    ];

    private readonly string _packagedActionManifestPath = packagedActionManifestPath;
    private readonly string _savedBindingDirectory = savedBindingDirectory;
    private readonly string _runtimeDirectory = runtimeDirectory;
    private readonly OpenVrControllerHand _controllerHand = controllerHand;

    public static OpenVrBindingProfileStore CreateDefault(OpenVrControllerHand? preparedHand = null)
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string localData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return new OpenVrBindingProfileStore(
            OpenVrBuiltInBindings.EnsureSourceFiles(OpenVrBuiltInBindings.SourceDirectory),
            Path.Combine(documents, "steamvr", "input"),
            Path.Combine(localData, "VRPhoneScreenOverlay", "steamvr"),
            preparedHand ?? OpenVrControllerPreferences.Load());
    }

    public OpenVrPreparedInputManifest Prepare()
    {
        JsonNode manifest = LoadObject(_packagedActionManifestPath);
        JsonArray defaultBindings = manifest["default_bindings"]?.AsArray() ??
            throw new InvalidDataException("SteamVR 动作清单缺少默认绑定列表");
        IReadOnlyList<OpenVrSelectedBindingProfile> selected = ResolveProfiles();
        string runtimeBindingsDirectory = Path.Combine(_runtimeDirectory, "bindings");
        string cachedBindingsDirectory = Path.Combine(_runtimeDirectory, "user-bindings");
        Directory.CreateDirectory(runtimeBindingsDirectory);
        Directory.CreateDirectory(cachedBindingsDirectory);
        defaultBindings.Clear();
        List<OpenVrPreparedBindingProfile> prepared = [];
        foreach (OpenVrSelectedBindingProfile profile in selected)
        {
            string runtimeBinding = Path.Combine(runtimeBindingsDirectory, $"{profile.ControllerType}.json");
            if (profile.Source == OpenVrBindingProfileSource.SavedUser)
            {
                AtomicCopy(profile.Path, Path.Combine(cachedBindingsDirectory, $"{profile.ControllerType}.json"));
            }
            WriteRuntimeBinding(profile.Path, runtimeBinding, _controllerHand, profile.PackagedPath);
            defaultBindings.Add(new JsonObject
            {
                ["controller_type"] = profile.ControllerType,
                ["binding_url"] = $"bindings/{profile.ControllerType}.json",
            });
            prepared.Add(new(profile.ControllerType, profile.Source, runtimeBinding));
        }
        string runtimeManifest = Path.Combine(_runtimeDirectory, "action_manifest.json");
        AtomicWrite(runtimeManifest, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return new OpenVrPreparedInputManifest(runtimeManifest, prepared);
    }

    // Shared, read-only selection for both runtime preparation and instructions.
    internal IReadOnlyList<OpenVrSelectedBindingProfile> ResolveProfiles(string? cachedDirectory = null)
    {
        JsonNode manifest = LoadObject(_packagedActionManifestPath);
        JsonArray defaults = manifest["default_bindings"]?.AsArray() ?? throw new InvalidDataException("Missing default bindings.");
        string packagedDirectory = Path.GetDirectoryName(_packagedActionManifestPath)!;
        cachedDirectory ??= Path.Combine(_runtimeDirectory, "user-bindings");
        List<OpenVrSelectedBindingProfile> selected = [];
        HashSet<string> known = new(StringComparer.Ordinal);
        foreach (JsonNode? item in defaults)
        {
            string type = item?["controller_type"]?.GetValue<string>() ?? throw new InvalidDataException("Missing controller type.");
            EnsureSafeControllerType(type);
            string packaged = ResolvePackagedBinding(packagedDirectory,
                item?["binding_url"]?.GetValue<string>() ?? throw new InvalidDataException("Missing binding URL."));
            (string path, OpenVrBindingProfileSource source) = SelectBinding(type,
                Path.Combine(_savedBindingDirectory, $"{OpenVrInputManifest.ApplicationKey}_{type}.json"),
                Path.Combine(cachedDirectory, $"{type}.json"), packaged);
            selected.Add(new(type, source, path, packaged));
            known.Add(type);
        }
        if (Directory.Exists(_savedBindingDirectory))
        {
            foreach (string path in Directory.EnumerateFiles(_savedBindingDirectory, "*.json"))
            {
                if (!TryReadControllerType(path, out string type) || known.Contains(type) || !IsValidBinding(path, type)) { continue; }
                EnsureSafeControllerType(type);
                selected.Add(new(type, OpenVrBindingProfileSource.SavedUser, path, null));
                known.Add(type);
            }
        }
        return selected;
    }
    public string GetSavedBindingRevision()
    {
        JsonNode manifest = LoadObject(_packagedActionManifestPath);
        JsonArray defaultBindings = manifest["default_bindings"]?.AsArray() ??
            throw new InvalidDataException("SteamVR 动作清单缺少默认绑定列表");
        using IncrementalHash revision = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (string controllerType in defaultBindings
                     .Select(item => item?["controller_type"]?.GetValue<string>() ??
                         throw new InvalidDataException("SteamVR 默认绑定缺少手柄类型"))
                     .Order(StringComparer.Ordinal))
        {
            EnsureSafeControllerType(controllerType);
            string savedBinding = Path.Combine(
                _savedBindingDirectory,
                $"{OpenVrInputManifest.ApplicationKey}_{controllerType}.json");
            revision.AppendData(Encoding.UTF8.GetBytes(controllerType));
            if (File.Exists(savedBinding))
            {
                revision.AppendData(ReadRevisionBytes(savedBinding));
            }
        }

        if (Directory.Exists(_savedBindingDirectory))
        {
            foreach (string savedBinding in Directory
                         .EnumerateFiles(_savedBindingDirectory, "*.json")
                         .Order(StringComparer.OrdinalIgnoreCase))
            {
                if (!Path.GetFileName(savedBinding).StartsWith(OpenVrInputManifest.ApplicationKey + "_", StringComparison.Ordinal) &&
                    !TryReadControllerType(savedBinding, out _)) { continue; }
                revision.AppendData(Encoding.UTF8.GetBytes(Path.GetFileName(savedBinding)));
                revision.AppendData(ReadRevisionBytes(savedBinding));
            }
        }

        return Convert.ToHexString(revision.GetHashAndReset());
    }

    private static byte[] ReadRevisionBytes(string path)
    {
        if (new FileInfo(path).Length > 1_048_576) { throw new InvalidDataException("Binding file capacity exceeded."); }
        return File.ReadAllBytes(path);
    }

    private static (string Path, OpenVrBindingProfileSource Source) SelectBinding(
        string controllerType,
        string savedBinding,
        string cachedBinding,
        string packagedBinding)
    {
        if (IsValidBinding(savedBinding, controllerType)) { return (savedBinding, OpenVrBindingProfileSource.SavedUser); }

        if (IsValidBinding(cachedBinding, controllerType))
        {
            return (cachedBinding, OpenVrBindingProfileSource.CachedUser);
        }

        if (!IsValidBinding(packagedBinding, controllerType))
        {
            throw new InvalidDataException($"开发默认绑定无效：{controllerType}");
        }

        return (packagedBinding, OpenVrBindingProfileSource.PackagedDefault);
    }

    internal static bool IsValidBinding(string path, string controllerType)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetString(root, "app_key", out string? applicationKey) ||
                !string.Equals(
                    applicationKey,
                    OpenVrInputManifest.ApplicationKey,
                    StringComparison.Ordinal) ||
                !TryGetString(root, "controller_type", out string? actualController) ||
                !string.Equals(actualController, controllerType, StringComparison.Ordinal))
            {
                return false;
            }

            HashSet<string> outputs = new(StringComparer.OrdinalIgnoreCase);
            CollectOutputs(root, outputs);
            return _requiredActionOutputs.All(outputs.Contains) &&
                !ContainsMixedRoleSource(root);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void CollectOutputs(JsonElement element, ISet<string> outputs)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, "output", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    string? output = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        outputs.Add(output);
                    }
                }

                CollectOutputs(property.Value, outputs);
            }

            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
            {
                CollectOutputs(child, outputs);
            }
        }
    }

    private static bool ContainsMixedRoleSource(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("path", out JsonElement path) &&
                path.ValueKind == JsonValueKind.String)
            {
                HashSet<string> outputs = new(StringComparer.OrdinalIgnoreCase);
                CollectOutputs(element, outputs);
                if (outputs.Any(IsPhoneAction) && outputs.Any(IsPlayspaceAction))
                {
                    return true;
                }
            }

            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (ContainsMixedRoleSource(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
            {
                if (ContainsMixedRoleSource(child))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryGetString(
        JsonElement element,
        string propertyName,
        out string? value)
    {
        value = null;
        return element.TryGetProperty(propertyName, out JsonElement property) &&
            property.ValueKind == JsonValueKind.String &&
            (value = property.GetString()) is not null;
    }

    private static JsonObject LoadObject(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("SteamVR 动作清单未随程序发布", path);
        }

        return JsonNode.Parse(File.ReadAllText(path))?.AsObject() ??
            throw new InvalidDataException("SteamVR 动作清单不是有效的 JSON 对象");
    }

    internal static bool TryReadControllerType(string path, out string controllerType)
    {
        controllerType = string.Empty;
        try
        {
            if (new FileInfo(path).Length > 1_048_576) { return false; }
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            string? found = null;
            bool valid = root.ValueKind == JsonValueKind.Object && TryGetString(root, "app_key", out string? applicationKey) &&
                string.Equals(applicationKey, OpenVrInputManifest.ApplicationKey, StringComparison.Ordinal) &&
                TryGetString(root, "controller_type", out found) &&
                !string.IsNullOrWhiteSpace(found);
            if (valid)
            {
                controllerType = found!;
            }

            return valid;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private static void WriteRuntimeBinding(
        string source,
        string destination,
        OpenVrControllerHand controllerHand,
        string? fallbackPath = null)
    {
        JsonNode binding = JsonNode.Parse(File.ReadAllText(source)) ??
            throw new InvalidDataException("SteamVR 手柄绑定不是有效 JSON");
        MigratePhoneButtonActions(binding);
        RewriteHandPaths(binding, controllerHand);
        JsonNode? fallback = fallbackPath is null ? null : JsonNode.Parse(File.ReadAllText(fallbackPath));
        if (fallback is not null) { RewriteHandPaths(fallback, controllerHand); }
        OpenVrInputCaptureBindings.Add(binding, controllerHand, fallback);
        AtomicWrite(destination, binding.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
    }

    internal static string PrepareGuideJson(string json, OpenVrControllerHand hand)
    {
        JsonNode binding = JsonNode.Parse(json) ?? throw new InvalidDataException("Invalid binding JSON.");
        MigratePhoneButtonActions(binding);
        RewriteHandPaths(binding, hand);
        return binding.ToJsonString();
    }

    private static void MigratePhoneButtonActions(JsonNode binding)
    {
        RewritePhoneButtonOutputPaths(binding);
        if (binding["bindings"] is not JsonObject actionSets)
        {
            return;
        }

        actionSets.Remove(_pointerActionSetPath);
        actionSets.Remove(_phoneButtonStateActionSetPath);
        if (actionSets[_legacyPhoneButtonActionSetPath] is JsonObject legacyActionSet &&
            legacyActionSet["sources"] is JsonArray legacySources &&
            actionSets[_mainActionSetPath] is JsonObject mainActionSet &&
            mainActionSet["sources"] is JsonArray mainSources)
        {
            foreach (JsonNode? source in legacySources)
            {
                if (source is not null)
                {
                    mainSources.Add(source.DeepClone());
                }
            }
        }

        actionSets.Remove(_legacyPhoneButtonActionSetPath);
        WritePointerBinding(actionSets);
        WritePhoneInputStateBindings(actionSets);
    }

    private static void WritePointerBinding(JsonObject actionSets)
    {
        JsonArray pointerPoses = [];
        if (actionSets[_mainActionSetPath] is JsonObject mainActionSet &&
            mainActionSet["poses"] is JsonArray mainPoses)
        {
            foreach (JsonNode? item in mainPoses)
            {
                if (item is not JsonObject pose ||
                    pose["output"] is not JsonValue outputValue ||
                    !outputValue.TryGetValue(out string? output) ||
                    !string.Equals(
                        output,
                        "/actions/main/in/phonepointerpose",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                JsonObject pointerPose = (JsonObject)pose.DeepClone();
                pointerPose["output"] = _phonePointerActionPath;
                pointerPoses.Add(pointerPose);
            }
        }

        JsonArray dismissSources = [];
        JsonArray recallSources = [];
        if (actionSets[_mainActionSetPath]?["sources"] is JsonArray sources)
        {
            foreach (JsonNode? source in sources)
            {
                if (source?["inputs"] is not JsonObject inputs) { continue; }
                JsonObject dismissInputs = [];
                JsonObject recallInputs = [];
                foreach ((string slot, JsonNode? input) in inputs)
                {
                    if (string.Equals(input?["output"]?.GetValue<string>(), "/actions/main/in/phoneoverlaytouch", StringComparison.OrdinalIgnoreCase))
                    {
                        dismissInputs[slot] = new JsonObject { ["output"] = "/actions/pointer/in/menudismiss" };
                    }
                    else if (string.Equals(input?["output"]?.GetValue<string>(), "/actions/main/in/phonescreenshot", StringComparison.OrdinalIgnoreCase))
                    {
                        recallInputs["long"] = new JsonObject { ["output"] = "/actions/phonerecall/in/recallmenu" };
                    }
                }
                if (recallInputs.Count > 0)
                {
                    JsonNode recall = source!.DeepClone();
                    recall["inputs"] = recallInputs;
                    // Use SteamVR's native hold feedback. Only its documented
                    // hold delay owns timing; do not layer an application timer
                    // or the compositor-specific long_press_expiry parameter.
                    recall["mode"] = "button";
                    JsonObject parameters = recall["parameters"] as JsonObject ?? new JsonObject();
                    if (recall["parameters"] is not JsonObject) { recall["parameters"] = parameters; }
                    parameters.Remove("double_press_delay");
                    parameters.Remove("long_press_expiry");
                    parameters["long_press_delay"] = 2;
                    recallSources.Add(recall);
                }
                if (dismissInputs.Count == 0) { continue; }
                JsonNode copy = source!.DeepClone();
                copy["inputs"] = dismissInputs;
                dismissSources.Add(copy);
            }
        }
        actionSets[_pointerActionSetPath] = new JsonObject { ["poses"] = pointerPoses, ["sources"] = dismissSources };
        actionSets["/actions/phonerecall"] = new JsonObject { ["sources"] = recallSources };
    }

    private static void WritePhoneInputStateBindings(JsonObject actionSets)
    {
        JsonArray stateSources = [];
        if (actionSets[_mainActionSetPath] is JsonObject mainActionSet &&
            mainActionSet["sources"] is JsonArray mainSources)
        {
            foreach (JsonNode? item in mainSources)
            {
                if (item is not JsonObject source || source["path"] is null)
                {
                    continue;
                }

                HashSet<string> outputs = new(StringComparer.OrdinalIgnoreCase);
                CollectOutputPaths(source["inputs"], outputs);
                if (!outputs.Any(IsGatedPhoneInputAction))
                {
                    continue;
                }

                JsonObject stateSource = (JsonObject)source.DeepClone();
                stateSource["inputs"] = new JsonObject
                {
                    ["click"] = new JsonObject { ["output"] = _phoneButtonStateActionPath },
                };
                if (stateSource["parameters"] is JsonObject parameters)
                {
                    parameters.Remove("double_press_delay");
                    parameters.Remove("long_press_delay");
                    parameters.Remove("long_press_expiry");
                }

                stateSources.Add(stateSource);
            }
        }

        actionSets[_phoneButtonStateActionSetPath] = new JsonObject
        {
            ["sources"] = stateSources,
        };
    }

    private static void RewritePhoneButtonOutputPaths(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach ((string key, JsonNode? value) in obj.ToArray())
            {
                if (string.Equals(key, "output", StringComparison.OrdinalIgnoreCase) &&
                    value is JsonValue outputValue &&
                    outputValue.TryGetValue(out string? output) &&
                    TryMapPhoneButtonAction(output, out string mapped))
                {
                    obj[key] = mapped;
                    continue;
                }

                RewritePhoneButtonOutputPaths(value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? item in array)
            {
                RewritePhoneButtonOutputPaths(item);
            }
        }
    }

    private static bool TryMapPhoneButtonAction(string? output, out string mapped)
    {
        const string legacyPrefix = "/actions/phonebuttons/in/phone";
        if (output is not null &&
            output.StartsWith(legacyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string suffix = output[legacyPrefix.Length..].ToLowerInvariant();
            if (suffix is "back" or "home" or "recents" or "controlpanel" or "screenshot")
            {
                mapped = $"/actions/main/in/phone{suffix}";
                return true;
            }
        }

        mapped = string.Empty;
        return false;
    }

    private static void RewriteHandPaths(
        JsonNode? node,
        OpenVrControllerHand controllerHand)
    {
        if (node is JsonObject obj)
        {
            if (obj["path"] is JsonValue pathValue &&
                pathValue.TryGetValue(out string? path) &&
                path is not null)
            {
                HashSet<string> outputs = new(StringComparer.OrdinalIgnoreCase);
                CollectOutputPaths(obj, outputs);
                OpenVrControllerHand? targetHand = ResolveTargetHand(outputs, controllerHand);
                if (targetHand is not null)
                {
                    obj["path"] = MapHandPath(path, targetHand.Value);
                }
            }

            foreach ((string _, JsonNode? value) in obj.ToArray())
            {
                RewriteHandPaths(value, controllerHand);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? item in array)
            {
                RewriteHandPaths(item, controllerHand);
            }
        }
    }

    private static void CollectOutputPaths(JsonNode? node, ISet<string> outputs)
    {
        if (node is JsonObject obj)
        {
            foreach ((string key, JsonNode? value) in obj)
            {
                if (string.Equals(key, "output", StringComparison.OrdinalIgnoreCase) &&
                    value is JsonValue outputValue &&
                    outputValue.TryGetValue(out string? output) &&
                    !string.IsNullOrWhiteSpace(output))
                {
                    outputs.Add(output);
                }

                CollectOutputPaths(value, outputs);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? item in array)
            {
                CollectOutputPaths(item, outputs);
            }
        }
    }

    private static OpenVrControllerHand? ResolveTargetHand(
        HashSet<string> outputs,
        OpenVrControllerHand controllerHand)
    {
        bool phoneAction = outputs.Any(IsPhoneAction);
        bool playspaceAction = outputs.Any(IsPlayspaceAction);
        if (phoneAction == playspaceAction)
        {
            return null;
        }

        return phoneAction
            ? controllerHand
            : OpenVrControllerHandRouting.Opposite(controllerHand);
    }

    private static bool IsPhoneAction(string output) =>
        output.StartsWith("/actions/main/in/phone", StringComparison.OrdinalIgnoreCase) ||
        output.StartsWith("/actions/phonebuttons/in/phone", StringComparison.OrdinalIgnoreCase) ||
        output.StartsWith("/actions/phonebuttonstate/in/", StringComparison.OrdinalIgnoreCase) ||
        output.StartsWith("/actions/phonerecall/in/", StringComparison.OrdinalIgnoreCase) ||
        output.Equals("/actions/pointer/in/menudismiss", StringComparison.OrdinalIgnoreCase) ||
        output.StartsWith("/actions/pointer/in/phone", StringComparison.OrdinalIgnoreCase);

    private static bool IsGatedPhoneInputAction(string output) =>
        output.Equals("/actions/main/in/phoneback", StringComparison.OrdinalIgnoreCase) ||
        output.Equals("/actions/main/in/phonehome", StringComparison.OrdinalIgnoreCase) ||
        output.Equals("/actions/main/in/phonerecents", StringComparison.OrdinalIgnoreCase) ||
        output.Equals("/actions/main/in/phonecontrolpanel", StringComparison.OrdinalIgnoreCase) ||
        output.Equals("/actions/main/in/phonescreenshot", StringComparison.OrdinalIgnoreCase) ||
        output.Equals("/actions/main/in/phoneoverlaytouch", StringComparison.OrdinalIgnoreCase) ||
        output.Equals("/actions/main/in/phoneoverlaygrab", StringComparison.OrdinalIgnoreCase);

    private static bool IsPlayspaceAction(string output) =>
        output.EndsWith("spacedrag", StringComparison.OrdinalIgnoreCase) ||
        output.Equals("/actions/main/in/resetoffsets", StringComparison.OrdinalIgnoreCase);

    private static string MapHandPath(
        string path,
        OpenVrControllerHand targetHand)
    {
        const string handPrefix = "/user/hand/";
        if (!path.StartsWith(handPrefix, StringComparison.Ordinal))
        {
            return path;
        }

        int suffixIndex = path.IndexOf('/', handPrefix.Length);
        if (suffixIndex < 0)
        {
            return path;
        }

        string currentHand = path[handPrefix.Length..suffixIndex];
        string target = targetHand == OpenVrControllerHand.Left ? "left" : "right";
        if (string.Equals(currentHand, target, StringComparison.Ordinal))
        {
            return path;
        }

        if (currentHand is not "left" and not "right")
        {
            return path;
        }

        string suffix = path[suffixIndex..];
        suffix = (currentHand, target, suffix) switch
        {
            ("right", "left", "/input/a") => "/input/x",
            ("right", "left", "/input/b") => "/input/y",
            ("left", "right", "/input/x") => "/input/a",
            ("left", "right", "/input/y") => "/input/b",
            _ => suffix,
        };

        return $"{handPrefix}{target}{suffix}";
    }

    private static string ResolvePackagedBinding(string baseDirectory, string bindingUrl)
    {
        string normalized = bindingUrl.Replace('/', Path.DirectorySeparatorChar);
        string resolved = Path.GetFullPath(Path.Combine(baseDirectory, normalized));
        string prefix = Path.GetFullPath(baseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("SteamVR 默认绑定路径越出程序目录");
        }

        return resolved;
    }

    private static void EnsureSafeControllerType(string controllerType)
    {
        if (controllerType.Length == 0 || controllerType.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-'))
        {
            throw new InvalidDataException("SteamVR 手柄类型包含无效字符");
        }
    }

    private static void AtomicCopy(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + ".new";
        File.Copy(source, temporary, true);
        File.Move(temporary, destination, true);
    }

    private static void AtomicWrite(string destination, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + ".new";
        File.WriteAllText(temporary, content);
        File.Move(temporary, destination, true);
    }
}
