using System.Text.Json;
using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.Diagnostics;
using VRPhoneScreenOverlay.Update;

namespace VRPhoneScreenOverlay.App;

// The asynchronous button boundary returns safe inline text, including errors
// from the shared network layer. Unexpected exceptions never escape async void.
internal static class AboutOperationRunner
{
    public static async Task<string?> RunAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            await operation(cancellationToken).ConfigureAwait(true);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            return exception switch
            {
                UpdateException update => $"{update.Message} [{update.ReasonCode}]",
                DiagnosticsException diagnostics => $"{diagnostics.Message} [{diagnostics.ReasonCode}]",
                IReasonCoded reason => $"{exception.Message} [{reason.ReasonCode}]",
                JsonException => "服务返回的内容无效，请稍后重试 [NETWORK_RESPONSE_INVALID]",
                OperationCanceledException => "网络操作已中止，请稍后重试 [NETWORK_TIMEOUT]",
                HttpRequestException or IOException or UnauthorizedAccessException =>
                    "测试服务连接或文件操作失败，请稍后重试 [UI_ABOUT_OPERATION_FAILED]",
                _ => "操作遇到未预期错误，请稍后重试 [UI_ABOUT_OPERATION_FAILED]",
            };
        }
    }
}
