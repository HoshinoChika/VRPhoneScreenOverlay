using VRPhoneScreenOverlay.App;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class ControllerBindingGuideTests
{
    [Fact]
    public async Task NewerBackupCannotOverrideTheCanonicalBindingUsedByRuntime()
    {
        using Fixture fixture = new();
        string canonical = await Fixture.Save("pico_controller", fixture.Saved);
        string original = await File.ReadAllTextAsync(canonical);
        File.SetLastWriteTimeUtc(canonical, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await File.WriteAllTextAsync(Path.Combine(fixture.Saved, "backup.json"), original.Replace("/input/grip", "/input/trigger", StringComparison.Ordinal));
        ControllerBindingGuide guide = await fixture.Read("pico_controller");
        Assert.Contains(("抓握", "右手 握把"), ControllerBindingInstructions.BuildLines(guide));
        OpenVrBindingProfileStore store = new(Path.Combine(AppContext.BaseDirectory, "action_manifest.json"),
            fixture.Saved, Path.Combine(fixture.Saved, "runtime"));
        OpenVrPreparedInputManifest prepared = store.Prepare();
        string runtimePath = prepared.Bindings.Single(profile => profile.ControllerType == "pico_controller").RuntimePath;
        ControllerBindingGuide running = OpenVrBindingGuide.ParseDefault("pico_controller", await File.ReadAllTextAsync(runtimePath));
        Assert.Equal(guide.Entries.Where(entry => entry.Action == "抓握"), running.Entries.Where(entry => entry.Action == "抓握"));
        Assert.Equal(original, await File.ReadAllTextAsync(canonical));
    }

    [Fact]
    public async Task EveryPackagedControllerHasReadableFunctionInstructions()
    {
        foreach (ControllerBindingChoice choice in OpenVrBindingGuide.Choices)
        {
            ControllerBindingGuide guide = await OpenVrBindingGuide.ReadDefaultAsync(choice.Key,
                OpenVrControllerHand.Right, CancellationToken.None);
            Assert.NotEmpty(guide.Entries);
            Assert.Equal(choice.ControllerType, guide.ControllerType);
            Assert.Contains(guide.Entries, entry => entry.Action == "抓握");
            Assert.Contains(guide.Entries, entry => entry.Action == "触屏点击");
            Assert.Equal(guide.Entries.Where(entry => entry.Action == "截屏")
                .Select(entry => (entry.DevicePath, entry.InputPath)),
                guide.Entries.Where(entry => entry.Action == "隐藏后唤回")
                .Select(entry => (entry.DevicePath, entry.InputPath)));
        }
    }

    [Fact]
    public async Task UnverifiedCompatibilityProfileStaysReadableWithoutAdvertisingAPicoProduct()
    {
        Assert.DoesNotContain(OpenVrBindingGuide.Choices, choice => choice.ControllerType == "pico_controller_ice");
        using Fixture fixture = new();
        string path = await Fixture.Save("pico_controller_ice", fixture.Saved);
        string original = await File.ReadAllTextAsync(path);
        ControllerBindingGuide guide = await fixture.Read(OpenVrBindingGuide.CurrentBindingKey("pico_controller_ice"));
        Assert.Equal("pico_controller_ice", guide.ControllerType);
        Assert.Equal("已保存绑定", guide.Source);
        Assert.Contains(guide.Entries, entry => entry.InputPath.Contains("trackpad", StringComparison.Ordinal));
        Assert.Equal(original, await File.ReadAllTextAsync(path));
        ControllerBindingGuide product = await OpenVrBindingGuide.ReadDefaultAsync("pico_controller", OpenVrControllerHand.Right, CancellationToken.None);
        Assert.Contains(product.Entries, entry => entry.InputPath.Contains("joystick", StringComparison.Ordinal));
        Assert.DoesNotContain(product.Entries, entry => entry.InputPath.Contains("trackpad", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RiftProductSelectionReadsTheCanonicalSavedBindingWithoutCreatingASecondFile()
    {
        using Fixture fixture = new();
        string path = await Fixture.Save("oculus_touch", fixture.Saved);
        string before = await File.ReadAllTextAsync(path);
        ControllerBindingGuide canonical = await fixture.Read("oculus_touch");
        ControllerBindingGuide rift = await fixture.Read("oculus_rift");
        Assert.Equal("oculus_touch", rift.ControllerType);
        Assert.Equal("已保存绑定", rift.Source);
        Assert.Equal(canonical.Entries, rift.Entries);
        Assert.Equal(before, await File.ReadAllTextAsync(path));
        Assert.Single(Directory.GetFiles(fixture.Saved, "*.json"));
    }

    [Theory]
    [InlineData("wmr_lenovo")]
    [InlineData("wmr_dell")]
    public async Task DifferentWmrProductBrandsReadTheSameCanonicalMapUsingSeparateSelectionKeys(string key)
    {
        ControllerBindingChoice choice = Assert.Single(OpenVrBindingGuide.Choices, choice => choice.Key == key);
        ControllerBindingGuide canonical = await OpenVrBindingGuide.ReadDefaultAsync("holographic_controller", OpenVrControllerHand.Right, CancellationToken.None);
        ControllerBindingGuide selected = await OpenVrBindingGuide.ReadDefaultAsync(choice.Key, OpenVrControllerHand.Right, CancellationToken.None);
        Assert.Equal("holographic_controller", selected.ControllerType);
        Assert.Equal(canonical.Entries, selected.Entries);
    }

    [Fact]
    public async Task NoSavedBindingsDefaultsToPicoWithoutStartingSteamVr()
    {
        using Fixture fixture = new();
        ControllerBindingGuide guide = await fixture.Read();
        Assert.Equal("pico_controller", guide.ControllerType);
        Assert.Equal("默认绑定", guide.Source);
        Assert.Contains(("抓握", "右手 握把"), ControllerBindingInstructions.BuildLines(guide));
        Assert.Contains(("触屏点击", "右手 扳机"), ControllerBindingInstructions.BuildLines(guide));
        Assert.Contains(("隐藏后唤回", "右手 摇杆（长按 2 秒） → 直接显示手机"), ControllerBindingInstructions.BuildLines(guide));
    }

    [Fact]
    public async Task NewestSavedTypeCannotOverrideTheDisconnectedPicoGroupAndExplicitCustomBindingsRemainReadable()
    {
        using Fixture fixture = new();
        string old = await Fixture.Save("pico_controller", fixture.Saved);
        File.SetLastWriteTimeUtc(old, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        string selected = await Fixture.Save("knuckles", fixture.Saved);
        string json = await File.ReadAllTextAsync(selected);
        json = json.Replace("/input/grip", "/input/trigger", StringComparison.Ordinal);
        await File.WriteAllTextAsync(selected, json);
        await Fixture.Save("oculus_touch", fixture.Cached);
        await File.WriteAllTextAsync(Path.Combine(fixture.Saved, "newer-invalid.json"), "[]");
        ControllerBindingGuide guide = await fixture.Read();
        Assert.Equal("pico_controller", guide.ControllerType);
        Assert.Equal("已保存绑定", guide.Source);
        ControllerBindingGuide explicitIndex = await fixture.Read("knuckles");
        Assert.Contains(explicitIndex.Entries, entry => entry.Action == "抓握" && entry.InputPath == "/trigger");
        Assert.Equal(json, await File.ReadAllTextAsync(selected));
        ControllerBindingGuide explicitPico = await fixture.Read("pico_controller");
        Assert.Equal("pico_controller", explicitPico.ControllerType);
        Assert.Equal("已保存绑定", explicitPico.Source);
    }

    [Fact]
    public async Task CacheIsUsedWhenSavedFileIsDamagedAndHandMappingMatchesRuntime()
    {
        using Fixture fixture = new();
        string saved = await Fixture.Save("pico_controller", fixture.Saved);
        await File.WriteAllTextAsync(saved, "{broken");
        await Fixture.Save("pico_controller", fixture.Cached);
        ControllerBindingGuide guide = await fixture.Read("", OpenVrControllerHand.Left);
        Assert.Equal("已保存绑定 · 缓存", guide.Source);
        Assert.Contains(("触屏点击", "左手 扳机"), ControllerBindingInstructions.BuildLines(guide));
        Assert.Contains(("桌面", "左手 Y 键（长按）"), ControllerBindingInstructions.BuildLines(guide));
    }

    [Fact]
    public void RecallInstructionsFollowCustomScreenshotBindingIncludingGestureAndHand()
    {
        string json = """
            { "bindings": { "/actions/main": { "sources": [
                { "path": "/user/hand/right/input/a", "mode": "button", "inputs": {
                    "long": { "output": "/actions/main/in/phonescreenshot" }
                } }
            ] } } }
            """;
        ControllerBindingGuide guide = OpenVrBindingGuide.ParseDefault("test",
            OpenVrBindingProfileStore.PrepareGuideJson(json, OpenVrControllerHand.Left));
        var lines = ControllerBindingInstructions.BuildLines(guide);
        Assert.Contains(("截屏", "左手 X 键（长按）"), lines);
        Assert.Contains(("隐藏后唤回", "左手 X 键（长按 2 秒） → 直接显示手机"), lines);
        Assert.DoesNotContain(guide.Entries, entry => entry.InputPath.Contains("joystick", StringComparison.Ordinal));
    }

    [Fact]
    public void TextRetainsLongPressMultipleBindingsAndUnboundFunctions()
    {
        ControllerBindingGuide guide = OpenVrBindingGuide.ParseDefault("test", """
            { "bindings": { "/actions/main": { "sources": [
                { "path": "/user/hand/left/input/a", "mode": "button", "inputs": {
                    "click": { "output": "/actions/main/in/phoneback" },
                    "long": { "output": "/actions/main/in/phonehome" }
                } },
                { "path": "/user/hand/right/input/b", "mode": "button", "inputs": {
                    "double": { "output": "/actions/main/in/phoneback" }
                } }
            ] } } }
            """);
        var lines = ControllerBindingInstructions.BuildLines(guide);
        Assert.Contains(("返回", "左手 A 键 / 右手 B 键（双击）"), lines);
        Assert.Contains(("桌面", "左手 A 键（长按）"), lines);
        Assert.Contains(("抓握", "未绑定"), lines);
        Assert.Contains(("隐藏后唤回", "未绑定"), lines);
    }

    [Fact]
    public void HeightResetKeepsTheExistingCustomResetActionAndGesture()
    {
        ControllerBindingGuide guide = OpenVrBindingGuide.ParseDefault("test", """
            { "bindings": { "/actions/main": { "sources": [
                { "path": "/user/hand/left/input/a", "mode": "button", "inputs": {
                    "double": { "output": "/actions/main/in/ResetOffsets" }
                } }
            ] } } }
            """);
        Assert.Contains(("重置空间", "左手 A 键（双击） · 按所选重置方式"), ControllerBindingInstructions.BuildLines(guide));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"vrpso-guide-{Guid.NewGuid():N}");
        public string Saved => Path.Combine(_root, "saved");
        public string Cached => Path.Combine(_root, "cached");
        public static async Task<string> Save(string type, string directory)
        {
            Directory.CreateDirectory(directory);
            string fileName = Path.GetFileName(directory) == "cached" ? $"{type}.json" : $"{OpenVrInputManifest.ApplicationKey}_{type}.json";
            string path = Path.Combine(directory, fileName);
            await File.WriteAllTextAsync(path, await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "bindings", type + ".json")));
            return path;
        }
        public ValueTask<ControllerBindingGuide> Read(string type = "", OpenVrControllerHand hand = OpenVrControllerHand.Right) =>
            OpenVrBindingGuide.ReadAsync(type, hand, Saved, Cached, AppContext.BaseDirectory, CancellationToken.None);
        public void Dispose() { if (Directory.Exists(_root)) { Directory.Delete(_root, true); } }
    }
}
