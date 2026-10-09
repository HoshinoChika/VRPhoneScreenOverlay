using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using VRPhoneScreenOverlay.Network;
using VRPhoneScreenOverlay.Protocols;

namespace VRPhoneScreenOverlay.Update;

public sealed record UpdateServiceOptions(
    Uri? ManifestUri,
    string Channel,
    long MaximumPackageBytes,
    TimeSpan RequestTimeout)
{
    public static UpdateServiceOptions FromConfiguration(ClientServiceConfiguration configuration) => new(
        configuration.UpdateManifestUri,
        configuration.UpdateChannel,
        350L * 1024 * 1024,
        TimeSpan.FromSeconds(30));
}

public sealed record UpdateRelease(
    string Version,
    DateTimeOffset PublishedAt,
    Uri PackageUri,
    long PackageSize,
    string PackageSha256,
    string ReleaseNotes,
    Uri? RefreshUri = null,
    DateTimeOffset? DownloadExpiresAt = null);

public sealed record UpdateCheckResult(
    bool UpdateAvailable,
    string ReasonCode,
    string Message,
    UpdateRelease? Release);

public sealed record UpdateDownloadProgress(long ReceivedBytes, long TotalBytes);

public sealed record PreparedUpdate(
    string Version,
    string StageDirectory,
    string MaintenanceRunnerPath,
    string InstallDirectory,
    string HealthFilePath,
    string TransactionToken);

public interface IUpdateService : IDisposable
{
    public ValueTask<UpdateCheckResult> CheckAsync(
        string currentVersion,
        CancellationToken cancellationToken);

    public ValueTask<PreparedUpdate> DownloadAndStageAsync(
        UpdateRelease release,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken);

    public void LaunchMaintenance(PreparedUpdate update, int currentProcessId);
}

