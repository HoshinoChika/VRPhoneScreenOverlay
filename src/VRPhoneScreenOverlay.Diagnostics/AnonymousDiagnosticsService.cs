using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using VRPhoneScreenOverlay.Contracts;
using VRPhoneScreenOverlay.Network;
using VRPhoneScreenOverlay.Protocols;

namespace VRPhoneScreenOverlay.Diagnostics;

public sealed record DiagnosticsServiceOptions(
    Uri? InitUri,
    long MaximumBundleBytes,
    TimeSpan RequestTimeout)
{
    public string? FailedBundleDirectory { get; init; }
    internal string? OutgoingBundleDirectory { get; init; }

    public static DiagnosticsServiceOptions FromConfiguration(ClientServiceConfiguration configuration) => new(
        configuration.DiagnosticsInitUri,
        24L * 1024 * 1024,
        TimeSpan.FromSeconds(45));
}

public sealed record DiagnosticUploadContext(
    string AppVersion,
    IReadOnlyDictionary<string, object?> RuntimeSnapshot,
    IReadOnlyDictionary<string, object?> SettingsSnapshot,
    DiagnosticHardwareSummary HardwareSummary,
    DiagnosticSessionSummary SessionSummary,
    DiagnosticBindingSummary BindingSummary,
    DiagnosticUserReport UserReport,
    string? CurrentLogPath);

public sealed record DiagnosticProgress(string Phase, long CompletedBytes, long TotalBytes);

public sealed record DiagnosticUploadResult(
    bool Succeeded,
    string ReasonCode,
    string Message,
    DateTimeOffset? ReceivedAt,
    string? RetainedBundlePath = null);

public interface IAnonymousDiagnosticsService : IDisposable
{
    public ValueTask<DiagnosticUploadResult> BuildAndUploadAsync(
        DiagnosticUploadContext context,
        IProgress<DiagnosticProgress>? progress,
        CancellationToken cancellationToken);
}

