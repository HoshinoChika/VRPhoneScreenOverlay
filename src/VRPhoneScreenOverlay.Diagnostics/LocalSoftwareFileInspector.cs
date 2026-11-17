using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VRPhoneScreenOverlay.Protocols;

namespace VRPhoneScreenOverlay.Diagnostics;

/// <summary>Fixed, bounded software file checks. No model paths, file contents, credentials or user filenames leave this service.</summary>
public sealed partial class LocalSoftwareFileInspector(string applicationDirectory, string dataDirectory, string runningVersion)
{
    private const long _maximumFileBytes = 32 * 1024 * 1024;
    private const int _maximumJsonBytes = 256 * 1024;
    private readonly string _application = Path.GetFullPath(applicationDirectory);
    private readonly string _data = Path.GetFullPath(dataDirectory);

    public async Task<TroubleshootingCheckResult> InspectAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        CancellationToken token = deadline.Token;
        try
        {
            List<TroubleshootingEvidence> evidence = [];
            evidence.Add(new("components", "software_files", string.Join(';',
                await HashGroupAsync("Adb", Path.Combine(_application, "resources", "android-platform-tools"), token).ConfigureAwait(false),
                await HashGroupAsync("Adb", Path.Combine(_data, "adb-runtime"), token, runtimeCopy: true).ConfigureAwait(false),
                await HashGroupAsync("Scrcpy", Path.Combine(_application, "resources", "scrcpy"), token).ConfigureAwait(false),
                await HashGroupAsync("OpenVr", _application, token).ConfigureAwait(false))));
            evidence.Add(new("binding_tool", "software_files", await InspectBindingToolAsync(token).ConfigureAwait(false)));
            List<string> binaries = [];
            foreach (string name in new[] { "VRPhoneScreenOverlay.dll", "VRPhoneScreenOverlay.Diagnostics.dll",
                "VRPhoneScreenOverlay.Android.dll", "VRPhoneScreenOverlay.Session.dll", "VRPhoneScreenOverlay.SteamVR.dll",
                "VRPhoneScreenOverlay.Maintenance.exe", "VRPhoneScreenOverlay.SteamVR.BindingTool.exe" })
            {
                token.ThrowIfCancellationRequested();
                binaries.Add(name + "=" + await InspectFileAsync(Path.Combine(_application, name), false, null, token).ConfigureAwait(false));
            }
            evidence.Add(new("binaries", "software_files", string.Join(';', binaries) + ";binaryCheck=readability_only;fullInstallationHashNotChecked=true"));
            List<string> configuration = [];
            foreach (string name in new[] { "settings.json", "connections.json", "steamvr/action_manifest.json" })
            {
                configuration.Add(name + "=" + await InspectFileAsync(Path.Combine(_data, name), true, null, token).ConfigureAwait(false));
            }
            foreach (string name in new[] { "action_manifest.json", "manifest.vrmanifest", "service.config.json",
                "VRPhoneScreenOverlay.SteamVR.BindingTool.deps.json", "VRPhoneScreenOverlay.SteamVR.BindingTool.runtimeconfig.json" })
            {
                configuration.Add("packaged_" + name + "=" + await InspectFileAsync(Path.Combine(_application, name), true, null, token).ConfigureAwait(false));
            }
            evidence.Add(new("configuration", "software_files", string.Join(';', configuration) +
                ";jsonCheck=object_syntax_only;missingOptionalUserFileMayBeNormal=true;contentsNotUploaded=true"));
            evidence.Add(new("storage", "software_storage", await ProbeStorageAsync(token).ConfigureAwait(false)));
            evidence.Add(new("limits", "software_files", "fixedInventoryOnly=true;noArbitraryFiles=true;noSettingsModified=true;noRepairPerformed=true;" +
                "validFilesDoNotProveRuntimeHealthy=true;hashMismatchDoesNotProveAntivirusQuarantine=true"));
            return new(TroubleshootingChecks.Files, started, DateTimeOffset.UtcNow, "completed",
                "已核对软件组件、配置文件和保存目录。文件检查不代表画面、声音或控制已正常，继续结合现场情况判断。", evidence.ToArray());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(TroubleshootingChecks.Files, started, DateTimeOffset.UtcNow, "timeout", "文件检查超时，不能认为文件正常。",
                [new("status", "software_files", "inspection_incomplete")]);
        }
    }

    private static async Task<string> HashGroupAsync(string group, string directory, CancellationToken token, bool runtimeCopy = false)
    {
        // Hashes are embedded from the version's resource inventory, never taken from mutable local manifests.
        using Stream resource = typeof(LocalSoftwareFileInspector).Assembly.GetManifestResourceStream("FileInspection." + group)!;
        using JsonDocument manifest = await JsonDocument.ParseAsync(resource, cancellationToken: token).ConfigureAwait(false);
        if (runtimeCopy)
        {
            string identity = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|',
                manifest.RootElement.GetProperty("files").EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal)
                    .Select(p => p.Name + ":" + p.Value.GetString())))));
            directory = Path.Combine(directory, identity);
        }
        List<string> results = [];
        foreach (JsonProperty file in manifest.RootElement.GetProperty("files").EnumerateObject())
        {
            if (file.Name is not ("adb.exe" or "AdbWinApi.dll" or "AdbWinUsbApi.dll" or "scrcpy-server-v4.1" or "openvr_api.dll")) { continue; }
            results.Add((runtimeCopy ? "runtime_" : string.Empty) + file.Name + "=" + await InspectFileAsync(Path.Combine(directory, file.Name), false, file.Value.GetString(), token).ConfigureAwait(false));
        }
        return string.Join(';', results);
    }

    private static void ValidatePath(string path)
    {
        // Reject redirected ancestors as well as the leaf; do not follow junctions into unrelated data.
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) { throw new NotSupportedException(); }
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static async Task<string> InspectFileAsync(string path, bool json, string? expectedHash, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            ValidatePath(path);
            await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            long length = stream.Length;
            if (length > (json ? _maximumJsonBytes : _maximumFileBytes)) { return "size_limit"; }
            if (length == 0) { return "empty"; }
            // Read exactly the bounded snapshot. Concurrent modification is inconclusive, not file corruption.
            byte[] bytes = new byte[(int)length];
            await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
            if (stream.Length != length) { return "changed_during_check"; }
            if (json)
            {
                ReadOnlyMemory<byte> jsonBytes = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? bytes.AsMemory(3) : bytes;
                using JsonDocument document = JsonDocument.Parse(jsonBytes, new JsonDocumentOptions { MaxDepth = 32 });
                if (document.RootElement.ValueKind != JsonValueKind.Object) { return "invalid_json_object"; }
                if (expectedHash is null) { return "json_object_readable"; }
            }
            if (expectedHash is not null)
            { return string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), expectedHash, StringComparison.OrdinalIgnoreCase) ? "hash_valid" : "hash_mismatch"; }
            return "readable";
        }
        catch (FileNotFoundException) { return "missing"; }
        catch (DirectoryNotFoundException) { return "missing"; }
        catch (UnauthorizedAccessException) { return "access_denied"; }
        catch (NotSupportedException) { return "redirected_skipped"; }
        catch (JsonException) { return "invalid_json"; }
        catch (IOException) { return "unreadable_or_busy"; }
    }

    private async Task<string> ProbeStorageAsync(CancellationToken token)
    {
        string writable;
        try
        {
            ValidatePath(_data);
            // Only a fresh diagnostic probe is written; never open a user's existing file for writing.
            string probe = Path.Combine(_data, ".diagnostic-probe-" + Guid.NewGuid().ToString("N"));
            await using FileStream stream = new(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            await stream.WriteAsync(new byte[] { 0 }, token).ConfigureAwait(false);
            await stream.FlushAsync(token).ConfigureAwait(false);
            writable = "probe_succeeded";
        }
        catch (UnauthorizedAccessException) { writable = "access_denied"; }
        catch (NotSupportedException) { writable = "redirected_skipped"; }
        catch (IOException) { writable = "probe_failed"; }
        string space = "unknown";
        try
        {
            ValidatePath(_data);
            DriveInfo drive = new(Path.GetPathRoot(_data)!);
            if (drive.IsReady) { space = drive.AvailableFreeSpace < 256L * 1024 * 1024 ? "low_under_256MiB" : "at_least_256MiB"; }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException) { }
        string version = "unknown";
        try
        {
            string app = Path.Combine(_application, "VRPhoneScreenOverlay.dll");
            ValidatePath(app);
            string? diskVersion = FileVersionInfo.GetVersionInfo(app).ProductVersion;
            if (!string.IsNullOrEmpty(diskVersion) && !string.IsNullOrEmpty(runningVersion))
            { version = diskVersion == runningVersion ? "same" : "different_restart_or_mixed_install_possible"; }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or System.ComponentModel.Win32Exception) { }
        return $"dataDirectoryWrite={writable};freeSpace={space};runningVsDiskVersion={version};existingFileReplacePermissionNotTested=true";
    }
}
