using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Session;

public static class UpdateBindingReset
{
    // The existing installer (including 0.2.7) supplies both health arguments
    // only when starting the newly installed candidate. Normal launches never
    // reset later user customizations. No version migration ledger is needed.
    public static ValueTask<IBindingDefaultsTransaction?> BeginAsync(string? healthFile, string? healthToken,
        CancellationToken token) => BeginAsync(healthFile, healthToken, OpenVrBindingDefaults.CreateForUpdate(), token);

    internal static async ValueTask<IBindingDefaultsTransaction?> BeginAsync(string? healthFile, string? healthToken,
        IBindingDefaultsRestorer defaults, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(healthFile) || string.IsNullOrWhiteSpace(healthToken)) { return null; }
        // Keep only the existing bounded in-memory transaction until startup
        // health is acknowledged; never persist a backup of old binding files.
        return await defaults.BeginResetAsync(token).ConfigureAwait(false);
    }
}
