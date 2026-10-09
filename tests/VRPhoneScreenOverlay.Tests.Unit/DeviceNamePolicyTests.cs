using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class DeviceNamePolicyTests
{
    [Theory]
    [InlineData("手机A0123", true)]
    [InlineData("ABCabc012345", true)]
    [InlineData("𠮷手机", true)]
    [InlineData("", false)]
    [InlineData("a b", false)]
    [InlineData("手机-1", false)]
    [InlineData("手机_1", false)]
    [InlineData("手机😀", false)]
    [InlineData("é", false)]
    [InlineData("あ", false)]
    [InlineData("한", false)]
    [InlineData("手机\n", false)]
    public void OnlyHanAsciiLettersAndDigitsAreAccepted(string name, bool expected) => Assert.Equal(expected, DeviceNamePolicy.IsValid(name));

    [Fact]
    public void LengthUsesCharactersRatherThanUtf16CodeUnits()
    {
        Assert.True(DeviceNamePolicy.IsValid(new string('中', 20)));
        Assert.False(DeviceNamePolicy.IsValid(new string('中', 21)));
        Assert.True(DeviceNamePolicy.IsValid(string.Concat(Enumerable.Repeat("𠮷", 20))));
        Assert.False(DeviceNamePolicy.IsValid(string.Concat(Enumerable.Repeat("𠮷", 21))));
        Assert.False(DeviceNamePolicy.IsValid("\ud800"));
    }
}
