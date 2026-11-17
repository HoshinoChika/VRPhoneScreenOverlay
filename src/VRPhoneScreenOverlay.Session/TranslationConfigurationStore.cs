using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Session;

public sealed class TranslationConfigurationStore(string directory) : IDisposable
{
    private readonly SemaphoreSlim _preferencesGate = new(1, 1);
    private int _disposed;
    private sealed class KeyState(string? value, string? failure)
    {
        public string? Value { get; } = value;
        public string? Failure { get; } = failure;
    }
    private KeyState _keyState = new(null, null);
    public bool KeyAvailable { get { var state = Volatile.Read(ref _keyState); return !string.IsNullOrWhiteSpace(state.Value) && state.Failure is null; } }
    public string? KeyFailure => Volatile.Read(ref _keyState).Failure;
    public void ReportKeyFailure(string reason) => ReportKeyHealth(Key, reason);
    public void ReportKeyHealth(string? usedKey, string? failure)
    {
        while (true)
        {
            var state = Volatile.Read(ref _keyState);
            if (!string.Equals(state.Value, usedKey, StringComparison.Ordinal)) { return; }
            var next = new KeyState(state.Value, failure);
            if (ReferenceEquals(Interlocked.CompareExchange(ref _keyState, next, state), state)) { return; }
        }
    }
    private readonly string _directory = Path.GetFullPath(directory);
    private TranslationPreferences _preferences = new();
    public string? Key => Volatile.Read(ref _keyState).Value;
    public TranslationPreferences Preferences => Volatile.Read(ref _preferences);

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        // Restore the selected source even if the independently stored personal key cannot be decrypted.
        string preferencesPath = Path.Combine(_directory, "translation.json");
        if (File.Exists(preferencesPath))
        {
            TranslationPreferences? preferences = JsonSerializer.Deserialize<TranslationPreferences>(await File.ReadAllTextAsync(preferencesPath, cancellationToken).ConfigureAwait(false));
            if (preferences?.IsValid == true) { Volatile.Write(ref _preferences, preferences with { HistoryLimit = 100 }); }
        }
        string keyPath = Path.Combine(_directory, "deepseek.key");
        if (File.Exists(keyPath))
        {
            byte[] encrypted = await File.ReadAllBytesAsync(keyPath, cancellationToken).ConfigureAwait(false);
            byte[] clear = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            try { Volatile.Write(ref _keyState, new(Encoding.UTF8.GetString(clear), null)); }
            finally { CryptographicOperations.ZeroMemory(clear); }
        }
    }

    public async Task SaveKeyAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        byte[] clear = Encoding.UTF8.GetBytes(key.Trim());
        try
        {
            byte[] encrypted = ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser);
            await WriteAsync("deepseek.key", encrypted, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _keyState, new(key.Trim(), null));
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    public void ClearKey()
    {
        File.Delete(Path.Combine(_directory, "deepseek.key"));
        Volatile.Write(ref _keyState, new(null, null));
    }

    public async Task SavePreferencesAsync(TranslationPreferences preferences, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(preferences);
        if (!preferences.IsValid) { throw new ArgumentOutOfRangeException(nameof(preferences)); }
        preferences = preferences with { HistoryLimit = 100 };
        await _preferencesGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteAsync("translation.json", JsonSerializer.SerializeToUtf8Bytes(preferences), cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _preferences, preferences);
        }
        finally { _preferencesGate.Release(); }
    }

    private async Task WriteAsync(string name, byte[] bytes, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        string target = Path.Combine(_directory, name);
        string temporary = target + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
        // Preference I/O does not capture the UI context; finish accepted saves before shutdown.
        _preferencesGate.Wait();
        _preferencesGate.Dispose();
    }
}
