using System.Text.Json;

namespace VRPhoneScreenOverlay.SteamVR;

public sealed record ControllerBindingEntry(string Action, string DevicePath, string InputPath, string Mode, string Slot);
public sealed record ControllerBindingGuide(string ControllerType, string Source, string Message, IReadOnlyList<ControllerBindingEntry> Entries,
    string? SelectionKey = null, bool Unrecognized = false);
public sealed record ControllerBindingChoice(string ControllerType, string Label, string? SelectionKey = null)
{
    public string Key => SelectionKey ?? ControllerType;
}

public static class OpenVrBindingGuide
{
    public const string GenericKey = "generic";
    public static IReadOnlyList<ControllerBindingChoice> Choices { get; } = Array.AsReadOnly<ControllerBindingChoice>(
    [
        // Only retail products with recorded sales evidence belong in this catalog.
        // See docs/plans/VERIFIED_VR_PRODUCTS_2026-10-08.md. Compatibility profiles
        // such as pico_controller_ice remain readable without inventing a product.
        new("pico_controller", "PICO Neo3 / 4 / 4 Pro / 4 Ultra"),
        new("oculus_touch", "Meta Quest 2 / 3 / 3S / Pro"),
        new("oculus_touch", "Oculus Rift / Rift S", "oculus_rift"),
        new("knuckles", "Valve Index"),
        new("vive_controller", "HTC VIVE / VIVE Pro"),
        new("holographic_controller", "Samsung Odyssey / Odyssey+"),
        new("holographic_controller", "Lenovo Explorer", "wmr_lenovo"),
        new("holographic_controller", "Dell Visor", "wmr_dell"),
        new("hpmotioncontroller", "HP Reverb G2"),
        new(GenericKey, "通用"),
    ]);

    public static string CurrentBindingKey(string controllerType) => "current:" + controllerType;

    public static string ResolveControllerType(string selectionKey) =>
        selectionKey.StartsWith("current:", StringComparison.Ordinal) ? selectionKey[8..] :
            Choices.FirstOrDefault(choice => choice.Key == selectionKey)?.ControllerType ?? selectionKey;

    public static ValueTask<ControllerBindingGuide> ReadAsync(string controllerType, CancellationToken cancellationToken) =>
        ReadAsync(controllerType, OpenVrControllerPreferences.Load(),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "steamvr", "input"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRPhoneScreenOverlay", "steamvr", "user-bindings"),
            Path.GetDirectoryName(OpenVrBuiltInBindings.EnsureSourceFiles(OpenVrBuiltInBindings.SourceDirectory))!, cancellationToken);