public sealed class HttpUpdateService : IUpdateService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };
    private readonly UpdateServiceOptions _options;
    private readonly IAppHttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ECDsa _manifestVerifier;
    private bool _disposed;

    public HttpUpdateService(UpdateServiceOptions options)
        : this(
            options,
            new AppHttpClient(new NetworkClientOptions(
                "update-client",
                (options ?? throw new ArgumentNullException(nameof(options))).RequestTimeout)
            { UseSystemProxy = false }),
            ownsHttpClient: true)
    {
    }

    public HttpUpdateService(
        UpdateServiceOptions options,
        IAppHttpClient httpClient,
        bool ownsHttpClient)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = ownsHttpClient;
        _manifestVerifier = ECDsa.Create();
        _manifestVerifier.ImportFromPem(ReadPublicKey());
    }

    public async ValueTask<UpdateCheckResult> CheckAsync(
        string currentVersion,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_options.ManifestUri is null)
        { return new(false, "UPDATE_SERVICE_NOT_CONFIGURED", "未配置更新服务", null); }

        // Fetching a manifest is a pure read, so transient failures may be retried. A 404 is a
        // real answer ("nothing published on this channel"), not a transport failure.
        using HttpResponseMessage response = await _httpClient.SendAsync(
                () => new HttpRequestMessage(HttpMethod.Get, _options.ManifestUri),
                NetworkRetryPolicy.Idempotent,
                HttpCompletionOption.ResponseHeadersRead,
                static status => (int)status is >= 200 and <= 299 ||
                    status == System.Net.HttpStatusCode.NotFound,
                cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new UpdateCheckResult(
                false,
                UpdateReasonCodes.NotPublished,
                "测试通道暂时没有可用更新",
                null);
        }

        SignedUpdateManifest manifest = await NetworkResponseBody.ReadJsonAsync<SignedUpdateManifest>(
                response.Content,
                _jsonOptions,
                _options.RequestTimeout,
                cancellationToken)
            .ConfigureAwait(false) ?? throw new UpdateException(
                UpdateReasonCodes.ManifestInvalid,
                "更新清单为空");
        byte[] payload = DecodeBase64(manifest.Payload, UpdateReasonCodes.ManifestPayloadInvalid);
        byte[] signature = DecodeBase64(manifest.Signature, UpdateReasonCodes.ManifestSignatureInvalid);
        if (!_manifestVerifier.VerifyData(payload, signature, HashAlgorithmName.SHA256))
        {
            throw new UpdateException(
                UpdateReasonCodes.ManifestSignatureInvalid,
                "更新清单签名验证失败");
        }

        UpdateReleasePayload descriptor = JsonSerializer.Deserialize<UpdateReleasePayload>(
            payload,
            _jsonOptions) ?? throw new UpdateException(
                UpdateReasonCodes.ManifestPayloadInvalid,
                "更新清单内容无效");
        ValidateDescriptor(descriptor);
        if (!SemanticVersion.TryParse(currentVersion, out SemanticVersion current) ||
            !SemanticVersion.TryParse(descriptor.Version, out SemanticVersion available))
        {
            throw new UpdateException(
                UpdateReasonCodes.VersionInvalid,
                "更新版本号无效");
        }

        if (available.CompareTo(current) <= 0)
        {
            return new UpdateCheckResult(
                false,
                UpdateReasonCodes.AlreadyCurrent,
                $"当前已是最新测试版 {currentVersion}",
                null);
        }

        Uri packageUri = Uri.TryCreate(descriptor.PackageUrl, UriKind.Absolute, out Uri? absolute)
            ? absolute
            : new Uri(_options.ManifestUri, descriptor.PackageUrl);
        Uri refreshUri = packageUri;
        DateTimeOffset? downloadExpiresAt = null;
        if (manifest.Download is { } lease && lease.Version == descriptor.Version &&
            lease.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(30) && lease.ExpiresAt < DateTimeOffset.UtcNow.AddDays(2) &&
            Uri.TryCreate(lease.Url, UriKind.Absolute, out Uri? direct) && UpdateDownloadAddressPolicy.IsAllowed(direct))
        {
            packageUri = direct;
            downloadExpiresAt = lease.ExpiresAt;
        }
        return new UpdateCheckResult(
            true,
            UpdateReasonCodes.Available,
            $"发现测试版 {descriptor.Version}",
            new UpdateRelease(
                descriptor.Version,
                descriptor.PublishedAt,
                packageUri,
                descriptor.PackageSize,
                descriptor.PackageSha256.ToLowerInvariant(),
                descriptor.ReleaseNotes, refreshUri, downloadExpiresAt));
    }

    public ValueTask<PreparedUpdate> DownloadAndStageAsync(
        UpdateRelease release,
        IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(release);
        return UpdatePackageStager.DownloadAndStageAsync(
            _httpClient,
            release,
            _options.MaximumPackageBytes,
            _options.RequestTimeout,
            progress,
            cancellationToken);
    }

    public void LaunchMaintenance(PreparedUpdate update, int currentProcessId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(update);
        ArgumentOutOfRangeException.ThrowIfLessThan(currentProcessId, 1);
        ProcessStartInfo startInfo = new()
        {
            FileName = update.MaintenanceRunnerPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(update.MaintenanceRunnerPath),
        };
        foreach (string argument in new[]
                 {
                     "--apply-update",
                     "--parent-pid",
                     currentProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     "--install-dir",
                     update.InstallDirectory,
                     "--stage-dir",
                     update.StageDirectory,
                     "--health-file",
                     update.HealthFilePath,
                     "--token",
                     update.TransactionToken,
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (Process.Start(startInfo) is null)
        {
            throw new UpdateException(
                UpdateReasonCodes.MaintenanceStartFailed,
                "无法启动更新维护程序");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _manifestVerifier.Dispose();
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private void ValidateDescriptor(UpdateReleasePayload descriptor)
    {
        if (descriptor.SchemaVersion != UpdatePackageFormat.SchemaVersion || descriptor.PackageFormat != UpdatePackageFormat.FullInstall ||
            !string.Equals(descriptor.Channel, _options.Channel, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(descriptor.Version) ||
            string.IsNullOrWhiteSpace(descriptor.PackageUrl) ||
            descriptor.PackageSize is <= 0 ||
            descriptor.PackageSize > _options.MaximumPackageBytes ||
            descriptor.PackageSha256.Length != 64 ||
            !descriptor.PackageSha256.All(Uri.IsHexDigit))
        {
            throw new UpdateException(
                UpdateReasonCodes.ManifestPayloadInvalid,
                "更新清单内容无效");
        }
    }

    private static byte[] DecodeBase64(string value, string reasonCode)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException exception)
        {
            throw new UpdateException(reasonCode, "更新清单编码无效", exception);
        }
    }

    private static string ReadPublicKey()
    {
        Assembly assembly = typeof(HttpUpdateService).Assembly;
        string resourceName = assembly.GetManifestResourceNames().Single(
            name => name.EndsWith("update-public-key.pem", StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resourceName) ??
            throw new InvalidOperationException("Embedded update public key is missing.");
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}

public sealed class UpdateException(
    string reasonCode,
    string message,
    Exception? inner = null) : Exception(message, inner)
{
    public string ReasonCode { get; } = reasonCode;
}

internal readonly record struct SemanticVersion(
    int Major,
    int Minor,
    int Patch,
    string Prerelease) : IComparable<SemanticVersion>
{
    public int CompareTo(SemanticVersion other)
    {
        int core = Major.CompareTo(other.Major);
        if (core == 0)
        {
            core = Minor.CompareTo(other.Minor);
        }

        if (core == 0)
        {
            core = Patch.CompareTo(other.Patch);
        }

        if (core != 0)
        {
            return core;
        }

        if (string.IsNullOrEmpty(Prerelease))
        {
            return string.IsNullOrEmpty(other.Prerelease) ? 0 : 1;
        }

        if (string.IsNullOrEmpty(other.Prerelease))
        {
            return -1;
        }

        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    public static bool TryParse(string value, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string withoutBuild = value.Split('+', 2)[0];
        string[] pieces = withoutBuild.Split('-', 2);
        string[] core = pieces[0].Split('.');
        if (core.Length is < 2 or > 4 ||
            !int.TryParse(core[0], out int major) ||
            !int.TryParse(core[1], out int minor) ||
            (core.Length >= 3 && !int.TryParse(core[2], out _)))
        {
            return false;
        }

        int patch = core.Length >= 3
            ? int.Parse(core[2], System.Globalization.CultureInfo.InvariantCulture)
            : 0;
        version = new SemanticVersion(major, minor, patch, pieces.Length == 2 ? pieces[1] : string.Empty);
        return true;
    }

    private static int ComparePrerelease(string left, string right)
    {
        string[] leftParts = left.Split('.');
        string[] rightParts = right.Split('.');
        for (int index = 0; index < Math.Max(leftParts.Length, rightParts.Length); index++)
        {
            if (index >= leftParts.Length)
            {
                return -1;
            }

            if (index >= rightParts.Length)
            {
                return 1;
            }

            bool leftNumeric = int.TryParse(leftParts[index], out int leftNumber);
            bool rightNumeric = int.TryParse(rightParts[index], out int rightNumber);
            int compared = leftNumeric && rightNumeric
                ? leftNumber.CompareTo(rightNumber)
                : string.Compare(leftParts[index], rightParts[index], StringComparison.Ordinal);
            if (compared != 0)
            {
                return compared;
            }
        }

        return 0;
    }
}
