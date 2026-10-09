using System.Text.Json;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OpenVrBindingUiTests
{
    private static readonly string[] _systemButtonActions =
    [
        "/actions/main/in/PhoneBack",
        "/actions/main/in/PhoneHome",
        "/actions/main/in/PhoneRecents",
        "/actions/main/in/PhoneControlPanel",
        "/actions/main/in/PhoneScreenshot",
    ];

    [Fact]
    public void SystemButtonActionsAreVisibleSuggestedBindings()
    {
        string repository = FindRepositoryRoot();
        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(repository, "action_manifest.json")));
        JsonElement[] actions = manifest.RootElement
            .GetProperty("actions")
            .EnumerateArray()
            .ToArray();

        foreach (string expectedName in _systemButtonActions)
        {
            JsonElement action = Assert.Single(actions, candidate =>
                string.Equals(
                    candidate.GetProperty("name").GetString(),
                    expectedName,
                    StringComparison.OrdinalIgnoreCase));
            Assert.Equal("suggested", action.GetProperty("requirement").GetString());
        }

        JsonElement mainSet = Assert.Single(
            manifest.RootElement.GetProperty("action_sets").EnumerateArray(),
            candidate => string.Equals(
                candidate.GetProperty("name").GetString(),
                "/actions/main",
                StringComparison.OrdinalIgnoreCase));
        Assert.Equal("leftright", mainSet.GetProperty("usage").GetString());
        JsonElement pointerSet = Assert.Single(
            manifest.RootElement.GetProperty("action_sets").EnumerateArray(),
            candidate => string.Equals(
                candidate.GetProperty("name").GetString(),
                "/actions/pointer",
                StringComparison.OrdinalIgnoreCase));
        JsonElement stateSet = Assert.Single(
            manifest.RootElement.GetProperty("action_sets").EnumerateArray(),
            candidate => string.Equals(
                candidate.GetProperty("name").GetString(),
                "/actions/phonebuttonstate",
                StringComparison.OrdinalIgnoreCase));
        Assert.Equal("hidden", pointerSet.GetProperty("usage").GetString());
        Assert.Equal("hidden", stateSet.GetProperty("usage").GetString());
        JsonElement captureSet = Assert.Single(
            manifest.RootElement.GetProperty("action_sets").EnumerateArray(),
            candidate => candidate.GetProperty("name").GetString() == "/actions/phonecapture");
        Assert.Equal("hidden", captureSet.GetProperty("usage").GetString());
        JsonElement recallSet = Assert.Single(
            manifest.RootElement.GetProperty("action_sets").EnumerateArray(),
            candidate => candidate.GetProperty("name").GetString() == "/actions/phonerecall");
        Assert.Equal("hidden", recallSet.GetProperty("usage").GetString());
        Assert.Single(actions, candidate => candidate.GetProperty("name").GetString()!.StartsWith("/actions/phonerecall/", StringComparison.Ordinal));
        Assert.Equal(5, manifest.RootElement.GetProperty("action_sets").GetArrayLength());
    }

    [Fact]
    public void EveryPackagedControllerBindingIncludesEverySystemButtonAction()
    {
        string repository = FindRepositoryRoot();
        foreach (string bindingPath in Directory.EnumerateFiles(
                     Path.Combine(repository, "bindings"),
                     "*.json",
                     SearchOption.TopDirectoryOnly))
        {
            string binding = File.ReadAllText(bindingPath);
            foreach (string expectedName in _systemButtonActions)
            {
                Assert.Contains(expectedName, binding, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "action_manifest.json")) &&
                Directory.Exists(Path.Combine(directory.FullName, "bindings")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }
}
