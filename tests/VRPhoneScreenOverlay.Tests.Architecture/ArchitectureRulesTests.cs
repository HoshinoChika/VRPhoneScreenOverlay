using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace VRPhoneScreenOverlay.Tests.Architecture;

public sealed class ArchitectureRulesTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> _allowedReferences =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["VRPhoneScreenOverlay.Contracts"] = Set(),
            ["VRPhoneScreenOverlay.Protocols"] = Set(),
            ["VRPhoneScreenOverlay.Presentation"] = Set(),
            ["VRPhoneScreenOverlay.Core"] = Set("VRPhoneScreenOverlay.Contracts"),
            ["VRPhoneScreenOverlay.Settings"] = Set(),
            ["VRPhoneScreenOverlay.Android"] = Set("VRPhoneScreenOverlay.Contracts"),
            ["VRPhoneScreenOverlay.Media"] = Set("VRPhoneScreenOverlay.Contracts"),
            ["VRPhoneScreenOverlay.Input"] = Set("VRPhoneScreenOverlay.Contracts"),
            ["VRPhoneScreenOverlay.SteamVR"] = Set(
                "VRPhoneScreenOverlay.Contracts",
                "VRPhoneScreenOverlay.Presentation",
                "VRPhoneScreenOverlay.Media"),
            ["VRPhoneScreenOverlay.SteamVR.BindingTool"] = Set(
                "VRPhoneScreenOverlay.SteamVR"),
            ["VRPhoneScreenOverlay.Network"] = Set(
                "VRPhoneScreenOverlay.Contracts",
                "VRPhoneScreenOverlay.Protocols"),
            ["VRPhoneScreenOverlay.Sharing"] = Set(
                "VRPhoneScreenOverlay.Contracts",
                "VRPhoneScreenOverlay.Protocols",
                "VRPhoneScreenOverlay.Media",
                "VRPhoneScreenOverlay.Network"),
            ["VRPhoneScreenOverlay.PhotoSync"] = Set(
                "VRPhoneScreenOverlay.Contracts",
                "VRPhoneScreenOverlay.Android"),
            ["VRPhoneScreenOverlay.Update"] = Set(
                "VRPhoneScreenOverlay.Contracts",
                "VRPhoneScreenOverlay.Protocols",
                "VRPhoneScreenOverlay.Network"),
            ["VRPhoneScreenOverlay.Diagnostics"] = Set(
                "VRPhoneScreenOverlay.Contracts",
                "VRPhoneScreenOverlay.Protocols",
                "VRPhoneScreenOverlay.Network"),
            ["VRPhoneScreenOverlay.App"] = Set(
                "VRPhoneScreenOverlay.Presentation",
                "VRPhoneScreenOverlay.Android",
                "VRPhoneScreenOverlay.Contracts",
                "VRPhoneScreenOverlay.Core",
                "VRPhoneScreenOverlay.Diagnostics",
                "VRPhoneScreenOverlay.Input",
                "VRPhoneScreenOverlay.Media",
                "VRPhoneScreenOverlay.Network",
                "VRPhoneScreenOverlay.PhotoSync",
                "VRPhoneScreenOverlay.Protocols",
                "VRPhoneScreenOverlay.Session",
                "VRPhoneScreenOverlay.Sharing",
                "VRPhoneScreenOverlay.Settings",
                "VRPhoneScreenOverlay.SteamVR",
                "VRPhoneScreenOverlay.Update"),
            ["VRPhoneScreenOverlay.Maintenance"] = Set(
                "VRPhoneScreenOverlay.Contracts",
                "VRPhoneScreenOverlay.Protocols",
                "VRPhoneScreenOverlay.Network",
                "VRPhoneScreenOverlay.Update",
                "VRPhoneScreenOverlay.Diagnostics"),
            ["VRPhoneScreenOverlay.Service"] = Set(
                "VRPhoneScreenOverlay.Protocols"),
            ["VRPhoneScreenOverlay.Session"] = Set(
                "VRPhoneScreenOverlay.Settings",
                "VRPhoneScreenOverlay.Android",
                "VRPhoneScreenOverlay.Contracts",
                "VRPhoneScreenOverlay.Input",
                "VRPhoneScreenOverlay.Media",
                "VRPhoneScreenOverlay.SteamVR"),
        };

    /// <summary>
    /// Every project under src/ must be listed in <see cref="_allowedReferences"/>.
    /// </summary>
    /// <remarks>
    /// Without this the reference whitelist is opt-in: it iterates the dictionary, so a project
    /// that nobody remembered to register is never checked at all and silently escapes every
    /// boundary rule. Adding a project must be a deliberate act, not a default exemption.
    /// </remarks>
    [Fact]
    public void EveryProductionProjectIsGovernedByTheReferenceWhitelist()
    {
        string root = FindRepositoryRoot();
        string[] projectsOnDisk = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToArray();

        string[] ungoverned = projectsOnDisk
            .Where(name => !_allowedReferences.ContainsKey(name))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(
            ungoverned.Length == 0,
            "These projects are not in the reference whitelist and are therefore unchecked: " +
            $"{string.Join(", ", ungoverned)}. Add them to _allowedReferences.");

        string[] stale = _allowedReferences.Keys
            .Where(name => !projectsOnDisk.Contains(name, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(
            stale.Length == 0,
            $"These whitelist entries no longer exist on disk: {string.Join(", ", stale)}.");
    }

    [Fact]
    public void ProductionLibrariesOnlyUseApprovedProjectReferences()
    {
        string root = FindRepositoryRoot();
        foreach ((string projectName, IReadOnlySet<string> allowed) in _allowedReferences)
        {
            string projectPath = Path.Combine(root, "src", projectName, $"{projectName}.csproj");
            XDocument project = XDocument.Load(projectPath);
            string[] actual = project
                .Descendants("ProjectReference")
                .Select(reference => Path.GetFileNameWithoutExtension(
                    reference.Attribute("Include")?.Value))
                .Where(reference => !string.IsNullOrWhiteSpace(reference))
                .Cast<string>()
                .ToArray();

            string[] forbidden = actual.Where(reference => !allowed.Contains(reference)).ToArray();
            Assert.True(
                forbidden.Length == 0,
                $"{projectName} has forbidden references: {string.Join(", ", forbidden)}");
        }
    }

    [Fact]
    public void CoreAndContractsDoNotContainPlatformImplementationTerms()
    {
        string root = FindRepositoryRoot();
        string[] forbiddenTerms =
        [
            "System.Windows.Forms",
            "Valve.VR",
            "DllImport",
            "HttpClient",
            "adb.exe",
            "scrcpy",
            "PrintWindow",
        ];

        foreach (string projectName in new[]
                 {
                     "VRPhoneScreenOverlay.Contracts",
                     "VRPhoneScreenOverlay.Core",
                     "VRPhoneScreenOverlay.Protocols",
                     "VRPhoneScreenOverlay.Presentation",
                 })
        {
            string projectDirectory = Path.Combine(root, "src", projectName);
            string source = string.Join(
                '\n',
                Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                    .Select(File.ReadAllText));

            foreach (string forbidden in forbiddenTerms)
            {
                Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void UserInterfaceDoesNotCallPlatformImplementationsDirectly()
    {
        string root = FindRepositoryRoot();
        string appDirectory = Path.Combine(root, "src", "VRPhoneScreenOverlay.App");
        string[] sourceFiles = Directory
            .EnumerateFiles(appDirectory, "*.cs", SearchOption.AllDirectories)
            .ToArray();
        string[] forbiddenTerms =
        [
            "System.Diagnostics.Process",
            "System.Net.Http.HttpClient",
            "DllImport",
            "Valve.VR",
            "adb.exe",
            "scrcpy",
            "FileSystemWatcher",
            "PrintWindow",
        ];

        foreach (string forbidden in forbiddenTerms)
        {
            string source = string.Join(
                '\n',
                sourceFiles
                    .Where(file =>
                        !string.Equals(forbidden, "DllImport", StringComparison.Ordinal) ||
                        !string.Equals(
                            Path.GetFileName(file),
                            "WindowsWindowChrome.cs",
                            StringComparison.OrdinalIgnoreCase))
                    .Select(File.ReadAllText));
            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
        }

        string windowChromeSource = File.ReadAllText(
            Path.Combine(appDirectory, "WindowsWindowChrome.cs"));
        Assert.Contains("DwmSetWindowAttribute", windowChromeSource, StringComparison.Ordinal);
        Assert.Contains("dwmapi.dll", windowChromeSource, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, windowChromeSource.Split("DllImport").Length - 1);
        Assert.DoesNotContain("user32.dll", windowChromeSource, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NuGetVersionsAreManagedCentrally()
    {
        string root = FindRepositoryRoot();
        foreach (string projectFile in Directory.EnumerateFiles(
                     root,
                     "*.csproj",
                     SearchOption.AllDirectories))
        {
            if (projectFile.Contains($"{Path.DirectorySeparatorChar}work{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            XDocument project = XDocument.Load(projectFile);
            string[] versionedReferences = project
                .Descendants("PackageReference")
                .Where(reference => reference.Attribute("Version") is not null)
                .Select(reference => reference.Attribute("Include")?.Value ?? "unknown")
                .ToArray();
            Assert.True(
                versionedReferences.Length == 0,
                $"{projectFile} contains non-central package versions: " +
                string.Join(", ", versionedReferences));
        }
    }

    [Fact]
    public void UnsafeCodeIsLimitedToApprovedInteropProjects()
    {
        string root = FindRepositoryRoot();
        string[] approved =
        [
            "VRPhoneScreenOverlay.Media",
            "VRPhoneScreenOverlay.SteamVR",
        ];

        foreach (string projectFile in Directory.EnumerateFiles(
                     Path.Combine(root, "src"),
                     "*.csproj",
                     SearchOption.AllDirectories))
        {
            XDocument project = XDocument.Load(projectFile);
            bool allowsUnsafe = project.Descendants("AllowUnsafeBlocks")
                .Any(value => string.Equals(value.Value, "true", StringComparison.OrdinalIgnoreCase));
            string projectName = Path.GetFileNameWithoutExtension(projectFile);
            Assert.True(!allowsUnsafe || approved.Contains(projectName, StringComparer.Ordinal));
        }
    }

    [Fact]
    public void WinFormsViewsUseDesignerSerializableLayout()
    {
        string root = FindRepositoryRoot();
        string appProjectPath = Path.Combine(
            root,
            "src",
            "VRPhoneScreenOverlay.App",
            "VRPhoneScreenOverlay.App.csproj");
        XDocument appProject = XDocument.Load(appProjectPath);
        string[] viewNames =
        [
            "MainShellView",
            "HomePageView",
            "WirelessConnectionView",
            "SettingsPageView",
            "OnlinePageView",
            "AboutPageView",
        ];

        foreach (string viewName in viewNames)
        {
            string codePath = $"Views\\{viewName}.cs";
            XElement? compile = appProject
                .Descendants("Compile")
                .FirstOrDefault(item =>
                    string.Equals(
                        item.Attribute("Update")?.Value,
                        codePath,
                        StringComparison.Ordinal));
            Assert.NotNull(compile);
            Assert.Equal("UserControl", compile.Element("SubType")?.Value);

            string resourcePath = $"Views\\{viewName}.resx";
            string resourceFilePath = Path.Combine(
                root,
                "src",
                "VRPhoneScreenOverlay.App",
                "Views",
                $"{viewName}.resx");
            Assert.True(File.Exists(resourceFilePath), $"Missing designer resource: {resourcePath}");
            XElement? resource = appProject
                .Descendants("EmbeddedResource")
                .FirstOrDefault(item =>
                    string.Equals(
                        item.Attribute("Update")?.Value,
                        resourcePath,
                        StringComparison.Ordinal));
            Assert.NotNull(resource);
            Assert.Equal($"{viewName}.cs", resource.Element("DependentUpon")?.Value);

            string designerPath = Path.Combine(
                root,
                "src",
                "VRPhoneScreenOverlay.App",
                "Views",
                $"{viewName}.Designer.cs");
            string designerSource = File.ReadAllText(designerPath);
            MatchCollection methods = Regex.Matches(
                designerSource,
                @"^\s*(?:private|protected)\s+(?:(?:static|override)\s+)*[A-Za-z0-9_.<>?]+\s+(?<name>[A-Za-z0-9_]+)\s*\(",
                RegexOptions.Multiline | RegexOptions.CultureInvariant);
            string[] unsupportedMethods = methods
                .Select(match => match.Groups["name"].Value)
                .Where(name => name is not "Dispose" and not "InitializeComponent")
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            Assert.True(
                unsupportedMethods.Length == 0,
                $"{viewName}.Designer.cs contains non-serializable helper methods: " +
                string.Join(", ", unsupportedMethods));
        }
    }

    private static HashSet<string> Set(params string[] values) =>
        new(values, StringComparer.Ordinal);

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root containing global.json was not found.");
    }
}
