using VRPhoneScreenOverlay.App;
using VRPhoneScreenOverlay.Network;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class AboutOperationRunnerTests
{
    [Theory]
    [InlineData(NetworkReasonCodes.Unavailable)]
    [InlineData(NetworkReasonCodes.Timeout)]
    [InlineData(NetworkReasonCodes.HttpStatus)]
    public async Task NetworkFailureIsReturnedInlineAndNextOperationStillRuns(string reason)
    {
        string? error = await AboutOperationRunner.RunAsync(
            _ => Task.FromException(new NetworkException(reason, "网络请求失败")), CancellationToken.None);
        bool nextRan = false;

        string? nextError = await AboutOperationRunner.RunAsync(_ =>
        {
            nextRan = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.Contains(reason, error, StringComparison.Ordinal);
        Assert.True(nextRan);
        Assert.Null(nextError);
    }

    [Fact]
    public async Task UnexpectedFailureDoesNotExposeExceptionDetailsOrEscapeEventHandler()
    {
        string? error = await AboutOperationRunner.RunAsync(
            _ => Task.FromException(new InvalidOperationException("private exception detail")),
            CancellationToken.None);

        Assert.Contains("UI_ABOUT_OPERATION_FAILED", error, StringComparison.Ordinal);
        Assert.DoesNotContain("private exception detail", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClosingWindowCancelsSilently()
    {
        using CancellationTokenSource closing = new();
        await closing.CancelAsync();

        string? error = await AboutOperationRunner.RunAsync(
            token => Task.FromCanceled(token), closing.Token);

        Assert.Null(error);
    }
}
