using VRPhoneScreenOverlay.Protocols;

namespace VRPhoneScreenOverlay.Diagnostics;

public interface ILocalTroubleshootingInspector
{
    public Task<TroubleshootingCheckResult> InspectAsync(string check, IProgress<string>? progress,
        Func<CancellationToken, Task<bool>>? prepareReproduction, CancellationToken token);
}
