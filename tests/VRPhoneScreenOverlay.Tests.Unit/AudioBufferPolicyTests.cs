using VRPhoneScreenOverlay.Media;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class AudioBufferPolicyTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(80, false)]
    [InlineData(160, false)]
    [InlineData(161, true)]
    [InlineData(500, true)]
    public void ResetsOnlyWhenLatencyBudgetIsExceeded(int milliseconds, bool expected)
    {
        Assert.Equal(expected, AudioBufferPolicy.ShouldReset(TimeSpan.FromMilliseconds(milliseconds)));
    }
}
