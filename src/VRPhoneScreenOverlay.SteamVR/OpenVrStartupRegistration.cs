using System.Diagnostics;
using System.Text.Json;
using Valve.VR;

namespace VRPhoneScreenOverlay.SteamVR;

public static class OpenVrStartupRegistration
{
    public static OpenVrBindingResult Configure(bool enabled)
        => ConfigureAsync(enabled, CancellationToken.None).AsTask().GetAwaiter().GetResult();

    public static ValueTask<OpenVrBindingResult> ConfigureAsync(bool enabled, CancellationToken token)
    {
        ProcessStartInfo start = new()
        {
            FileName = Path.Combine(AppContext.BaseDirectory, "VRPhoneScreenOverlay.SteamVR.BindingTool.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
        };
        start.ArgumentList.Add("configure-startup");
        start.ArgumentList.Add(enabled ? "enabled" : "disabled");
        return RunUtilityAsync(start, TimeSpan.FromSeconds(3), token);
    }

    internal static async ValueTask<OpenVrBindingResult> RunUtilityAsync(ProcessStartInfo start, TimeSpan timeout, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using Process process = new() { StartInfo = start };
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        Task<string>? output = null;
        try
        {
            if (!process.Start()) { throw new InvalidOperationException("Registration utility did not start."); }
            output = ReadBoundedAsync(process.StandardOutput, deadline.Token);
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            string json = await output.ConfigureAwait(false);
            OpenVrBindingResult? result = JsonSerializer.Deserialize<OpenVrBindingResult>(json);
            if (result is null || string.IsNullOrEmpty(result.ReasonCode) || (process.ExitCode == 0) != result.Succeeded)
            { throw new InvalidDataException("Invalid registration response."); }
            return result;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return new(false, OpenVrReasonCodes.StartupRegistrationTimeout, "SteamVR连接超时，请检查SteamVR状态和运行权限。"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or
            System.ComponentModel.Win32Exception or JsonException)
        { return new(false, OpenVrReasonCodes.StartupRegistrationFailed, "SteamVR 自动启动设置未应用，请检查 SteamVR 状态后重试"); }
        finally
        {
            try
            {
                if (process.Id != 0 && !process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
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

    internal static OpenVrBindingResult ConfigureInUtility(bool enabled)
    {
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
                            "SteamVR 自动启动设置未应用，请确认已安装 SteamVR 后重试");
                    }
                }
                string applicationManifest = OpenVrInputManifest.ResolveApplicationManifest();
                OpenVrInputManifest.RegisterApplicationManifest(applicationManifest, enabled);
                return new(true, OpenVrReasonCodes.StartupConfigured,
                    enabled ? "已启用随 SteamVR 启动" : "已关闭随 SteamVR 启动");
            }
            catch (OpenVrApplicationRegistrationException exception)
            {
                return new(false, OpenVrReasonCodes.StartupRegistrationFailed, exception.UserMessage);
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
