namespace VRPhoneScreenOverlay.SteamVR;

public interface ISavedBindingPreparation
{
    public string ReadRevision();
    public ValueTask<IBindingDefaultsTransaction> PrepareAsync(string revision, CancellationToken token);
}

/// <summary>Prepares an immutable saved-content snapshot without overwriting the user's saved files.</summary>
public sealed class OpenVrSavedBindingPreparation : ISavedBindingPreparation
{
    private readonly string _manifest;
    private readonly string _saved;
    private readonly string _runtime;
    private readonly Func<OpenVrControllerHand> _hand;
    private readonly Func<OpenVrControllerHand, CancellationToken, Task>? _enrich;

    public OpenVrSavedBindingPreparation() : this(
        OpenVrBuiltInBindings.EnsureSourceFiles(OpenVrBuiltInBindings.SourceDirectory),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "steamvr", "input"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRPhoneScreenOverlay", "steamvr"),
        () => OpenVrBindingRecovery.DiagnosticSnapshot.ControllerHand, OpenVrBindingStartup.EnrichLivePreparedAsync)
    { }

    internal OpenVrSavedBindingPreparation(string manifest, string saved, string runtime, Func<OpenVrControllerHand> hand,
        Func<OpenVrControllerHand, CancellationToken, Task>? enrich = null)
    { _manifest = manifest; _saved = saved; _runtime = runtime; _hand = hand; _enrich = enrich; }

    private OpenVrBindingProfileStore Store() => new(_manifest, _saved, _runtime, _hand());
    public string ReadRevision() => Store().GetSavedBindingRevision();

    public async ValueTask<IBindingDefaultsTransaction> PrepareAsync(string revision, CancellationToken token)
    {
        OpenVrBindingProfileStore store = Store();
        if (store.GetSavedBindingRevision() != revision) { throw new InvalidDataException("Saved binding changed during preparation."); }
        string snapshot = Path.Combine(_runtime, ".saved-snapshot-" + Guid.NewGuid().ToString("N"));
        OpenVrBindingDefaults.AssertNoReparsePoints(snapshot);
        Directory.CreateDirectory(snapshot);
        OpenVrBindingDefaults.Transaction? transaction = null;
        try
        {
            IReadOnlyList<OpenVrSelectedBindingProfile> profiles = store.ResolveProfiles();
            if (profiles.Count > 32) { throw new InvalidDataException("Binding profile capacity exceeded."); }
            // Canonical invalid/partial writes must wait; never accept the cache as the newly saved version.
            if (Directory.Exists(_saved))
            {
                foreach (string path in Directory.EnumerateFiles(_saved, OpenVrInputManifest.ApplicationKey + "_*.json"))
                {
                    if (!OpenVrBindingProfileStore.TryReadControllerType(path, out string type) ||
                        !OpenVrBindingProfileStore.IsValidBinding(path, type)) { throw new InvalidDataException("Saved binding is not complete."); }
                }
            }
            foreach (OpenVrSelectedBindingProfile profile in profiles.Where(value => value.Source == OpenVrBindingProfileSource.SavedUser))
            {
                byte[] content = await OpenVrBindingDefaults.ReadBoundedAsync(profile.Path, token).ConfigureAwait(false);
                await File.WriteAllBytesAsync(Path.Combine(snapshot, OpenVrInputManifest.ApplicationKey + "_" + profile.ControllerType + ".json"), content, token).ConfigureAwait(false);
            }
            if (store.GetSavedBindingRevision() != revision) { throw new InvalidDataException("Saved binding changed during preparation."); }
            Dictionary<string, byte[]?> original = new(StringComparer.OrdinalIgnoreCase);
            int backupBytes = 0;
            foreach (string root in new[] { Path.Combine(_runtime, "bindings"), Path.Combine(_runtime, "user-bindings") })
            {
                OpenVrBindingDefaults.AssertNoReparsePoints(root);
                if (Directory.Exists(root))
                {
                    foreach (string path in Directory.EnumerateFiles(root, "*.json")) { await CaptureAsync(path).ConfigureAwait(false); }
                }
                foreach (OpenVrSelectedBindingProfile profile in profiles)
                { await CaptureAsync(Path.Combine(root, profile.ControllerType + ".json")).ConfigureAwait(false); }
            }
            await CaptureAsync(Path.Combine(_runtime, "action_manifest.json")).ConfigureAwait(false);
            transaction = new(original) { SavedRevision = revision };
            OpenVrControllerHand hand = _hand();
            OpenVrPreparedInputManifest prepared = new OpenVrBindingProfileStore(_manifest, snapshot, _runtime, hand).Prepare();
            if (_enrich is not null) { await _enrich(hand, token).ConfigureAwait(false); }
            transaction.RuntimeBindings = prepared.Bindings.Where(value => !original.TryGetValue(value.RuntimePath, out byte[]? before) || before is null ||
                !before.AsSpan().SequenceEqual(File.ReadAllBytes(value.RuntimePath))).ToDictionary(value => value.ControllerType, value => value.RuntimePath, StringComparer.Ordinal);
            return transaction;

            async Task CaptureAsync(string path)
            {
                if (original.ContainsKey(path)) { return; }
                if (original.Count >= 128) { throw new InvalidDataException("Binding snapshot capacity exceeded."); }
                byte[]? bytes = File.Exists(path) ? await OpenVrBindingDefaults.ReadBoundedAsync(path, token).ConfigureAwait(false) : null;
                backupBytes = checked(backupBytes + (bytes?.Length ?? 0));
                if (backupBytes > 16_777_216) { throw new InvalidDataException("Binding snapshot capacity exceeded."); }
                original.Add(path, bytes);
            }
        }
        catch
        {
            if (transaction is not null) { await transaction.DisposeAsync().ConfigureAwait(false); }
            throw;
        }
        finally
        {
            // Cleanup cannot conceal a prepared transaction that still owns rollback.
            try { Directory.Delete(snapshot, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
