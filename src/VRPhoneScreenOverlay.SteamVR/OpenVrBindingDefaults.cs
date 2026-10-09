using System.Text.Json;

namespace VRPhoneScreenOverlay.SteamVR;

public interface IBindingDefaultsTransaction : IAsyncDisposable
{
    public IReadOnlyDictionary<string, string> RuntimeBindings { get; }
    public string? SavedRevision => null;
    public void Commit();
}

public interface IBindingDefaultsRestorer
{
    public ValueTask<IBindingDefaultsTransaction> BeginResetAsync(CancellationToken cancellationToken);
    public ValueTask<IBindingDefaultsTransaction> BeginResetAsync(IReadOnlyList<string> registeredTypes, CancellationToken cancellationToken)
        => BeginResetAsync(cancellationToken);
    public ValueTask<IBindingDefaultsTransaction> BeginResetAsync(IReadOnlyList<string> registeredTypes, IReadOnlyList<string> genericTypes,
        CancellationToken cancellationToken) => BeginResetAsync(registeredTypes, cancellationToken);
}

/// <summary>Resets app-owned binding files, with a bounded rollback snapshot until activation succeeds.</summary>
public sealed class OpenVrBindingDefaults(string packagedManifest, string savedDirectory, string runtimeDirectory,
    OpenVrControllerHand? preparedHand = null) : IBindingDefaultsRestorer
{
    private const int _maximumFiles = 128;
    private const int _maximumFileBytes = 1_048_576;
    private const int _maximumBackupBytes = 16_777_216;
    private bool UseBuiltIn { get; init; }
    private bool EnrichFromDriver { get; init; }

    public static OpenVrBindingDefaults CreateDefault() => CreateBuiltIn(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "steamvr", "input"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRPhoneScreenOverlay", "steamvr"), enrichFromDriver: true);

    internal static OpenVrBindingDefaults CreateBuiltIn(string savedDirectory, string runtimeDirectory, OpenVrControllerHand? preparedHand = null, bool enrichFromDriver = false) =>
        new(Path.Combine(runtimeDirectory, "default-source", "action_manifest.json"), savedDirectory, runtimeDirectory, preparedHand)
        { UseBuiltIn = true, EnrichFromDriver = enrichFromDriver };

    public ValueTask<IBindingDefaultsTransaction> BeginResetAsync(CancellationToken cancellationToken)
        => BeginResetAsync([], cancellationToken);

    public ValueTask<IBindingDefaultsTransaction> BeginResetAsync(IReadOnlyList<string> registeredTypes, CancellationToken cancellationToken)
        => BeginResetAsync(registeredTypes, [], cancellationToken);

    public async ValueTask<IBindingDefaultsTransaction> BeginResetAsync(IReadOnlyList<string> registeredTypes, IReadOnlyList<string> genericTypes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (UseBuiltIn) { OpenVrBuiltInBindings.EnsureSourceFiles(Path.GetDirectoryName(packagedManifest)!); }
        string packageRoot = Path.GetDirectoryName(Path.GetFullPath(packagedManifest))!;
        string savedRoot = Path.GetFullPath(savedDirectory);
        string runtimeRoot = Path.GetFullPath(runtimeDirectory);
        string cachedRoot = Path.Combine(runtimeRoot, "user-bindings");
        string bindingsRoot = Path.Combine(runtimeRoot, "bindings");
        Dictionary<string, byte[]> defaults = new(StringComparer.Ordinal);
        using (JsonDocument manifest = ParseFile(await ReadBoundedAsync(packagedManifest, cancellationToken).ConfigureAwait(false)))
        {
            foreach (JsonElement entry in manifest.RootElement.GetProperty("default_bindings").EnumerateArray())
            {
                string type = entry.GetProperty("controller_type").GetString()!;
                if (string.IsNullOrEmpty(type) || type.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-') || defaults.Count >= 32)
                { throw new InvalidDataException("Invalid default binding type."); }
                string source = ContainedPath(packageRoot, entry.GetProperty("binding_url").GetString()!);
                byte[] contents = await ReadBoundedAsync(source, cancellationToken).ConfigureAwait(false);
                using JsonDocument binding = ParseFile(contents);
                if (!IsOwned(binding.RootElement) || binding.RootElement.GetProperty("controller_type").GetString() != type ||
                    !OpenVrBindingProfileStore.IsValidBinding(source, type))
                { throw new InvalidDataException("Invalid packaged default binding."); }
                defaults.Add(type, contents);
            }
        }
        if (defaults.Count == 0) { throw new InvalidDataException("Default bindings are missing."); }
        foreach (string type in registeredTypes) { AddGenericDefault(type); }
        foreach (string type in genericTypes) { AddGenericDefault(type, replace: true); }

        Dictionary<string, byte[]?> changes = new(StringComparer.OrdinalIgnoreCase);
        foreach (string root in new[] { savedRoot, cachedRoot, bindingsRoot })
        {
            AssertNoReparsePoints(root);
            if (!Directory.Exists(root)) { continue; }
            foreach (string path in Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Canonical names are app-owned even when a config is damaged.
                bool canonical = defaults.Keys.Any(type => Path.GetFileName(path) ==
                    (root == savedRoot ? OpenVrInputManifest.ApplicationKey + "_" : "") + type + ".json");
                if (!canonical && new FileInfo(path).Length > _maximumFileBytes) { continue; }
                byte[] contents = await ReadBoundedAsync(path, cancellationToken).ConfigureAwait(false);
                bool owned = false;
                string? ownedType = null;
                try
                {
                    using JsonDocument existing = ParseFile(contents);
                    owned = IsOwned(existing.RootElement);
                    if (owned) { ownedType = existing.RootElement.GetProperty("controller_type").GetString(); }
                    if (canonical && existing.RootElement.ValueKind == JsonValueKind.Object &&
                        existing.RootElement.TryGetProperty("app_key", out JsonElement key) && key.ValueKind == JsonValueKind.String &&
                        key.GetString() != OpenVrInputManifest.ApplicationKey)
                    { throw new InvalidDataException("Canonical binding belongs to another application."); }
                }
                catch (JsonException) { if (!canonical) { continue; } }
                if (owned || canonical) { AddChange(path, null); }
                if (owned) { AddGenericDefault(ownedType!); }
            }
        }
        foreach ((string type, byte[] contents) in defaults)
        {
            AddChange(ContainedPath(savedRoot, OpenVrInputManifest.ApplicationKey + "_" + type + ".json"), contents);
            AddChange(ContainedPath(cachedRoot, type + ".json"), contents);
            // Native activation regenerates these files for the selected hand.
            AddChange(ContainedPath(bindingsRoot, type + ".json"), contents);
        }
        AddChange(ContainedPath(runtimeRoot, "action_manifest.json"), null);
        Dictionary<string, byte[]?> original = new(StringComparer.OrdinalIgnoreCase);
        int backupBytes = 0;
        foreach (string path in changes.Keys)
        {
            byte[]? before = File.Exists(path) ? await ReadBoundedAsync(path, cancellationToken).ConfigureAwait(false) : null;
            backupBytes = checked(backupBytes + (before?.Length ?? 0));
            if (backupBytes > _maximumBackupBytes) { throw new InvalidDataException("Binding rollback capacity exceeded."); }
            original.Add(path, before);
        }
        Transaction transaction = new(original);
        try
        {
            foreach ((string path, byte[]? contents) in changes)
            { await WriteAsync(path, contents, cancellationToken).ConfigureAwait(false); }
            // Reuse the existing transformations, without shutting down OpenVR.
            // Preserve the runtime's basis hand: the current dominant-hand flip
            // remains active. Rebuilding for the UI hand here would flip twice.
            OpenVrBindingPreparationSnapshot basis = OpenVrBindingRecovery.DiagnosticSnapshot;
            OpenVrControllerHand hand = preparedHand ?? (basis.Prepared ? basis.ControllerHand : OpenVrControllerPreferences.Load());
            OpenVrBindingProfileStore store = new(packagedManifest, savedRoot, runtimeRoot, hand);
            transaction.SavedRevision = store.GetSavedBindingRevision();
            OpenVrPreparedInputManifest prepared = store.Prepare();
            if (store.GetSavedBindingRevision() != transaction.SavedRevision) { throw new InvalidDataException("Saved binding changed during preparation."); }
            if (EnrichFromDriver) { await OpenVrBindingStartup.EnrichLivePreparedAsync(hand, cancellationToken).ConfigureAwait(false); }
            transaction.RuntimeBindings = prepared.Bindings.ToDictionary(binding => binding.ControllerType, binding => binding.RuntimePath, StringComparer.Ordinal);
            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        void AddChange(string path, byte[]? contents)
        {
            AssertNoReparsePoints(path);
            if (!changes.ContainsKey(path) && changes.Count >= _maximumFiles)
            { throw new InvalidDataException("Binding file capacity exceeded."); }
            changes[path] = contents;
        }

        void AddGenericDefault(string type, bool replace = false)
        {
            if (!OpenVrGenericBinding.ValidType(type)) { throw new InvalidDataException("Invalid controller type."); }
            bool exists = defaults.ContainsKey(type);
            if (exists && !replace) { return; }
            if (!exists && defaults.Count >= 32) { throw new InvalidDataException("Default binding type capacity exceeded."); }
            defaults[type] = System.Text.Encoding.UTF8.GetBytes(OpenVrGenericBinding.Create(type));
        }
    }

    private static bool IsOwned(JsonElement root) => root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("app_key", out JsonElement key) && key.ValueKind == JsonValueKind.String && key.GetString() == OpenVrInputManifest.ApplicationKey &&
        root.TryGetProperty("controller_type", out JsonElement type) && type.ValueKind == JsonValueKind.String;

    private static JsonDocument ParseFile(byte[] contents)
    {
        // Decode the bounded memory snapshot with the same BOM handling as the
        // original binding reader. This performs no filesystem I/O.
        using MemoryStream memory = new(contents, writable: false);
        using StreamReader text = new(memory, detectEncodingFromByteOrderMarks: true);
        return JsonDocument.Parse(text.ReadToEnd());
    }

    private static string ContainedPath(string root, string relative)
    {
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) { throw new InvalidDataException("Binding path escaped its root."); }
        return path;
    }

    internal static void AssertNoReparsePoints(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            { throw new InvalidDataException("Binding path contains a reparse point."); }
        }
    }

    internal static async Task<byte[]> ReadBoundedAsync(string path, CancellationToken token)
    {
        AssertNoReparsePoints(path);
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (stream.Length > _maximumFileBytes) { throw new InvalidDataException("Binding file capacity exceeded."); }
        byte[] contents = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(contents, token).ConfigureAwait(false);
        return contents;
    }

    private static async Task WriteAsync(string path, byte[]? contents, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        AssertNoReparsePoints(path);
        if (contents is null) { File.Delete(path); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".defaults-new";
        AssertNoReparsePoints(temporary);
        try
        {
            await File.WriteAllBytesAsync(temporary, contents, token).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    internal sealed class Transaction(Dictionary<string, byte[]?> original) : IBindingDefaultsTransaction
    {
        public IReadOnlyDictionary<string, string> RuntimeBindings { get; set; } = new Dictionary<string, string>();
        public string? SavedRevision { get; set; }
        private bool _committed;
        private bool _disposed;
        public void Commit() { ObjectDisposedException.ThrowIf(_disposed, this); _committed = true; }
        public async ValueTask DisposeAsync()
        {
            if (_disposed) { return; }
            _disposed = true;
            if (!_committed)
            {
                List<Exception> errors = [];
                foreach ((string path, byte[]? contents) in original)
                {
                    try
                    {
                        if (contents is null && !File.Exists(path)) { continue; }
                        if (contents is not null && File.Exists(path) &&
                            (await ReadBoundedAsync(path, CancellationToken.None).ConfigureAwait(false)).AsSpan().SequenceEqual(contents)) { continue; }
                        await WriteAsync(path, contents, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException) { errors.Add(exception); }
                }
                if (errors.Count > 0) { throw new AggregateException("Binding rollback failed.", errors); }
            }
            original.Clear();
        }
    }
}
