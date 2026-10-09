using System.Diagnostics;

namespace VRPhoneScreenOverlay.SteamVR;

internal static class OpenVrBindingStartup
{
    private const int _bindingToolTimeoutMilliseconds = 3_000;
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
                    "已准备并强制激活程序本地绑定");
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
                    OpenVrReasonCodes.LocalBindingPrepareFailed,
                    "无法准备本地手柄绑定，请确认 SteamVR 正在运行后重试");
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

        RunBindingTool();
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
        string executable = Path.Combine(AppContext.BaseDirectory, "VRPhoneScreenOverlay.SteamVR.BindingTool.exe");
        using Process process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = "enrich-local-binding " + (hand == OpenVrControllerHand.Left ? "left" : "right"),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        }) ?? throw new InvalidOperationException("无法启动绑定驱动适配工具");
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try { await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false); }
        catch
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); }
            using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(2));
            await process.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
            throw;
        }
        if (process.ExitCode != 0) { throw new InvalidOperationException("绑定驱动适配未完成"); }
    }

    private static void RunBindingTool()
    {
        string executable = Path.Combine(
            AppContext.BaseDirectory,
            "VRPhoneScreenOverlay.SteamVR.BindingTool.exe");
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("SteamVR 本地绑定准备工具未随程序发布", executable);
        }

        using Process process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = "activate-local-binding",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        }) ?? throw new InvalidOperationException("无法启动 SteamVR 本地绑定准备工具");
        if (!process.WaitForExit(_bindingToolTimeoutMilliseconds))
        {
            process.Kill(true);
            if (!process.WaitForExit(1000)) { throw new InvalidOperationException("SteamVR 本地绑定准备工具清理未完成"); }
            throw new InvalidOperationException("SteamVR 本地绑定准备工具运行超时");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("SteamVR 本地绑定准备工具执行失败");
        }
    }
}
