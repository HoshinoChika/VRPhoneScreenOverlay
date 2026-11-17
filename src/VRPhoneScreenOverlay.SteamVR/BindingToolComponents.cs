using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.SteamVR;

public sealed class BindingToolComponentException(string reason, Exception? inner = null)
    : InvalidOperationException("软件绑定辅助组件损坏或无法启动，请从官网下载完整安装包修复。", inner)
{
    public string ReasonCode { get; } = reason;
}

/// <summary>Fixed helper group, trusted by an archive embedded in the running application.</summary>
public sealed class BindingToolComponents(string directory, string version, Func<Stream?> openArchive, string evidenceRoot)
{
    internal static readonly string[] FileNames = ["VRPhoneScreenOverlay.SteamVR.BindingTool.exe",
        "VRPhoneScreenOverlay.SteamVR.BindingTool.dll", "VRPhoneScreenOverlay.SteamVR.BindingTool.deps.json",
        "VRPhoneScreenOverlay.SteamVR.BindingTool.runtimeconfig.json"];
    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static BindingToolComponents Current(string? evidenceRoot = null) => new(AppContext.BaseDirectory,
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
        () => Assembly.GetEntryAssembly()?.GetManifestResourceStream("BindingToolRecovery.zip"),
        evidenceRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppIdentity.LocalDataFolderName,
            "diagnostics", "component-recovery"));

    public async Task EnsureAsync(CancellationToken token)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        ComponentFileEvidence[] files = [];
        string phase = "validation";
        try
        {
            AssertPlainPath(directory);
            using Stream source = openArchive() ?? throw new BindingToolComponentException("BINDING_COMPONENT_RECOVERY_UNAVAILABLE");
            using ZipArchive archive = new(source, ZipArchiveMode.Read);
            if (archive.Entries.Count != 5 || archive.Entries.Any(e => e.Length > 8 * 1024 * 1024))
            { throw new InvalidDataException("Invalid recovery inventory."); }
            ZipArchiveEntry manifest = archive.GetEntry("inventory.json") ?? throw new InvalidDataException("Missing inventory.");
            await using Stream manifestStream = await manifest.OpenAsync(deadline.Token).ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(manifestStream, cancellationToken: deadline.Token).ConfigureAwait(false);
            JsonElement root = document.RootElement;
            if (root.GetProperty("version").GetString() != version) { throw new BindingToolComponentException("BINDING_COMPONENT_VERSION_MISMATCH"); }
            Dictionary<string, byte[]> trusted = new(StringComparer.Ordinal);
            foreach (string name in FileNames)
            {
                ZipArchiveEntry entry = archive.GetEntry(name) ?? throw new InvalidDataException("Missing component.");
                await using Stream input = await entry.OpenAsync(deadline.Token).ConfigureAwait(false);
                using MemoryStream bytes = new();
                await input.CopyToAsync(bytes, deadline.Token).ConfigureAwait(false);
                byte[] content = bytes.ToArray();
                if (content.Length == 0 || Convert.ToHexStringLower(SHA256.HashData(content)) != root.GetProperty("files").GetProperty(name).GetString())
                { throw new InvalidDataException("Invalid recovery bytes."); }
                trusted.Add(name, content);
            }
            files = await InspectAsync(trusted, deadline.Token).ConfigureAwait(false);
            if (files.All(f => f.Status == "valid")) { return; }
            phase = "recovery";
            // The lease serializes this app's utility callers. No helper is launched until every member verifies.
            await using FileStream lease = await LeaseAsync(deadline.Token).ConfigureAwait(false);
            files = await InspectAsync(trusted, deadline.Token).ConfigureAwait(false);
            if (files.All(f => f.Status == "valid")) { return; }
            string marker = Path.Combine(directory, ".binding-repair-attempt");
            AssertPlainPath(marker);
            using Process current = Process.GetCurrentProcess();
            string owner = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
                current.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (File.Exists(marker) && await File.ReadAllTextAsync(marker, deadline.Token).ConfigureAwait(false) == owner)
            { throw new BindingToolComponentException("BINDING_COMPONENT_RECOVERY_FAILED"); }
            await File.WriteAllTextAsync(marker, owner, deadline.Token).ConfigureAwait(false);
            await SaveAsync("repairing", files, null, deadline.Token).ConfigureAwait(false);
            foreach (ComponentFileEvidence damaged in files.Where(f => f.Status != "valid"))
            {
                string destination = Path.Combine(directory, damaged.File), temporary = destination + ".repair-new";
                AssertPlainPath(destination); AssertPlainPath(temporary);
                await using (FileStream output = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                    8192, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await output.WriteAsync(trusted[damaged.File], deadline.Token).ConfigureAwait(false);
                    await output.FlushAsync(deadline.Token).ConfigureAwait(false);
                }
                File.Move(temporary, destination, overwrite: true);
            }
            files = await InspectAsync(trusted, deadline.Token).ConfigureAwait(false);
            if (files.Any(f => f.Status != "valid")) { throw new IOException("Repair verification failed."); }
            await SaveAsync("recovered", files, null, deadline.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or OperationCanceledException or KeyNotFoundException)
        {
            try { using CancellationTokenSource logDeadline = new(TimeSpan.FromSeconds(2)); await SaveAsync(phase + "-failed", files, error.GetType().Name, logDeadline.Token).ConfigureAwait(false); }
            catch (Exception logging) when (logging is IOException or UnauthorizedAccessException or OperationCanceledException) { }
            if (token.IsCancellationRequested) { throw; }
            throw error as BindingToolComponentException ?? new BindingToolComponentException("BINDING_COMPONENT_RECOVERY_FAILED", error);
        }
    }

    private async Task<ComponentFileEvidence[]> InspectAsync(Dictionary<string, byte[]> trusted, CancellationToken token)
    {
        List<ComponentFileEvidence> result = [];
        foreach ((string name, byte[] content) in trusted)
        {
            string path = Path.Combine(directory, name);
            AssertPlainPath(path);
            string expected = Convert.ToHexStringLower(SHA256.HashData(content));
            if (!File.Exists(path)) { result.Add(new(name, "missing", null, expected, null)); continue; }
            await using FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            string actual = input.Length <= 8 * 1024 * 1024 ? Convert.ToHexStringLower(await SHA256.HashDataAsync(input, token).ConfigureAwait(false)) : "size-limit";
            result.Add(new(name, actual == expected ? "valid" : "hash-mismatch", input.Length, expected, actual));
        }
        return result.ToArray();
    }

    private async Task<FileStream> LeaseAsync(CancellationToken token)
    {
        string path = Path.Combine(directory, ".binding-repair.lock");
        AssertPlainPath(path);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(100, token).ConfigureAwait(false); }
        }
    }

    private async Task SaveAsync(string phase, ComponentFileEvidence[] files, string? error, CancellationToken token)
    {
        AssertPlainPath(evidenceRoot);
        Directory.CreateDirectory(evidenceRoot);
        string path = Path.Combine(evidenceRoot, "binding-tool.json");
        AssertPlainPath(path);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var record = new ComponentRecoveryEvidence(1, "binding-tool", version, "binding-tool", DateTimeOffset.UtcNow,
            phase, phase == "recovered" ? "BINDING_COMPONENT_RECOVERED" : phase == "repairing" ? "BINDING_COMPONENT_RECOVERY_STARTED" : "BINDING_COMPONENT_RECOVERY_FAILED", files, error);
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(record, _json), token).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally { try { File.Delete(temporary); } catch (IOException) { } }
    }
    private static void AssertPlainPath(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            { throw new IOException("Redirected component path."); }
        }
    }
}
