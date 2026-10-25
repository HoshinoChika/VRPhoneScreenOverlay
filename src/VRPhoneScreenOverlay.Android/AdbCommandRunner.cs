using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace VRPhoneScreenOverlay.Android;

internal sealed record AdbCommandResult(
    int ExitCode, string StandardOutput, string StandardError, TimeSpan Duration, bool TimedOut)
{
    public bool OutputTruncated { get; init; }
    public AdbCommandDiagnostic? Diagnostic { get; init; }
    public bool Succeeded => !TimedOut && !OutputTruncated && ExitCode == 0;
}

internal interface IAdbCommandRunner
{
    public ValueTask<AdbCommandResult> RunAsync(
        IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken,
        string? standardInput = null);
}

internal sealed class AdbCommandRunner(string executablePath, bool killProcessTree = false,
    Func<CancellationToken, ValueTask<string>>? resolveExecutable = null,
    IAndroidConnectionLogSink? log = null, bool verifySharedServer = false) : IAdbCommandRunner
{
    private readonly string _source = Path.GetFullPath(executablePath);
    private string? _resolved;
    private int _endpointLogged;

    public string ExecutablePath => Volatile.Read(ref _resolved) ?? _source;
    internal string SourceExecutablePath => _source;

    public async ValueTask<AdbCommandResult> RunAsync(
        IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken,
        string? standardInput = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        cancellationToken.ThrowIfCancellationRequested();
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        long started = Stopwatch.GetTimestamp();
        AdbEndpoint endpoint = AdbDiagnosticPolicy.Resolve(arguments,
            resolveExecutable is null ? "provided-tool" : "runtime-copy-pending");
        AdbCommandDiagnostic diagnostic = endpoint.Diagnostic;
        if (Interlocked.Exchange(ref _endpointLogged, 1) == 0)
        { WriteDiagnostic("ANDROID_TOOL_ENDPOINT", diagnostic, 0); }

        using Process process = new();
        BoundedOutput output = new(262144);
        BoundedOutput error = new(32768);
        Task? reads = null;
        bool launched = false;
        bool timedOut = false;
        string? timeoutPhase = null;
        string phase = "prepare";
        int? cleanupError = null;
        try
        {
            if (resolveExecutable is not null)
            {
                Volatile.Write(ref _resolved, await resolveExecutable(deadline.Token).ConfigureAwait(false));
                diagnostic = diagnostic with { ToolLocation = "verified-runtime-copy" };
            }
            if (verifySharedServer && diagnostic.Command != "version")
            {
                phase = "server-probe";
                if (diagnostic.EndpointKind == "invalid")
                { throw new AndroidConnectionException("ANDROID_SERVER_ENDPOINT_INVALID", "ADB 服务端点配置无效，请检查启动环境中的服务地址和端口"); }
                int? version = await AdbServerCompatibilityProbe.ReadVersionAsync(endpoint, deadline.Token).ConfigureAwait(false);
                diagnostic = diagnostic with { ServerProtocolVersion = version };
                if (version is int actual && actual != AdbServerCompatibilityProbe.ExpectedVersion)
                {
                    diagnostic = diagnostic with { ErrorCategory = "server-version-mismatch" };
                    throw new AndroidConnectionException("ANDROID_SERVER_VERSION_CONFLICT", "现有 ADB 服务版本不兼容，请先退出相关 ADB 软件后重试")
                    { CommandDiagnostic = diagnostic };
                }
            }
            ProcessStartInfo start = new()
            {
                FileName = ExecutablePath,
                WorkingDirectory = Path.GetDirectoryName(ExecutablePath),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = standardInput is not null,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (string argument in arguments) { start.ArgumentList.Add(argument); }
            process.StartInfo = start;
            phase = "start";
            if (!process.Start()) { throw new InvalidOperationException("ADB process did not start."); }
            launched = true;
            phase = "command";
            reads = Task.WhenAll(output.ReadAsync(process.StandardOutput, deadline.Token),
                error.ReadAsync(process.StandardError, deadline.Token));
            if (standardInput is not null)
            {
                await process.StandardInput.WriteLineAsync(standardInput.AsMemory(), deadline.Token).ConfigureAwait(false);
                process.StandardInput.Close();
            }
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            phase = "output-drain";
            await reads.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            timeoutPhase = phase;
        }
        catch (AndroidConnectionException exception)
        {
            diagnostic = exception.CommandDiagnostic ?? diagnostic with
            {
                ErrorCategory = "preflight-failed",
                ExceptionType = exception.InnerException?.GetType().FullName ?? exception.GetType().FullName,
                HResult = exception.InnerException?.HResult ?? exception.HResult,
                NativeErrorCode = exception.InnerException is Win32Exception native ? native.NativeErrorCode : null,
            };
            WriteDiagnostic(exception.ReasonCode, diagnostic, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw new AndroidConnectionException(exception.ReasonCode, exception.Message, exception)
            { CommandDiagnostic = diagnostic };
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            diagnostic = diagnostic with
            {
                ExceptionType = exception.GetType().FullName,
                HResult = exception.HResult,
                NativeErrorCode = exception is Win32Exception native ? native.NativeErrorCode : null,
                ErrorCategory = phase == "prepare" ? "runtime-prepare-failed" : launched ? "process-io-failed" : "process-start-failed",
            };
            string reason = phase == "prepare" ? "ANDROID_RUNTIME_PREPARE_FAILED" : launched ? "ANDROID_TOOL_IO_FAILED" : AndroidReasonCodes.ToolStartFailed;
            WriteDiagnostic(reason,
                diagnostic, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw new AndroidConnectionException(reason,
                phase == "prepare" ? "ADB 运行副本准备失败，请检查磁盘空间及访问权限" : launched ? "读取手机连接工具结果失败" : "无法启动手机连接工具，请检查组件及访问权限", exception)
            { CommandDiagnostic = diagnostic };
        }
        finally
        {
            await deadline.CancelAsync().ConfigureAwait(false);
            if (launched)
            {
                if (!process.HasExited) { cleanupError = TryKill(process); }
                if (reads is not null)
                {
                    // Close only this client's handles. A descendant may be a shared daemon.
                    process.StandardOutput.Dispose();
                    process.StandardError.Dispose();
                    try { await reads.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
                    catch (Exception exception) when (exception is OperationCanceledException or IOException or TimeoutException or ObjectDisposedException) { }
                }
                if (!process.HasExited)
                {
                    try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
                    catch (TimeoutException) { }
                }
                if (!process.HasExited || cleanupError is not null)
                {
                    WriteDiagnostic("ANDROID_TOOL_CLEANUP_INCOMPLETE", diagnostic with
                    { ClientStillRunning = !process.HasExited, CleanupNativeErrorCode = cleanupError }, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        int code = !launched || !process.HasExited || timedOut ? -1 : process.ExitCode;
        bool truncated = output.Truncated || error.Truncated;
        string errorCategory = AdbDiagnosticPolicy.ErrorCategory(error.Text);
        bool restarted = errorCategory == "server-version-mismatch" && error.Text.Contains("killing", StringComparison.OrdinalIgnoreCase);
        diagnostic = diagnostic with
        {
            ExitCode = code,
            TimedOut = timedOut,
            OutputTruncated = truncated,
            TimeoutPhase = timeoutPhase,
            ErrorCategory = code == 0 && !truncated && !restarted ? "none" : errorCategory,
            ClientStillRunning = launched && !process.HasExited,
            CleanupNativeErrorCode = cleanupError,
            NativeServerRestartAttempted = restarted,
            StandardErrorSummary = timedOut || truncated || code != 0 || restarted
                ? AdbOutputRedactor.Summarize(error.Text, arguments, standardInput, endpoint.Host) : null,
        };
        TimeSpan duration = Stopwatch.GetElapsedTime(started);
        if (timedOut || truncated || code != 0)
        { WriteDiagnostic(timedOut ? "ANDROID_TOOL_TIMEOUT" : truncated ? "ANDROID_TOOL_OUTPUT_LIMIT" : "ANDROID_TOOL_COMMAND_FAILED", diagnostic, duration.TotalMilliseconds); }
        else if (restarted) { WriteDiagnostic("ANDROID_SERVER_RESTART_CONFLICT", diagnostic, duration.TotalMilliseconds); }
        return new(code, output.Text, error.Text, duration, timedOut) { Diagnostic = diagnostic, OutputTruncated = truncated };
    }

    private void WriteDiagnostic(string code, AdbCommandDiagnostic diagnostic, double duration) =>
        log?.TryWrite(new(DateTimeOffset.UtcNow, "adb_command", code, "ADB 命令诊断",
            DurationMilliseconds: (long)duration)
        { AdbCommand = diagnostic });

    private int? TryKill(Process process)
    {
        try { if (!process.HasExited) { process.Kill(entireProcessTree: killProcessTree); } }
        catch (InvalidOperationException) { }
        catch (Win32Exception exception) { return exception.NativeErrorCode; }
        return null;
    }

    private sealed class BoundedOutput(int capacity)
    {
        private readonly object _gate = new();
        private readonly StringBuilder _text = new();
        private bool _truncated;
        public bool Truncated { get { lock (_gate) { return _truncated; } } }
        public string Text { get { lock (_gate) { return _text.ToString(); } } }
        public async Task ReadAsync(StreamReader reader, CancellationToken token)
        {
            char[] buffer = new char[4096];
            try
            {
                int count;
                while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
                {
                    lock (_gate)
                    {
                        int retained = Math.Min(count, capacity - _text.Length);
                        _text.Append(buffer, 0, retained);
                        _truncated |= retained < count;
                    }
                }
            }
            catch (Exception exception) when (token.IsCancellationRequested && exception is OperationCanceledException or IOException or ObjectDisposedException) { }
        }
    }
}
