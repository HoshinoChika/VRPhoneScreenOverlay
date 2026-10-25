using System.Security.Cryptography;

namespace VRPhoneScreenOverlay.Diagnostics;

/// <summary>Private bearer credential stored outside the portable installation; never include it in diagnostics.</summary>
public sealed class TroubleshootingCredential
{
    private readonly string _secret;

    private TroubleshootingCredential(string secret) => _secret = secret;

    internal string Authorization => "Bearer " + _secret;
    public string StorageId => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("VRPSO-TROUBLESHOOTING-V1:" + _secret)));

    public static async Task<TroubleshootingCredential?> TryLoadDefaultAsync(CancellationToken cancellationToken)
    {
        try { return await LoadAsync(DefaultPath, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "VRPhoneScreenOverlay", "identity", "troubleshooting.key");

    public static async Task<TroubleshootingCredential> LoadAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        if (!File.Exists(fullPath))
        {
            string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), cancellationToken)
                    .ConfigureAwait(false);
                try { File.Move(temporary, fullPath, overwrite: false); }
                catch (IOException) when (File.Exists(fullPath)) { } // Concurrent startup published the credential.
            }
            finally { File.Delete(temporary); }
        }
        FileInfo file = new(fullPath);
        if (file.Length != 64 || (file.Attributes & FileAttributes.ReparsePoint) != 0)
        { throw new InvalidDataException("TROUBLESHOOTING_IDENTITY_INVALID"); }
        string secret = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
        if (secret.Length != 64 || !secret.All(char.IsAsciiHexDigit))
        { throw new InvalidDataException("TROUBLESHOOTING_IDENTITY_INVALID"); }
        return new(secret.ToLowerInvariant());
    }

    public override string ToString() => "TroubleshootingCredential [private]";
}
