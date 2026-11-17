using System.Security.Cryptography;

namespace VRPhoneScreenOverlay.Network;

/// <summary>Per-installation capability, not an account credential or heartbeat identifier.</summary>
public sealed class TranslationInstallationCredential(string directory) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task<string> GetAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "translation-installation.key");
            if (!File.Exists(path))
            {
                string value = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
                string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllTextAsync(temporary, value, token).ConfigureAwait(false);
                    try { File.Move(temporary, path, overwrite: false); }
                    catch (IOException) when (File.Exists(path)) { /* Another process won; use its identity. */ }
                }
                finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
            }
            if (new FileInfo(path).Length > 128) { throw new InvalidDataException("TRANSLATION_IDENTITY_INVALID"); }
            string secret = (await File.ReadAllTextAsync(path, token).ConfigureAwait(false)).Trim();
            if (secret.Length != 64 || !secret.All(char.IsAsciiHexDigit)) { throw new InvalidDataException("TRANSLATION_IDENTITY_INVALID"); }
            return secret;
        }
        finally { _gate.Release(); }
    }
    public void Dispose() => _gate.Dispose();
}
