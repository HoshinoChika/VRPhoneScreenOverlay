using VRPhoneScreenOverlay.Core;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class ShutdownSequenceTests
{
    [Fact]
    public async Task OneFailedOwnerCannotSkipRemainingOwners()
    {
        List<string> stopped = [];
        IReadOnlyList<string> failures = await ShutdownSequence.RunAsync(
        [
            new("media", () => { stopped.Add("media"); throw new InvalidOperationException(); }),
            new("playspace", async () => { await Task.Yield(); stopped.Add("playspace"); }),
            new("input", () => { stopped.Add("input"); return ValueTask.CompletedTask; }),
        ]);
        Assert.Equal("media", Assert.Single(failures));
        Assert.Equal("media,playspace,input", string.Join(',', stopped));
    }
}
