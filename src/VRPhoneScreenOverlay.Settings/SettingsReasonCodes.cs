namespace VRPhoneScreenOverlay.Settings;

/// <summary>
/// Stable reason codes raised by this module. Values are protocol: they are only ever
/// added, never re-spelled, and never localised.
/// </summary>
public static class SettingsReasonCodes
{
    public const string BatchApplyBusy = "SETTINGS_BATCH_APPLY_BUSY";
    /// <summary><c>SETTINGS_BATCH_APPLY_ROLLED_BACK</c></summary>
    public const string BatchApplyRolledBack = "SETTINGS_BATCH_APPLY_ROLLED_BACK";

    /// <summary><c>SETTINGS_BATCH_ROLLBACK_FAILED</c></summary>
    public const string BatchRollbackFailed = "SETTINGS_BATCH_ROLLBACK_FAILED";

    /// <summary><c>SETTINGS_CORRUPT_DEFAULTED</c></summary>
    public const string CorruptDefaulted = "SETTINGS_CORRUPT_DEFAULTED";

    /// <summary><c>SETTINGS_DEFAULT_CREATED</c></summary>
    public const string DefaultCreated = "SETTINGS_DEFAULT_CREATED";

    /// <summary><c>SETTINGS_INPUT_MIGRATED</c></summary>
    public const string InputMigrated = "SETTINGS_INPUT_MIGRATED";

    /// <summary><c>SETTINGS_LOADED</c></summary>
    public const string Loaded = "SETTINGS_LOADED";

    /// <summary><c>SETTINGS_NOT_INITIALIZED</c></summary>
    public const string NotInitialized = "SETTINGS_NOT_INITIALIZED";

    /// <summary><c>SETTINGS_SAVE_UNAVAILABLE</c></summary>
    public const string SaveUnavailable = "SETTINGS_SAVE_UNAVAILABLE";

    /// <summary><c>SETTINGS_SAVED</c></summary>
    public const string Saved = "SETTINGS_SAVED";

    /// <summary><c>SETTINGS_VALUES_REPAIRED</c></summary>
    public const string ValuesRepaired = "SETTINGS_VALUES_REPAIRED";
}
