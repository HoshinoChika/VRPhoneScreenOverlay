using System.Diagnostics;
using System.Text.Json;
using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

public static class OpenVrStartupRegistration
{
    public static OpenVrBindingResult Configure(bool enabled)
        => ConfigureAsync(enabled, CancellationToken.None).AsTask().GetAwaiter().GetResult();

    public static async ValueTask<OpenVrBindingResult> ConfigureAsync(bool? enabled, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try { await BindingToolComponents.Current().EnsureAsync(token).ConfigureAwait(false); }
        catch (BindingToolComponentException error) { return new(false, error.ReasonCode, error.Message); }
        ProcessStartInfo start = new()
        {
            FileName = Path.Combine(AppContext.BaseDirectory, "VRPhoneScreenOverlay.SteamVR.BindingTool.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
        };
        start.ArgumentList.Add("configure-startup");
        start.ArgumentList.Add(enabled is null ? "preserve" : enabled.Value ? "enabled" : "disabled");
        OpenVrBindingResult result = await RunCoordinatedUtilityAsync(start, TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
        return result.StartupDiagnostic is not null ? result : result with
        {
            StartupDiagnostic = new(result.ReasonCode == OpenVrReasonCodes.StartupRegistrationTimeout ? "utility-timeout" : "utility",
                RequestedAutoLaunch: enabled)
        };
    }

    internal static async ValueTask<OpenVrBindingResult> RunCoordinatedUtilityAsync(ProcessStartInfo start, TimeSpan timeout, CancellationToken token, Action? beforeStart = null)
    {
        token.ThrowIfCancellationRequested();
        using IDisposable? reservation = OpenVrRuntimeHost.RegistrationGate.TryReserve();
        if (reservation is null)
        {
            return new(false, "OPENVR_STARTUP_BUSY", "SteamVR 连接初始化进行中，登记将稍后重试")
            { StartupDiagnostic = new("runtime-initialization-busy") };
        }
        if (!OpenVrRuntimeAvailability.CanInitializeUtility()) { return OpenVrRuntimeAvailability.NamespaceUnavailable(); }
        beforeStart?.Invoke();
        return await RunUtilityAsync(start, timeout, token).ConfigureAwait(false);
    }

    internal static async ValueTask<OpenVrBindingResult> RunUtilityAsync(ProcessStartInfo start, TimeSpan timeout, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using Process process = new() { StartInfo = start };
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        Task<string>? output = null;
        Task<string>? errors = null;
        bool started = false;
        long began = Stopwatch.GetTimestamp();
        OpenVrBindingResult Finish(OpenVrBindingResult value) => value with
        {
            StartupDiagnostic = (value.StartupDiagnostic ?? new(value.ReasonCode == OpenVrReasonCodes.StartupRegistrationTimeout ? "utility-timeout" : "utility-result")) with
            {
                HelperProcessId = started ? process.Id : null,
                ElapsedMilliseconds = Stopwatch.GetElapsedTime(began).TotalMilliseconds,
                ExitCode = started && process.HasExited ? process.ExitCode : null
            }
        };
        try
        {
            start.RedirectStandardError = true;
            if (!process.Start()) { throw new InvalidOperationException("Registration utility did not start."); }
            started = true;
            output = ReadBoundedAsync(process.StandardOutput, deadline.Token);
            errors = ReadBoundedAsync(process.StandardError, deadline.Token);
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            string errorText = await errors.ConfigureAwait(false);
            if (process.ExitCode != 0 && (errorText.Contains("dependency resolver", StringComparison.OrdinalIgnoreCase) ||
                errorText.Contains("Failed to parse", StringComparison.OrdinalIgnoreCase) || errorText.Contains("hostpolicy", StringComparison.OrdinalIgnoreCase)))
            {
                return Finish(new(false, "BINDING_COMPONENT_HOST_FAILED", "软件绑定辅助组件无法加载，请从官网下载完整安装包修复。")
                { StartupDiagnostic = new("utility-host-load", NativeError: $"exit=0x{process.ExitCode:X8};category=dependency-loader") });
            }
            string json = await output.ConfigureAwait(false);
            OpenVrBindingResult? result = JsonSerializer.Deserialize<OpenVrBindingResult>(json);
            if (result is null || string.IsNullOrEmpty(result.ReasonCode) || (process.ExitCode == 0) != result.Succeeded)
            { throw new InvalidDataException("Invalid registration response."); }
            return Finish(result);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return Finish(new(false, OpenVrReasonCodes.StartupRegistrationTimeout, "SteamVR 连接超时，本次尝试已结束；请检查 SteamVR 状态。")); }
        catch (Exception error) when (error is InvalidDataException or JsonException)
        { return Finish(new(false, "BINDING_COMPONENT_HOST_FAILED", "软件绑定辅助组件无法加载，请从官网下载完整安装包修复。")); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or
            System.ComponentModel.Win32Exception or JsonException)
        { return Finish(new(false, OpenVrReasonCodes.StartupRegistrationUnconfirmed, "SteamVR 自动启动设置结果未确认，请检查 SteamVR 状态后重试")); }
        finally
        {
            try
            {
                if (started && !process.HasExited)
                {
                    // OpenVR may have spawned a runtime; never terminate its descendants.
                    process.Kill(entireProcessTree: false);
                    using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(1));
                    await process.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
                }
            }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException) { }
            if (output is not null)
            {
                try { await output.ConfigureAwait(false); }
                catch (Exception error) when (error is OperationCanceledException or IOException or InvalidDataException) { }
            }
            if (errors is not null)
            {
                try { await errors.ConfigureAwait(false); }
                catch (Exception error) when (error is OperationCanceledException or IOException or InvalidDataException) { }
            }
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token)
    {
        char[] buffer = new char[512];
        System.Text.StringBuilder text = new();
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
        {
            if (text.Length + count > 4096) { throw new InvalidDataException("Registration response exceeds limit."); }
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }

    internal static OpenVrBindingResult ConfigureInUtility(bool? enabled)
    {
        if (!OpenVrRuntimeAvailability.CanInitializeUtility()) { return OpenVrRuntimeAvailability.NamespaceUnavailable(); }
        lock (OpenVrRuntimeHost.ApiGate)
        {
            bool utilityStarted = false;
            try
            {
                // Utility registration does not require a running compositor or HMD.
                if (OpenVrRuntimeHost.CurrentSystem is null)
                {
                    EVRInitError error = EVRInitError.None;
                    _ = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Utility);
                    utilityStarted = error == EVRInitError.None;
                    if (!utilityStarted)
                    {
                        return new(false, OpenVrReasonCodes.InitializationFailed,
                            "SteamVR 自动启动设置未应用，请确认已安装 SteamVR 后重试")
                        { StartupDiagnostic = new("initialize", RequestedAutoLaunch: enabled, NativeError: error.ToString()) };
                    }
                }
                string applicationManifest = OpenVrInputManifest.ResolveApplicationManifest();
                var diagnostic = OpenVrInputManifest.RegisterApplicationManifest(applicationManifest, enabled);
                return new(true, OpenVrReasonCodes.StartupConfigured,
                    enabled is null ? "已校准安装登记，保留 SteamVR 原有启动偏好" : enabled.Value ? "已启用随 SteamVR 启动" : "已关闭随 SteamVR 启动")
                { StartupDiagnostic = diagnostic };
            }
            catch (OpenVrApplicationRegistrationException exception)
            {
                return new(false, OpenVrReasonCodes.StartupRegistrationFailed, exception.UserMessage)
                { StartupDiagnostic = exception.Diagnostic };
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or
                UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                return new(false, OpenVrReasonCodes.StartupRegistrationFailed, "SteamVR 自动启动设置失败，请确认安装文件完整后重试");
            }
            finally
            {
                if (utilityStarted) { OpenVR.Shutdown(); }
            }
        }
    }
}
