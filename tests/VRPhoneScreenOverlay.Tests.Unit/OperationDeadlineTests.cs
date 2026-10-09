using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class OperationDeadlineTests
{
    [Fact]
    public void RemainingNeverExceedsOriginalBudget()
    {
        OperationDeadline deadline = OperationDeadline.Start(TimeSpan.FromSeconds(5));

        Assert.InRange(deadline.Remaining, TimeSpan.Zero, TimeSpan.FromSeconds(5));
        Assert.InRange(
            deadline.GetRemainingUpTo(TimeSpan.FromMilliseconds(250)),
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public async Task CancellationSourceExpiresWithDeadline()
    {
        OperationDeadline deadline = OperationDeadline.Start(TimeSpan.FromMilliseconds(80));
        using CancellationTokenSource source = deadline.CreateCancellationSource(CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Task.Delay(Timeout.InfiniteTimeSpan, source.Token));
        await Task.Delay(TimeSpan.FromMilliseconds(20));
        Assert.True(deadline.IsExpired);
    }
}
