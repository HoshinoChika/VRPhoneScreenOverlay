namespace VRPhoneScreenOverlay.Android;

internal interface IAdbServerShutdown
{
    public ValueTask<AdbServerShutdownResult> StopAsync(CancellationToken cancellationToken);
}

internal sealed record AdbServerShutdownResult(
    bool Succeeded,
    string ReasonCode,
    string Message);

internal sealed class PreserveSharedAdbServer : IAdbServerShutdown
{
    public ValueTask<AdbServerShutdownResult> StopAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // A server started from our binary can still serve unrelated clients.
        // Session/forward owners release their own resources before this step.
        return ValueTask.FromResult(new AdbServerShutdownResult(true,
            "ANDROID_SHARED_SERVER_PRESERVED", "本应用连接已释放，保留共享 ADB 服务"));
    }
}
