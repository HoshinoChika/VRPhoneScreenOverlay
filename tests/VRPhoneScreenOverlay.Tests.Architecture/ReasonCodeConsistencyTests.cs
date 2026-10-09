using System.Text.RegularExpressions;

namespace VRPhoneScreenOverlay.Tests.Architecture;

/// <summary>
/// Guards the reason code contract: a code is what diagnostics, logs and the UI branch on, so one
/// code must mean exactly one thing.
/// </summary>
public sealed partial class ReasonCodeConsistencyTests
{
    /// <summary>
    /// Codes that legitimately carry more than one message because the difference is interpolated
    /// detail about the same event, not a different event. Keep this list short and justified.
    /// </summary>
    private static readonly HashSet<string> _allowedMultiMessageCodes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            // Same transport timeout; the UI names the operation, the body reader names the stage.
            // Display text is not the machine-readable identity of the failure.
            "NetworkReasonCodes.Timeout",

            // Each failure names the specific OpenVR error it hit.
            "OpenVrReasonCodes.BindingUiFailed",
            "OpenVrReasonCodes.InitializationFailed",
            "OpenVrReasonCodes.LocalBindingActivationFailed",

            // Reports either the plain timeout or the timeout plus its consequence.
            "AndroidReasonCodes.ControlDisplaySizeTimeout",

            // Reports either the exhaustion or the exhaustion plus what the user should do.
            "MediaReasonCodes.AudioRecoveryExhausted",

            // Reports either the send failure or the failure plus the recovery behaviour.
            "PhoneReasonCodes.ControlSendFailed",
        };

    [Fact]
    public void OneReasonCodeCarriesOneMeaning()
    {
        string root = FindRepositoryRoot();
        Dictionary<string, SortedSet<string>> messagesByCode = new(StringComparer.Ordinal);

        foreach (string file in Directory.EnumerateFiles(
                     Path.Combine(root, "src"),
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal) ||
                file.EndsWith("openvr_api.cs", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in ReasonCodeWithMessage().Matches(File.ReadAllText(file)))
            {
                string code = match.Groups["code"].Value;
                string message = match.Groups["message"].Value;

                // Only plain literals are compared. An interpolated message differs by design.
                if (message.StartsWith('$') || !ContainsHan().IsMatch(message))
                {
                    continue;
                }

                if (!messagesByCode.TryGetValue(code, out SortedSet<string>? messages))
                {
                    messages = new SortedSet<string>(StringComparer.Ordinal);
                    messagesByCode[code] = messages;
                }

                messages.Add(message);
            }
        }

        string[] conflicts = messagesByCode
            .Where(entry => entry.Value.Count > 1)
            .Where(entry => !_allowedMultiMessageCodes.Contains(entry.Key))
            .Select(entry => $"{entry.Key}: {string.Join(" | ", entry.Value)}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            conflicts.Length == 0,
            "These reason codes carry more than one meaning. A code is a machine readable " +
            "contract, so give the distinct state its own code instead of reusing one:\n" +
            string.Join("\n", conflicts));
    }

    [GeneratedRegex(
        @"(?<code>[A-Za-z]*ReasonCodes\.[A-Za-z]+)\s*,\s*\r?\n?\s*(?<message>\$?""[^""]*"")",
        RegexOptions.Singleline)]
    private static partial Regex ReasonCodeWithMessage();

    [GeneratedRegex(@"[一-龥]")]
    private static partial Regex ContainsHan();

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
