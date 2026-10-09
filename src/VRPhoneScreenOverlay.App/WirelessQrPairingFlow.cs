using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.App;

// One serial pairing transaction, zero queued refreshes. Only an expired QR
// renews; cancellation, navigation, success and actual failures end the flow.
internal static class WirelessQrPairingFlow
{
    public static Task<AndroidOperationResult> RunAsync(IAndroidWirelessConnectionService service,
        Action<WirelessPairingQr, bool> showQr, Func<bool> isActive, CancellationToken cancellationToken) =>
        RunOwnedAsync(service, showQr, isActive, false, null, cancellationToken);

    public static Task<AndroidOperationResult> RunContinuousAsync(IAndroidWirelessConnectionService service,
        Action<WirelessPairingQr, bool> showQr, Func<bool> isActive, Action<AndroidOperationResult> reportResult,
        CancellationToken cancellationToken) => RunOwnedAsync(service, showQr, isActive, true, reportResult, cancellationToken);

    private static async Task<AndroidOperationResult> RunOwnedAsync(IAndroidWirelessConnectionService service,
        Action<WirelessPairingQr, bool> showQr, Func<bool> isActive, bool continuous,
        Action<AndroidOperationResult>? reportResult, CancellationToken cancellationToken)
    {
        bool refreshed = false;
        while (isActive())
        {
            cancellationToken.ThrowIfCancellationRequested();
            WirelessPairingQr qr = new();
            showQr(qr, refreshed);
            AndroidOperationResult result = await service.ExecuteWirelessAsync(
                WirelessAdbOperation.PairQr, qr.ServiceName, qr.Password, cancellationToken).ConfigureAwait(true);
            if (result.ReasonCode != "WIRELESS_QR_EXPIRED")
            {
                reportResult?.Invoke(result);
                if (!continuous || !result.Succeeded) { return result; }
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(true);
            }
            // Also yield if a faulty service reports expiry immediately.
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(true);
            refreshed = true;
        }
        return new(false, "WIRELESS_CANCELLED", "无线连接操作已取消");
    }
}
