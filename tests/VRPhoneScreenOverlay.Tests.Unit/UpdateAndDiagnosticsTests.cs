using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using VRPhoneScreenOverlay.Diagnostics;
using VRPhoneScreenOverlay.Network;
using VRPhoneScreenOverlay.Protocols;
using VRPhoneScreenOverlay.Service;
using VRPhoneScreenOverlay.Update;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class UpdateAndDiagnosticsTests
{
    [Theory]
    [InlineData(0)] // Init failure.
    [InlineData(1)] // Body upload failure.
    [InlineData(2)] // Completion failure.
    [InlineData(3)] // Server rejected the completed upload.
    [InlineData(4)] // Cancellation after the bundle exists.
    public async Task UploadFailureMovesCompleteBundleToInstallationAndAsksToContactAuthor(int failure)
    {
        using TemporaryDirectory directory = new();
        string outgoing = Path.Combine(directory.Path, "outgoing");
        DiagnosticsServiceOptions options = DiagnosticsServiceOptions.FromConfiguration(TestServiceConfiguration.Value) with
        { FailedBundleDirectory = directory.Path, OutgoingBundleDirectory = outgoing };
        using DiagnosticHttpClient client = new(failure);
        using AnonymousDiagnosticsService service = new(options, client, false);
        DiagnosticUploadResult result = await service.BuildAndUploadAsync(SyntheticContext(), null, CancellationToken.None);
        Assert.False(result.Succeeded);
        string retained = Assert.IsType<string>(result.RetainedBundlePath);
        Assert.Equal(directory.Path, Path.GetDirectoryName(retained));
        Assert.StartsWith("VRPhoneScreenOverlay-Diagnostics-", Path.GetFileName(retained));
        Assert.Contains("联系作者", result.Message);
        Assert.Empty(Directory.EnumerateFiles(outgoing));
        using ZipArchive zip = await ZipFile.OpenReadAsync(retained, CancellationToken.None);
        Assert.NotNull(zip.GetEntry("diagnostic.json"));
        Assert.NotNull(zip.GetEntry("user-report.json"));
    }

    [Fact]
    public async Task ReadOnlyInstallationKeepsOriginalBundleAndSuccessfulUploadDeletesOnlyOutgoingBundle()
    {
        using TemporaryDirectory directory = new();
        string outgoing = Path.Combine(directory.Path, "outgoing");
        DiagnosticsServiceOptions options = DiagnosticsServiceOptions.FromConfiguration(TestServiceConfiguration.Value) with
        { FailedBundleDirectory = Path.Combine(directory.Path, "nonexistent"), OutgoingBundleDirectory = outgoing };
        using DiagnosticHttpClient failedClient = new(0);
        using AnonymousDiagnosticsService failed = new(options, failedClient, false);
        DiagnosticUploadResult result = await failed.BuildAndUploadAsync(SyntheticContext(), null, CancellationToken.None);
        string retained = Assert.IsType<string>(result.RetainedBundlePath);
        Assert.Equal(outgoing, Path.GetDirectoryName(retained));
        Assert.True(File.Exists(retained));
        Assert.Contains(retained, result.Message);
        using DiagnosticHttpClient successfulClient = new(-1);
        using AnonymousDiagnosticsService success = new(options, successfulClient, false);
        Assert.True((await success.BuildAndUploadAsync(SyntheticContext(), null, CancellationToken.None)).Succeeded);
        Assert.Equal(retained, Assert.Single(Directory.EnumerateFiles(outgoing)));
    }

    private static DiagnosticUploadContext SyntheticContext() => new("test", new Dictionary<string, object?>(),
        new Dictionary<string, object?>(), CreateHardwareSummary(), CreateSessionSummary(), CreateBindingSummary(),
        new(DiagnosticIssueType.Other, DateTimeOffset.UtcNow, "synthetic"), null);

    private sealed class DiagnosticHttpClient(int failure) : IAppHttpClient
    {
        private int _step;
        public async ValueTask<System.Net.Http.HttpResponseMessage> SendAsync(Func<System.Net.Http.HttpRequestMessage> createRequest,
            NetworkRetryPolicy retryPolicy, System.Net.Http.HttpCompletionOption completionOption,
            Func<System.Net.HttpStatusCode, bool>? isSuccess, CancellationToken cancellationToken)
        {
            using System.Net.Http.HttpRequestMessage request = createRequest();
            if (failure == 4) { throw new OperationCanceledException(); }
            int step = _step++;
            if (step == failure) { throw new NetworkException("TEST_FAILURE", "synthetic failure"); }
            if (request.Content is not null) { _ = await request.Content.ReadAsByteArrayAsync(cancellationToken); }
            object body = step == 0
                ? new DiagnosticUploadInitResponse("test", new Uri(request.RequestUri!, "upload/test").AbsoluteUri,
                    "synthetic-scoped-token", 24L * 1024 * 1024, DateTimeOffset.UtcNow.AddMinutes(5))
                : new DiagnosticUploadCompleteResponse(failure != 3, DateTimeOffset.UtcNow);
            return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new System.Net.Http.StringContent(JsonSerializer.Serialize(body)) };
        }
        public void Dispose() { }
    }

    [Theory]
    [InlineData("0.2.5", "0.2.6-beta.1", -1)]
    [InlineData("0.2.6-beta.1", "0.2.6-beta.2", -1)]
    [InlineData("0.2.6-beta.2", "0.2.6", -1)]
    [InlineData("0.2.6", "0.2.6-beta.2", 1)]
    public void SemanticVersionsOrderReleaseAndPrerelease(
        string left,
        string right,
        int expectedSign)
    {
        Assert.True(SemanticVersion.TryParse(left, out SemanticVersion first));
        Assert.True(SemanticVersion.TryParse(right, out SemanticVersion second));
        Assert.Equal(expectedSign, Math.Sign(first.CompareTo(second)));
    }

    [Fact]
    public async Task FailedDiagnosticBundleLeavesNoPartialArchive()
    {
        using TemporaryDirectory directory = new();
        string path = System.IO.Path.Combine(directory.Path, "partial.zip");
        DiagnosticUploadContext context = new("test", new Dictionary<string, object?>(), new Dictionary<string, object?>(),
            CreateHardwareSummary(), CreateSessionSummary(), CreateBindingSummary(),
            new DiagnosticUserReport(DiagnosticIssueType.Other, DateTimeOffset.UtcNow, "synthetic"), null);
        await Assert.ThrowsAsync<DiagnosticsException>(() => DiagnosticBundleBuilder.BuildAtPathAsync(context, 1, null,
            path, CancellationToken.None).AsTask());
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task DiagnosticBundleContainsSummariesAndRedactsSecrets()
    {
        using TemporaryDirectory directory = new();
        string log = Path.Combine(directory.Path, "android-20260818.jsonl");
        string userPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        await File.WriteAllTextAsync(
            log,
            JsonSerializer.Serialize(new
            {
                message = $"{userPath} sk-sensitive-token-123456789",
                exceptionMessage = $"serial=ABC123456 {string.Concat("pass", "word")}=hunter2 " +
                    $"path={userPath}\\private.txt",
            }) + "\n" + "{\"token\":\"synthetic-truncated-value\"\n" +
            "{\"message\":\"following-valid-entry\"}\n");
        DiagnosticUploadContext context = new(
            "0.2.6-beta.1",
            new Dictionary<string, object?> { ["videoState"] = "Running" },
            new Dictionary<string, object?> { ["bitrateMbps"] = 32 },
            CreateHardwareSummary(),
            CreateSessionSummary(),
            CreateBindingSummary(),
            new DiagnosticUserReport(
                DiagnosticIssueType.Video,
                DateTimeOffset.Now.AddMinutes(-5),
                $"旋转后画面冻结；日志在 {userPath}；token=very-secret-token"),
            log);

        string bundle = await DiagnosticBundleBuilder.BuildAsync(
            context,
            24L * 1024 * 1024,
            null,
            CancellationToken.None);
        try
        {
            using ZipArchive archive = await ZipFile.OpenReadAsync(
                bundle,
                CancellationToken.None);
            Assert.NotNull(archive.GetEntry("diagnostic.json"));
            Assert.NotNull(archive.GetEntry("settings-summary.json"));
            string hardware = await ReadEntryAsync(archive, "hardware-summary.json");
            string session = await ReadEntryAsync(archive, "session-summary.json");
            string binding = await ReadEntryAsync(archive, "binding-summary.json");
            Assert.Contains("\"videoDecoderBackend\": \"hardware: decoder\"", hardware);
            Assert.Contains("\"submittedFrames\": 300", session);
            Assert.Contains("\"controllerType\": \"oculus_touch\"", binding);
            Assert.Contains("\"source\": \"SavedUser\"", binding);
            Assert.Contains("\"pointerPoseActive\": true", binding);
            Assert.Contains("\"touchActive\": true", binding);
            Assert.Contains("\"grabActive\": true", binding);
            Assert.Contains("\"scaleActive\": true", binding);
            ZipArchiveEntry reportEntry = Assert.IsType<ZipArchiveEntry>(
                archive.GetEntry("user-report.json"));
            ZipArchiveEntry logEntry = Assert.IsType<ZipArchiveEntry>(
                archive.GetEntry("logs/android-20260818.jsonl"));
            await using Stream reportStream = await reportEntry.OpenAsync(CancellationToken.None);
            using StreamReader reportReader = new(reportStream);
            string report = await reportReader.ReadToEndAsync();
            Assert.Contains("\"issueType\": \"Video\"", report, StringComparison.Ordinal);
            Assert.DoesNotContain(userPath, report, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("very-secret-token", report, StringComparison.Ordinal);
            Assert.Contains("<redacted>", report, StringComparison.Ordinal);
            await using Stream logStream = await logEntry.OpenAsync(CancellationToken.None);
            using StreamReader reader = new(logStream);
            string content = await reader.ReadToEndAsync();
            Assert.DoesNotContain(userPath, content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sk-sensitive", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ABC123456", content, StringComparison.Ordinal);
            Assert.DoesNotContain("hunter2", content, StringComparison.Ordinal);
            Assert.Contains("<redacted>", content, StringComparison.Ordinal);
            Assert.DoesNotContain("synthetic-truncated-value", content, StringComparison.Ordinal);
            Assert.Contains("DIAGNOSTIC_LOG_LINE_INVALID", content, StringComparison.Ordinal);
            Assert.Contains("following-valid-entry", content, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(bundle);
        }
    }

    [Fact]
    public async Task DiagnosticStoreAcceptsOneTimeBoundedUpload()
    {
        using TemporaryDirectory directory = new();
        ServiceOptions options = new(directory.Path, 1024 * 1024, 14, 8);
        DiagnosticUploadStore store = new(options);
        byte[] zip = CreateZip();
        string hash = Convert.ToHexStringLower(SHA256.HashData(zip));
        DiagnosticUploadInitResponse? init = await store.CreateAsync(
            new DiagnosticUploadInitRequest("0.2.6-beta.1", zip.Length, hash, DateTimeOffset.UtcNow),
            "127.0.0.1",
            "https",
            "example.test",
            CancellationToken.None);
        Assert.NotNull(init);

        DiagnosticStoreResult uploaded = await store.UploadAsync(
            init.UploadId,
            init.UploadToken,
            new MemoryStream(zip),
            zip.Length,
            CancellationToken.None);
        Assert.True(uploaded.Succeeded);
        DiagnosticUploadCompleteResponse? completed = await store.CompleteAsync(
            new DiagnosticUploadCompleteRequest(init.UploadId, hash),
            CancellationToken.None);
        Assert.True(completed?.Accepted);
        Assert.Single(Directory.EnumerateFiles(
            Path.Combine(directory.Path, "diagnostics", "completed"),
            "*.zip"));
    }

    private static byte[] CreateZip()
    {
        using MemoryStream output = new();
        using (ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry entry = archive.CreateEntry("diagnostic.json");
            using StreamWriter writer = new(entry.Open());
            writer.Write("{}");
        }

        return output.ToArray();
    }

    private static async Task<string> ReadEntryAsync(ZipArchive archive, string name)
    {
        ZipArchiveEntry entry = Assert.IsType<ZipArchiveEntry>(archive.GetEntry(name));
        await using Stream stream = await entry.OpenAsync(CancellationToken.None);
        using StreamReader reader = new(stream);
        return await reader.ReadToEndAsync();
    }

    internal static DiagnosticHardwareSummary CreateHardwareSummary() => new(
        "Google",
        "google",
        "Pixel",
        "pixel",
        "16",
        36,
        "arm64-v8a",
        "Usb",
        1080,
        2400,
        "hardware: decoder",
        "GPU; D3D11 feature level 12_0",
        42,
        "Headset",
        "oculus_touch");

    internal static DiagnosticSessionSummary CreateSessionSummary() => new(
        DateTimeOffset.UtcNow,
        "Running",
        "MEDIA_RUNNING",
        "媒体运行中",
        "Running",
        "VIDEO_OVERLAY_RUNNING",
        "画面运行中",
        1080,
        2400,
        300,
        2,
        59.5,
        100,
        16,
        60,
        15.2,
        17.8,
        42,
        "Playing",
        "AUDIO_PLAYING",
        "音频播放中",
        200,
        1,
        40,
        true,
        "Ready",
        "PHONE_CONTROL_READY",
        "控制已就绪",
        15,
        7,
        2.5,
        "Ready",
        "PLAYSPACE_DRAG_READY",
        "空间拖拽已就绪",
        false,
        1,
        0,
        0,
        0);

    internal static DiagnosticBindingSummary CreateBindingSummary() => new(
        true,
        "ABCDEF",
        "Right",
        [new DiagnosticBindingProfileSummary("oculus_touch", "SavedUser")],
        "Ready",
        "OPENVR_BINDING_READY",
        "SteamVR 手柄绑定已加载",
        "oculus_touch",
        true,
        true,
        true,
        true,
        true,
        true,
        true,
        true,
        true);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"vrpso-update-diagnostics-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