public sealed partial class AnonymousDiagnosticsService : IAnonymousDiagnosticsService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };
    private readonly DiagnosticsServiceOptions _options;
    private readonly IAppHttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    public AnonymousDiagnosticsService(DiagnosticsServiceOptions options)
        : this(
            options,
            new AppHttpClient(new NetworkClientOptions(
                "diagnostics-client",
                (options ?? throw new ArgumentNullException(nameof(options))).RequestTimeout)
            { UseSystemProxy = false }),
            ownsHttpClient: true)
    {
    }

    public AnonymousDiagnosticsService(
        DiagnosticsServiceOptions options,
        IAppHttpClient httpClient,
        bool ownsHttpClient)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = ownsHttpClient;
    }

    public async ValueTask<DiagnosticUploadResult> BuildAndUploadAsync(
        DiagnosticUploadContext context,
        IProgress<DiagnosticProgress>? progress,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        string bundlePath = await DiagnosticBundleBuilder.BuildAsync(
                context,
                _options.MaximumBundleBytes,
                progress,
                cancellationToken,
                _options.OutgoingBundleDirectory)
            .ConfigureAwait(false);
        DiagnosticUploadResult result;
        try
        {
            result = await UploadBundleAsync(context, bundlePath, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is NetworkException or DiagnosticsException or
            OperationCanceledException or IOException or UnauthorizedAccessException or JsonException or HttpRequestException or FormatException)
        {
            result = new(false, exception is DiagnosticsException diagnostic ? diagnostic.ReasonCode :
                "DIAGNOSTICS_UPLOAD_FAILED", "诊断包上传失败", null);
        }
        if (!result.Succeeded)
        {
            progress?.Report(new("正在保留本地诊断包", 0, 1));
            string retained = await RetainBundleAsync(bundlePath).ConfigureAwait(false);
            return result with
            {
                RetainedBundlePath = retained,
                Message = retained == bundlePath
                    ? $"上传失败，诊断包仍保留在 {retained}；请联系作者并提供此文件。"
                    : "上传失败，诊断包已移至软件根目录；请联系作者并提供该文件。",
            };
        }
        try { File.Delete(bundlePath); }
        catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
        return result;
    }

    private Task<string> RetainBundleAsync(string bundlePath) => Task.Run(() =>
    {
        string directory = _options.FailedBundleDirectory ?? ResolveInstallationDirectory();
        string destination = Path.Combine(directory, $"VRPhoneScreenOverlay-Diagnostics-{Guid.NewGuid():N}.zip");
        try
        {
            File.Move(bundlePath, destination);
            return destination;
        }
        catch (Exception move) when (move is IOException or UnauthorizedAccessException)
        {
            // A read-only install must never cause the complete outgoing bundle to be deleted.
            return bundlePath;
        }
    });

    private static string ResolveInstallationDirectory()
    {
        string runtime = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        string? executableDirectory = Path.GetDirectoryName(Environment.ProcessPath);
        return executableDirectory is not null &&
            string.Equals(Path.GetFileName(Environment.ProcessPath), AppIdentity.ExecutableName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(runtime, Path.Combine(executableDirectory, "app"), StringComparison.OrdinalIgnoreCase)
                ? executableDirectory : runtime;
    }

    private async ValueTask<DiagnosticUploadResult> UploadBundleAsync(DiagnosticUploadContext context,
        string bundlePath, IProgress<DiagnosticProgress>? progress, CancellationToken cancellationToken)
    {
        if (_options.InitUri is null)
        { throw new DiagnosticsException("DIAGNOSTICS_SERVICE_NOT_CONFIGURED", "未配置诊断上传服务"); }
        {
            FileInfo bundle = new(bundlePath);
            string sha256;
            await using (FileStream stream = bundle.OpenRead())
            {
                sha256 = Convert.ToHexStringLower(
                    await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            }

            DiagnosticUploadInitRequest request = new(
                context.AppVersion,
                bundle.Length,
                sha256,
                DateTimeOffset.UtcNow);
            progress?.Report(new DiagnosticProgress("正在申请安全上传", 0, bundle.Length));

            // The init call carries the bundle's sha256 as its request identity, so the server
            // can collapse repeats. That makes it safe to retry.
            using HttpResponseMessage initResponse = await _httpClient.SendAsync(
                    () => new HttpRequestMessage(HttpMethod.Post, _options.InitUri)
                    {
                        Content = JsonContent.Create(request, options: _jsonOptions),
                    },
                    NetworkRetryPolicy.Idempotent,
                    HttpCompletionOption.ResponseHeadersRead,
                    null,
                    cancellationToken)
                .ConfigureAwait(false);
            DiagnosticUploadInitResponse upload = await NetworkResponseBody
                .ReadJsonAsync<DiagnosticUploadInitResponse>(initResponse.Content,
                    _jsonOptions, _options.RequestTimeout, cancellationToken)
                .ConfigureAwait(false) ?? throw new DiagnosticsException(
                    DiagnosticsReasonCodes.InitInvalid,
                    "诊断上传服务返回无效响应");
            if (bundle.Length > upload.MaximumBytes || upload.ExpiresAt <= DateTimeOffset.UtcNow ||
                string.IsNullOrWhiteSpace(upload.UploadToken) || upload.UploadToken.Any(char.IsControl) ||
                !Uri.TryCreate(upload.UploadUrl, UriKind.Absolute, out Uri? uploadUri) ||
                uploadUri.Scheme != Uri.UriSchemeHttps || uploadUri.UserInfo.Length != 0 ||
                !string.Equals(uploadUri.Authority, _options.InitUri.Authority, StringComparison.OrdinalIgnoreCase))
            {
                throw new DiagnosticsException(
                    DiagnosticsReasonCodes.UploadPolicyInvalid,
                    "诊断上传授权无效");
            }

            // The bundle body is a one-shot FileStream and the PUT creates a server-side object,
            // so this must run at most once. NetworkRetryPolicy.None is load bearing here.
            using HttpResponseMessage uploadResponse = await _httpClient.SendAsync(
                    () =>
                    {
                        HttpRequestMessage uploadRequest = new(HttpMethod.Put, uploadUri);
                        uploadRequest.Headers.Authorization = new AuthenticationHeaderValue(
                            "Bearer",
                            upload.UploadToken);
                        uploadRequest.Content = new ProgressStreamContent(
                            bundle.OpenRead(),
                            bundle.Length,
                            sent => progress?.Report(
                                new DiagnosticProgress("正在上传诊断包", sent, bundle.Length)));
                        uploadRequest.Content.Headers.ContentType =
                            new MediaTypeHeaderValue("application/zip");
                        return uploadRequest;
                    },
                    NetworkRetryPolicy.None,
                    HttpCompletionOption.ResponseHeadersRead,
                    null,
                    cancellationToken)
                .ConfigureAwait(false);

            Uri completeUri = new(_options.InitUri, "complete");

            // Completion carries the upload id, so repeating it names the same object.
            using HttpResponseMessage completeResponse = await _httpClient.SendAsync(
                    () => new HttpRequestMessage(HttpMethod.Post, completeUri)
                    {
                        Content = JsonContent.Create(
                            new DiagnosticUploadCompleteRequest(upload.UploadId, sha256),
                            options: _jsonOptions),
                    },
                    NetworkRetryPolicy.Idempotent,
                    HttpCompletionOption.ResponseHeadersRead,
                    null,
                    cancellationToken)
                .ConfigureAwait(false);
            DiagnosticUploadCompleteResponse completed = await NetworkResponseBody
                .ReadJsonAsync<DiagnosticUploadCompleteResponse>(
                    completeResponse.Content, _jsonOptions, _options.RequestTimeout,
                    cancellationToken)
                .ConfigureAwait(false) ?? throw new DiagnosticsException(
                    DiagnosticsReasonCodes.CompleteInvalid,
                    "诊断上传完成响应无效");
            return new DiagnosticUploadResult(
                completed.Accepted,
                completed.Accepted ? DiagnosticsReasonCodes.Uploaded : DiagnosticsReasonCodes.NotAccepted,
                completed.Accepted ? "匿名诊断包已上传" : "诊断包未被服务器接受",
                completed.ReceivedAt);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    [GeneratedRegex(@"(?i)(?:bearer\s+|sk-)[a-z0-9._-]{12,}")]
    internal static partial Regex SecretPattern();
}

public sealed class DiagnosticsException(
    string reasonCode,
    string message,
    Exception? inner = null) : Exception(message, inner)
{
    public string ReasonCode { get; } = reasonCode;
}

internal static class DiagnosticBundleBuilder
{
    private const long _maximumLogBytesPerFile = 5L * 1024 * 1024;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static async ValueTask<string> BuildAsync(
        DiagnosticUploadContext context,
        long maximumBundleBytes,
        IProgress<DiagnosticProgress>? progress,
        CancellationToken cancellationToken,
        string? outgoingDirectory = null)
    {
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string outputDirectory = outgoingDirectory ?? Path.Combine(
            localData,
            AppIdentity.LocalDataFolderName,
            "diagnostics",
            "outgoing");
        Directory.CreateDirectory(outputDirectory);
        string outputPath = Path.Combine(outputDirectory, $"diagnostic-{Guid.NewGuid():N}.zip");
        return await BuildAtPathAsync(context, maximumBundleBytes, progress, outputPath, cancellationToken).ConfigureAwait(false);
    }

    internal static async ValueTask<string> BuildAtPathAsync(DiagnosticUploadContext context, long maximumBundleBytes,
        IProgress<DiagnosticProgress>? progress, string outputPath, CancellationToken cancellationToken)
    {
        try { return await WriteBundleAsync(context, maximumBundleBytes, progress, outputPath, cancellationToken).ConfigureAwait(false); }
        catch
        {
            try { File.Delete(outputPath); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    private static async ValueTask<string> WriteBundleAsync(DiagnosticUploadContext context, long maximumBundleBytes,
        IProgress<DiagnosticProgress>? progress, string outputPath, CancellationToken cancellationToken)
    {
        string[] logs = SelectLogFiles(context.CurrentLogPath).ToArray();
        progress?.Report(new DiagnosticProgress("正在整理诊断信息", 0, 1));
        await using FileStream output = new(
            outputPath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous);
        using (ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteJsonAsync(archive, "diagnostic.json", new
            {
                schemaVersion = 1,
                product = AppIdentity.ProductName,
                appVersion = context.AppVersion,
                createdAt = DateTimeOffset.UtcNow,
                os = RuntimeInformation.OSDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                framework = RuntimeInformation.FrameworkDescription,
                culture = CultureInfo.CurrentCulture.Name,
                runtime = context.RuntimeSnapshot,
                collection = new
                {
                    logFileCount = logs.Length,
                    maximumLogFiles = 3,
                    maximumLogBytesPerFile = _maximumLogBytesPerFile,
                    phoneImagesIncluded = false,
                    audioIncluded = false,
                    bindingFilesIncluded = false
                },
            }, cancellationToken).ConfigureAwait(false);
            await WriteJsonAsync(
                    archive,
                    "settings-summary.json",
                    context.SettingsSnapshot,
                    cancellationToken)
                .ConfigureAwait(false);
            await WriteJsonAsync(
                    archive,
                    "hardware-summary.json",
                    context.HardwareSummary,
                    cancellationToken)
                .ConfigureAwait(false);
            await WriteJsonAsync(
                    archive,
                    "session-summary.json",
                    context.SessionSummary,
                    cancellationToken)
                .ConfigureAwait(false);
            await WriteJsonAsync(
                    archive,
                    "binding-summary.json",
                    context.BindingSummary,
                    cancellationToken)
                .ConfigureAwait(false);
            await WriteJsonAsync(
                    archive,
                    "user-report.json",
                    context.UserReport,
                    cancellationToken)
                .ConfigureAwait(false);

            foreach (string logPath in logs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string entryName = $"logs/{Path.GetFileName(logPath)}";
                ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                await using Stream destination = await entry.OpenAsync(cancellationToken)
                    .ConfigureAwait(false);
                await CopySanitizedLogTailAsync(logPath, destination, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        if (output.Length <= 0 || output.Length > maximumBundleBytes)
        {
            throw new DiagnosticsException(
                DiagnosticsReasonCodes.BundleSizeInvalid,
                "诊断包大小超出限制");
        }

        progress?.Report(new DiagnosticProgress("诊断包已生成", output.Length, output.Length));
        return outputPath;
    }

    private static IEnumerable<string> SelectLogFiles(string? currentLogPath)
    {
        string? directory = string.IsNullOrWhiteSpace(currentLogPath)
            ? null
            : Path.GetDirectoryName(Path.GetFullPath(currentLogPath));
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            yield break;
        }

        foreach (FileInfo file in new DirectoryInfo(directory)
                     .EnumerateFiles("android-*.jsonl")
                     .OrderByDescending(file => file.LastWriteTimeUtc)
                     .Take(3))
        {
            yield return file.FullName;
        }
    }

    private static async ValueTask CopySanitizedLogTailAsync(
        string path,
        Stream destination,
        CancellationToken cancellationToken)
    {
        await using FileStream source = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (source.Length > _maximumLogBytesPerFile)
        {
            source.Seek(-_maximumLogBytesPerFile, SeekOrigin.End);
        }

        using StreamReader reader = new(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        await using StreamWriter writer = new(destination, new UTF8Encoding(false), leaveOpen: true);
        if (source.Position > 0)
        {
            _ = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        }

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            string sanitized = DiagnosticSanitizer.SanitizeJsonLine(line);
            await writer.WriteLineAsync(sanitized.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask WriteJsonAsync<T>(
        ZipArchive archive,
        string name,
        T value,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        await using Stream stream = await entry.OpenAsync(cancellationToken)
            .ConfigureAwait(false);
        JsonNode? node = JsonSerializer.SerializeToNode(value, _jsonOptions);
        DiagnosticSanitizer.Sanitize(node);
        await JsonSerializer.SerializeAsync(stream, node, _jsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }
}

internal static partial class DiagnosticSanitizer
{
    private static readonly JsonSerializerOptions _lineJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string SanitizeJsonLine(string line)
    {
        try
        {
            JsonNode? node = JsonNode.Parse(line);
            if (node is JsonValue root && root.TryGetValue(out string? rootText) && rootText is not null)
            {
                node = JsonValue.Create(SanitizeText(rootText));
            }
            Sanitize(node);
            return node?.ToJsonString(_lineJsonOptions) ?? string.Empty;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            // A truncated write or duplicate property cannot be safely sanitized
            // as structured data. Never export its raw text via a fallback path.
            return "{\"reasonCode\":\"DIAGNOSTIC_LOG_LINE_INVALID\",\"message\":\"Unparseable log entry omitted\"}";
        }
    }

    public static void Sanitize(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach ((string key, JsonNode? value) in obj.ToArray())
            {
                if (value is not null && SensitiveFieldPattern().IsMatch(key))
                {
                    obj[key] = "<redacted>";
                    continue;
                }
                if (value is JsonValue jsonValue &&
                    jsonValue.TryGetValue(out string? text) &&
                    text is not null)
                {
                    obj[key] = SanitizeText(text);
                }
                else
                {
                    Sanitize(value);
                }
            }
        }
        else if (node is JsonArray array)
        {
            for (int index = 0; index < array.Count; index++)
            {
                JsonNode? value = array[index];
                if (value is JsonValue jsonValue && jsonValue.TryGetValue(out string? text) && text is not null)
                {
                    array[index] = SanitizeText(text);
                }
                else
                {
                    Sanitize(value);
                }
            }
        }
    }

    internal static string SanitizeText(string value)
    {
        string sanitized = value;
        foreach ((string path, string replacement) in SensitiveDirectories())
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                sanitized = sanitized.Replace(
                    path,
                    replacement,
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        sanitized = AnonymousDiagnosticsService.SecretPattern().Replace(sanitized, "<redacted>");
        sanitized = NamedSecretPattern().Replace(sanitized, "$1=<redacted>");
        sanitized = DeviceSerialPattern().Replace(sanitized, "$1=<redacted>");
        sanitized = PrivateKeyPattern().Replace(sanitized, "<redacted-private-key>");
        sanitized = ProfilePathPattern().Replace(sanitized, "<user-profile>");
        sanitized = PairingCodePattern().Replace(sanitized, "$1=<redacted>");
        return sanitized;
    }

    [GeneratedRegex(@"-----BEGIN (?:[A-Z ]+)?PRIVATE KEY-----[\s\S]*?-----END (?:[A-Z ]+)?PRIVATE KEY-----")]
    private static partial Regex PrivateKeyPattern();

    [GeneratedRegex(@"(?i)(?:[a-z]:[\\/]Users[\\/]|/home/|/Users/)[^\\/\s]+")]
    private static partial Regex ProfilePathPattern();

    [GeneratedRegex(@"(?i)\b(pairing[_-]?code|pair[_-]?code)\s*[:=]\s*[^\s,;]+")]
    private static partial Regex PairingCodePattern();

    private static IEnumerable<(string Path, string Replacement)> SensitiveDirectories()
    {
        yield return (
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "<user-profile>");
        yield return (
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "<local-data>");
        yield return (
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "<documents>");
        yield return (Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), "<temp>");
    }

    [GeneratedRegex(
        @"(?i)\b(token|password|passwd|secret|api[_-]?key|authorization)\s*[:=]\s*[^\s,;\""']{4,}")]
    private static partial Regex NamedSecretPattern();

    [GeneratedRegex(@"^(token|password|passwd|secret|api[_-]?key|authorization|serial|device[_-]?serial|pairing[_-]?code|access[_-]?token|refresh[_-]?token)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveFieldPattern();

    [GeneratedRegex(@"(?i)\b(serial|device[_-]?serial)\s*[:=]\s*[^\s,;\""']{4,}")]
    private static partial Regex DeviceSerialPattern();
}

internal sealed class ProgressStreamContent(
    Stream source,
    long length,
    Action<long> progress) : HttpContent
{
    protected override void Dispose(bool disposing)
    {
        // Ownership starts when the request is built, including failures before serialization.
        if (disposing) { source.Dispose(); }
        base.Dispose(disposing);
    }

    protected override bool TryComputeLength(out long computedLength)
    {
        computedLength = length;
        return true;
    }

    protected override async Task SerializeToStreamAsync(
        Stream stream,
        TransportContext? context)
    {
        await using (source.ConfigureAwait(false))
        {
            byte[] buffer = new byte[128 * 1024];
            long sent = 0;
            while (true)
            {
                int read = await source.ReadAsync(buffer).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                await stream.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                sent += read;
                progress(sent);
            }
        }
    }
}
