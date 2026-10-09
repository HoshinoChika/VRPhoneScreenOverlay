using System.Text.Json.Nodes;
using VRPhoneScreenOverlay.Diagnostics;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class DiagnosticSanitizerAuditTests
{
    [Fact]
    public void FreeformReportsRemovePairingCodesOtherUserPathsAndPrivateKeyBlocks()
    {
        string text = "pairingCode=123456 C:\\Users\\ExamplePerson\\file.txt\n" +
            "-----BEGIN " + "PRIVATE KEY-----\nsynthetic-key-fixture\n-----END " + "PRIVATE KEY-----";
        string result = DiagnosticSanitizer.SanitizeText(text);
        Assert.DoesNotContain("123456", result);
        Assert.DoesNotContain("ExamplePerson", result);
        Assert.DoesNotContain("synthetic-key-fixture", result);
    }

    [Theory]
    [InlineData("token")]
    [InlineData("Password")]
    [InlineData("api_key")]
    [InlineData("deviceSerial")]
    [InlineData("pairingCode")]
    public void FullAuditStructuredSensitiveFieldsAreRedacted(string field)
    {
        JsonObject input = new() { [field] = "audit-example-value", ["frameCount"] = 45 };
        JsonNode output = JsonNode.Parse(DiagnosticSanitizer.SanitizeJsonLine(input.ToJsonString()))!;
        Assert.Equal("<redacted>", output[field]?.GetValue<string>());
        Assert.Equal(45, output["frameCount"]?.GetValue<int>());
    }

    [Fact]
    public void FullAuditArrayValuesAreSanitizedAtEveryDepth()
    {
        string path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        JsonObject input = new()
        {
            ["messages"] = new JsonArray("token=audit-example-value", new JsonArray(path + "\\private.txt")),
        };
        string output = DiagnosticSanitizer.SanitizeJsonLine(input.ToJsonString());
        Assert.DoesNotContain("audit-example-value", output, StringComparison.Ordinal);
        Assert.DoesNotContain(path.Replace("\\", "\\\\", StringComparison.Ordinal), output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<redacted>", output, StringComparison.Ordinal);
    }

    [Fact]
    public void FullAuditRootStringIsSanitized()
    {
        string input = System.Text.Json.JsonSerializer.Serialize("serial=EXAMPLE1234");
        string output = DiagnosticSanitizer.SanitizeJsonLine(input);
        Assert.DoesNotContain("EXAMPLE1234", output, StringComparison.Ordinal);
    }
}
