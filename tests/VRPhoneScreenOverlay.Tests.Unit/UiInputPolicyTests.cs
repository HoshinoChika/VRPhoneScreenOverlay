using VRPhoneScreenOverlay.App;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class UiInputPolicyTests
{
    [Fact]
    public void DateTimeInputAcceptsOnlyExactInRangeValues()
    {
        DateTime minimum = new(2026, 8, 14, 12, 0, 0);
        DateTime maximum = new(2026, 8, 21, 12, 0, 0);

        Assert.True(UiInputPolicy.TryParseDateTime(
            "2026-08-20 18:35",
            minimum,
            maximum,
            out DateTime parsed));
        Assert.Equal(new DateTime(2026, 8, 20, 18, 35, 0), parsed);
        Assert.False(UiInputPolicy.TryParseDateTime(
            "2026/08/20 18:35",
            minimum,
            maximum,
            out _));
        Assert.False(UiInputPolicy.TryParseDateTime(
            "2026-08-22 18:35",
            minimum,
            maximum,
            out _));
    }

    [Fact]
    public void DiagnosticDescriptionRemovesUnsafeControlCharacters()
    {
        Assert.True(UiInputPolicy.TryNormalizeDescription(
            "  视频冻结\0\u0001\r\n重新打开后恢复  ",
            out string? normalized));

        Assert.Equal("视频冻结\r\n重新打开后恢复", normalized);
    }

    [Fact]
    public void DiagnosticDescriptionRejectsOversizedProgrammaticInput()
    {
        string oversized = new('测', UiInputPolicy.MaximumDescriptionLength + 1);

        Assert.False(UiInputPolicy.TryNormalizeDescription(oversized, out _));
    }
}
