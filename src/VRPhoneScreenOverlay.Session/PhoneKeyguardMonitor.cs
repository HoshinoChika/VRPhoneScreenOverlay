using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.Session;

// One sequential monitor per video session. No queue: each bounded Android
// probe replaces the previous state; it never runs on video or VR input threads.
internal sealed class PhoneKeyguardMonitor(IAndroidKeyguardStateService keyguard,
    IAndroidConnectionLogSink log, TimeSpan? interval = null)
{
    public async Task RunAsync(string? deviceKey, Func<bool> enabled, Action<bool?> publish,
        CancellationToken cancellationToken)
    {
        string? previousReason = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!enabled())
            {
                publish(null);
                previousReason = null;
            }
            else
            {
                AndroidKeyguardSnapshot state;
                try { state = await keyguard.ProbeAsync(deviceKey, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                // Optional lock detection must not fault the independent video
                // session; unexpected provider failures use the same bounded retry.
                catch (Exception)
                {
                    state = new(AndroidKeyguardState.Unknown, AndroidReasonCodes.KeyguardProbeFailed, "暂时无法读取手机锁屏状态");
                }
                // A settings change during the probe must not re-enable a keypad.
                publish(enabled() ? state.State switch
                { AndroidKeyguardState.Locked => true, AndroidKeyguardState.Unlocked => false, _ => null } : null);
                if (state.ReasonCode != previousReason)
                {
                    previousReason = state.ReasonCode;
                    log.TryWrite(new AndroidConnectionLogEntry(DateTimeOffset.UtcNow, "phone_keyguard", state.ReasonCode, state.Message));
                }
            }
            await Task.Delay(interval ?? TimeSpan.FromMilliseconds(750), cancellationToken).ConfigureAwait(false);
        }
    }
}
