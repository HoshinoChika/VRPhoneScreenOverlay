using System.Text.Json;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class BindingDefaultsTests
{
    [Fact]
    public async Task ResetRestoresEveryPackagedTypeAndRemovesAppOwnedExtrasOnly()
    {
        using Fixture fixture = new();
        await fixture.CustomizeAsync();
        await using (IBindingDefaultsTransaction transaction = await fixture.Store.BeginResetAsync(CancellationToken.None))
        {
            foreach ((string type, byte[] packaged) in fixture.Defaults)
            {
                Assert.Equal(packaged, await File.ReadAllBytesAsync(fixture.SavedPath(type)));
                Assert.Equal(packaged, await File.ReadAllBytesAsync(Path.Combine(fixture.Cached, type + ".json")));
                using JsonDocument runtime = JsonDocument.Parse(await File.ReadAllTextAsync(transaction.RuntimeBindings[type]));
                Assert.True(runtime.RootElement.GetProperty("bindings").TryGetProperty("/actions/phonerecall", out _));
                Assert.True(runtime.RootElement.GetProperty("bindings").TryGetProperty("/actions/phonecapture", out _));
            }
            Assert.False(File.Exists(Path.Combine(fixture.Saved, "old-user-copy.json")));
            Assert.False(File.Exists(Path.Combine(fixture.Saved, "unicode-user-copy.json")));
            Assert.False(File.Exists(Path.Combine(fixture.Saved, "unknown-controller.json")));
            Assert.False(File.Exists(Path.Combine(fixture.Cached, "unknown-controller.json")));
            Assert.False(File.Exists(Path.Combine(fixture.RuntimeBindings, "unknown-controller.json")));
            using JsonDocument unknown = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.SavedPath("unknown_controller")));
            Assert.Equal("unknown_controller", unknown.RootElement.GetProperty("controller_type").GetString());
            using JsonDocument pico = JsonDocument.Parse(System.Text.Encoding.UTF8.GetString(fixture.Defaults["pico_controller"]));
            Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
                System.Text.Json.Nodes.JsonNode.Parse(pico.RootElement.GetProperty("bindings").GetRawText()),
                System.Text.Json.Nodes.JsonNode.Parse(unknown.RootElement.GetProperty("bindings").GetRawText())));
            Assert.Contains("unknown_controller", transaction.RuntimeBindings.Keys);
            Assert.Equal("{\"app_key\":\"another.app\",\"controller_type\":\"other\"}", await File.ReadAllTextAsync(Path.Combine(fixture.Saved, "other-app.json")));
            transaction.Commit();
        }
        Assert.Equal(fixture.Defaults["pico_controller"], await File.ReadAllBytesAsync(fixture.SavedPath("pico_controller")));
    }

    [Fact]
    public async Task UncommittedResetRestoresEveryOriginalByteAndDeletesNewDefaults()
    {
        using Fixture fixture = new();
        await fixture.CustomizeAsync();
        Dictionary<string, byte[]> before = fixture.ReadState();
        await using (IBindingDefaultsTransaction transaction = await fixture.Store.BeginResetAsync(CancellationToken.None)) { }
        AssertState(before, fixture.ReadState());
    }

    [Fact]
    public async Task LockedFileAfterAnEarlierWriteRollsBackTheEarlierWrite()
    {
        using Fixture fixture = new();
        await fixture.CustomizeAsync();
        Dictionary<string, byte[]> before = fixture.ReadState();
        using (FileStream locked = new(Path.Combine(fixture.Cached, "pico_controller.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Exception? failure = await Record.ExceptionAsync(async () => await fixture.Store.BeginResetAsync(CancellationToken.None));
            Assert.True(failure is IOException or UnauthorizedAccessException);
        }
        AssertState(before, fixture.ReadState());
    }

    [Fact]
    public async Task InvalidPackagedDefaultsAreRejectedBeforeChangingAnyUserFile()
    {
        using Fixture fixture = new();
        await fixture.CustomizeAsync();
        Dictionary<string, byte[]> before = fixture.ReadState();
        await File.WriteAllTextAsync(Path.Combine(fixture.Package, "bindings", "pico_controller.json"), "{}");
        await Assert.ThrowsAsync<InvalidDataException>(async () => await fixture.Store.BeginResetAsync(CancellationToken.None));
        AssertState(before, fixture.ReadState());
    }

    [Fact]
    public async Task ACanonicalFilenameCannotAuthorizeOverwritingAnotherApplicationsBinding()
    {
        using Fixture fixture = new();
        await fixture.CustomizeAsync();
        await File.WriteAllTextAsync(fixture.SavedPath("pico_controller"), "{\"app_key\":\"another.app\",\"controller_type\":\"pico_controller\"}");
        Dictionary<string, byte[]> before = fixture.ReadState();
        await Assert.ThrowsAsync<InvalidDataException>(async () => await fixture.Store.BeginResetAsync(CancellationToken.None));
        AssertState(before, fixture.ReadState());
    }

    [Fact]
    public async Task CancellationDoesNotCommitOrAlterAnyBinding()
    {
        using Fixture fixture = new();
        await fixture.CustomizeAsync();
        Dictionary<string, byte[]> before = fixture.ReadState();
        using CancellationTokenSource stop = new();
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await fixture.Store.BeginResetAsync(stop.Token));
        AssertState(before, fixture.ReadState());
    }

    private static void AssertState(Dictionary<string, byte[]> expected, Dictionary<string, byte[]> actual)
    {
        Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
        foreach ((string path, byte[] bytes) in expected) { Assert.Equal(bytes, actual[path]); }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(AppContext.BaseDirectory, "binding-defaults-test", Guid.NewGuid().ToString("N"));
        public string Package => Path.Combine(_root, "package");
        public string Saved => Path.Combine(_root, "saved");
        public string Runtime => Path.Combine(_root, "runtime");
        public string Cached => Path.Combine(Runtime, "user-bindings");
        public string RuntimeBindings => Path.Combine(Runtime, "bindings");
        public Dictionary<string, byte[]> Defaults { get; } = new(StringComparer.Ordinal);
        public OpenVrBindingDefaults Store => new(Path.Combine(Package, "action_manifest.json"), Saved, Runtime, OpenVrControllerHand.Right);
        public string SavedPath(string type) => Path.Combine(Saved, "local.spacedraglite.desktop.v1_" + type + ".json");

        public Fixture()
        {
            Directory.CreateDirectory(Path.Combine(Package, "bindings"));
            File.Copy(Path.Combine(AppContext.BaseDirectory, "action_manifest.json"), Path.Combine(Package, "action_manifest.json"));
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Package, "action_manifest.json")));
            foreach (JsonElement binding in manifest.RootElement.GetProperty("default_bindings").EnumerateArray())
            {
                string type = binding.GetProperty("controller_type").GetString()!;
                string relative = binding.GetProperty("binding_url").GetString()!;
                byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, relative));
                Defaults.Add(type, bytes);
                File.WriteAllBytes(Path.Combine(Package, relative), bytes);
            }
        }

        public async Task CustomizeAsync()
        {
            Directory.CreateDirectory(Saved);
            Directory.CreateDirectory(Cached);
            Directory.CreateDirectory(RuntimeBindings);
            string original = System.Text.Encoding.UTF8.GetString(Defaults["pico_controller"]);
            string custom = original.Replace("/input/grip", "/input/trigger", StringComparison.Ordinal);
            await File.WriteAllTextAsync(SavedPath("pico_controller"), custom);
            await File.WriteAllTextAsync(Path.Combine(Cached, "pico_controller.json"), custom);
            await File.WriteAllTextAsync(Path.Combine(RuntimeBindings, "pico_controller.json"), custom);
            await File.WriteAllTextAsync(Path.Combine(Runtime, "action_manifest.json"), "{\"old\":true}");
            await File.WriteAllTextAsync(Path.Combine(Saved, "old-user-copy.json"), custom);
            await File.WriteAllTextAsync(Path.Combine(Saved, "unicode-user-copy.json"), custom, System.Text.Encoding.Unicode);
            string unknown = custom.Replace("\"controller_type\": \"pico_controller\"", "\"controller_type\": \"unknown_controller\"", StringComparison.Ordinal);
            foreach (string directory in new[] { Saved, Cached, RuntimeBindings })
            { await File.WriteAllTextAsync(Path.Combine(directory, "unknown-controller.json"), unknown); }
            await File.WriteAllTextAsync(Path.Combine(Saved, "other-app.json"), "{\"app_key\":\"another.app\",\"controller_type\":\"other\"}");
        }

        public Dictionary<string, byte[]> ReadState() => Directory.GetFiles(_root, "*.json", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(Package + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(path => Path.GetRelativePath(_root, path), File.ReadAllBytes, StringComparer.Ordinal);

        public void Dispose()
        {
            string safeRoot = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(_root).StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase)) { throw new InvalidOperationException("Unexpected test root."); }
            if (Directory.Exists(_root)) { Directory.Delete(_root, true); }
        }
    }
}