    internal static async ValueTask<ControllerBindingGuide> ReadAsync(string controllerType, OpenVrControllerHand hand,
        string savedDirectory, string cachedDirectory, string packagedDirectory, CancellationToken cancellationToken)
    {
        controllerType = ResolveControllerType(controllerType);
        if (controllerType == GenericKey)
        {
            return await ReadDefaultAsync(GenericKey, hand, cancellationToken).ConfigureAwait(false);
        }
        if (controllerType.Length == 0) { controllerType = "pico_controller"; }
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            OpenVrBindingProfileStore store = new(Path.Combine(packagedDirectory, "action_manifest.json"),
                savedDirectory, Path.GetDirectoryName(cachedDirectory)!, hand);
            IReadOnlyList<OpenVrSelectedBindingProfile> profiles = store.ResolveProfiles(cachedDirectory);
            OpenVrSelectedBindingProfile? selected = profiles.FirstOrDefault(profile => profile.ControllerType == controllerType);
            if (selected is not null)
            {
                string json = await File.ReadAllTextAsync(selected.Path, cancellationToken).ConfigureAwait(false);
                return ParseDefault(selected.ControllerType, OpenVrBindingProfileStore.PrepareGuideJson(json, hand)) with
                {
                    Source = selected.Source switch
                    {
                        OpenVrBindingProfileSource.SavedUser => "已保存绑定",
                        OpenVrBindingProfileSource.CachedUser => "已保存绑定 · 缓存",
                        _ => "默认绑定",
                    },
                };
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return new(controllerType, "绑定读取失败", "暂时无法确认绑定来源，请刷新重试", []);
        }
        return await ReadDefaultAsync(controllerType.Length == 0 ? "pico_controller" : controllerType,
            hand, cancellationToken, packagedDirectory).ConfigureAwait(false);
    }
    // Snapshots use only packaged data, never the user's saved files or SteamVR.
    public static async ValueTask<ControllerBindingGuide> ReadDefaultAsync(string controllerType, OpenVrControllerHand hand,
        CancellationToken cancellationToken, string? packagedDirectory = null)
    {
        controllerType = ResolveControllerType(controllerType);
        if (controllerType == GenericKey)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ParseDefault(GenericKey, OpenVrBindingProfileStore.PrepareGuideJson(OpenVrGenericBinding.Create(GenericKey), hand))
                with
            { Source = "通用默认绑定", SelectionKey = GenericKey };
        }
        try
        {
            if (packagedDirectory is null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return OpenVrBuiltInBindings.Bindings.TryGetValue(controllerType, out string? compiled)
                    ? ParseDefault(controllerType, OpenVrBindingProfileStore.PrepareGuideJson(compiled, hand))
                    : new(controllerType, "默认绑定", "此型号没有内置默认说明", []);
            }
            string directory = packagedDirectory;
            using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
                Path.Combine(directory, "action_manifest.json"), cancellationToken).ConfigureAwait(false));
            foreach (JsonElement binding in manifest.RootElement.GetProperty("default_bindings").EnumerateArray())
            {
                if (binding.GetProperty("controller_type").GetString() != controllerType) { continue; }
                string path = Path.GetFullPath(Path.Combine(directory, binding.GetProperty("binding_url").GetString()!));
                if (!path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Invalid packaged binding path.");
                }
                string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                return ParseDefault(controllerType, OpenVrBindingProfileStore.PrepareGuideJson(json, hand));
            }
            return new(controllerType, "默认绑定", "此型号没有随包默认说明", []);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return new(controllerType, "绑定读取失败", "暂时无法读取说明，请刷新重试", []);
        }
    }

    internal static ControllerBindingGuide ParseDefault(string controllerType, string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        List<ControllerBindingEntry> entries = [];
        foreach (JsonProperty actionSet in document.RootElement.GetProperty("bindings").EnumerateObject())
        {
            if (actionSet.Name is not "/actions/main" and not "/actions/phonerecall" ||
                !actionSet.Value.TryGetProperty("sources", out JsonElement sources)) { continue; }
            foreach (JsonElement source in sources.EnumerateArray())
            {
                string path = source.GetProperty("path").GetString()!;
                foreach (JsonProperty input in source.GetProperty("inputs").EnumerateObject())
                {
                    string action = input.Value.GetProperty("output").GetString()!;
                    string? label = ActionLabel(action);
                    if (label is null) { continue; }
                    entries.Add(new(label, path.Contains("/left/", StringComparison.Ordinal) ? "/user/hand/left" : "/user/hand/right",
                        path[(path.LastIndexOf("/input/", StringComparison.Ordinal) + 6)..],
                        source.GetProperty("mode").GetString()!, input.Name));
                }
            }
        }
        return new(controllerType, "默认绑定", "", entries);
    }

    private static string? ActionLabel(string action) => action.Split('/')[^1].ToLowerInvariant() switch
    {
        "lefthandspacedrag" or "righthandspacedrag" => "空间拖拽",
        "resetoffsets" => "重置空间",
        "phoneback" => "返回",
        "phonehome" => "桌面",
        "phonerecents" => "最近任务",
        "phonecontrolpanel" => "控制栏",
        "phonescreenshot" => "截屏",
        "recallmenu" => "隐藏后唤回",
        "phoneoverlaygrab" => "抓握",
        "phoneoverlayscale" => "缩放 / 滚动",
        "phoneoverlaytouch" => "触屏点击",
        _ => null,
    };
}
