using System.Diagnostics;

namespace VRPhoneScreenOverlay.SteamVR;

internal static class OpenVrBindingStartup
{
    private const int _bindingToolTimeoutMilliseconds = 10_000;
    private static readonly object _gate = new();
    private static bool _prepared;
    private static string? _actionManifestPath;
    private static string? _savedBindingRevision;
    private static OpenVrControllerHand? _preparedHand;
    private static OpenVrBindingPreparationSnapshot _diagnosticSnapshot =
        OpenVrBindingPreparationSnapshot.NotPrepared;

    public static OpenVrBindingPreparationSnapshot GetDiagnosticSnapshot()
    {
        lock (_gate)
        {
            return _diagnosticSnapshot;
        }
    }

    public static string EnsurePrepared()
    {
        lock (_gate)
        {
            OpenVrControllerHand currentHand = OpenVrControllerPreferences.Load();
            if (_prepared && _actionManifestPath is not null && File.Exists(_actionManifestPath) && _preparedHand == currentHand)
            {
                return _actionManifestPath;
            }

            return PrepareCore();
        }
    }

    public static OpenVrBindingResult Refresh()
    {
        lock (_gate)
        {
            try
            {
                _ = PrepareCore();
                return new OpenVrBindingResult(
                    true,
                    OpenVrReasonCodes.LocalBindingPrepared,
                    "已准备程序本地绑定文件，尚未激活");
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException or
                    InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                _prepared = false;
                _actionManifestPath = null;
                _savedBindingRevision = null;
                _preparedHand = null;
                _diagnosticSnapshot = OpenVrBindingPreparationSnapshot.NotPrepared;
                return new OpenVrBindingResult(
                    false,
                    exception is BindingToolComponentException component ? component.ReasonCode : OpenVrReasonCodes.LocalBindingPrepareFailed,
                    exception is BindingToolComponentException ? exception.Message : "无法准备本地手柄绑定，请确认 SteamVR 正在运行后重试");
            }
        }
    }

    public static bool HasSavedBindingChanged()
    {
        lock (_gate)
        {
            if (!_prepared || _savedBindingRevision is null)
            {
                return false;
            }

            try
            {
                string currentRevision = OpenVrBindingProfileStore
                    .CreateDefault()
                    .GetSavedBindingRevision();
                return !string.Equals(
                    currentRevision,
                    _savedBindingRevision,
                    StringComparison.Ordinal);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException or
                    InvalidOperationException)
            {
                return false;
            }
        }
    }

    internal static void AcceptLivePrepared(string? revision = null)
    {
        lock (_gate)
        {
            OpenVrBindingProfileStore store = OpenVrBindingProfileStore.CreateDefault();
            _savedBindingRevision = revision ?? store.GetSavedBindingRevision();
            _preparedHand ??= OpenVrControllerPreferences.Load();
            _diagnosticSnapshot = new(true, _savedBindingRevision, _preparedHand.Value,
                store.ResolveProfiles().Select(profile => new OpenVrBindingProfileDiagnostic(profile.ControllerType, profile.Source.ToString())).ToArray());
        }
    }

    private static string PrepareCore()
    {
        OpenVrBindingProfileStore store = OpenVrBindingProfileStore.CreateDefault();
        string revisionBeforePrepare = store.GetSavedBindingRevision();
        OpenVrPreparedInputManifest prepared = store.Prepare();
        string revisionAfterPrepare = store.GetSavedBindingRevision();
        if (!string.Equals(
            revisionBeforePrepare,
            revisionAfterPrepare,
            StringComparison.Ordinal))
        {
            prepared = store.Prepare();
            revisionAfterPrepare = store.GetSavedBindingRevision();
        }

        _actionManifestPath = prepared.ActionManifestPath;
        _savedBindingRevision = revisionAfterPrepare;
        _preparedHand = OpenVrControllerPreferences.Load();
        _diagnosticSnapshot = new OpenVrBindingPreparationSnapshot(
            true,
            revisionAfterPrepare,
            _preparedHand.Value,
            prepared.Bindings
                .Select(profile => new OpenVrBindingProfileDiagnostic(
                    profile.ControllerType,
                    profile.Source.ToString()))
                .ToArray());
        _prepared = true;
        return prepared.ActionManifestPath;
    }

    internal static async Task EnrichLivePreparedAsync(OpenVrControllerHand hand, CancellationToken token)
    {
        await BindingToolComponents.Current().EnsureAsync(token).ConfigureAwait(false);
        OpenVrBindingResult result = await RunCheckedUtilityAsync("enrich-local-binding " +
            (hand == OpenVrControllerHand.Left ? "left" : "right"), TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
        CheckUtilityResult(result);
    }

    internal static async ValueTask<OpenVrBindingResult> ActivateAsync(CancellationToken token, Action? beforeStart = null)
    {
        try { await BindingToolComponents.Current().EnsureAsync(token).ConfigureAwait(false); }
        catch (BindingToolComponentException error) { return new(false, error.ReasonCode, error.Message); }
        return await RunCheckedUtilityAsync("activate-local-binding", TimeSpan.FromMilliseconds(_bindingToolTimeoutMilliseconds), token, beforeStart).ConfigureAwait(false);
    }

    internal static Task<OpenVrBindingResult> RunCheckedUtilityAsync(string arguments, TimeSpan timeout, CancellationToken token, Action? beforeStart = null)
        => OpenVrStartupRegistration.RunCoordinatedUtilityAsync(new ProcessStartInfo
        {
            FileName = Path.Combine(AppContext.BaseDirectory, "VRPhoneScreenOverlay.SteamVR.BindingTool.exe"),
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
        }, timeout, token, beforeStart).AsTask();

    private static void CheckUtilityResult(OpenVrBindingResult result)
    {
        if (result.Succeeded) { return; }
        if (result.ReasonCode.StartsWith("BINDING_COMPONENT_", StringComparison.Ordinal))
        { throw new BindingToolComponentException(result.ReasonCode); }
        throw new InvalidOperationException(result.Message);
    }
}
