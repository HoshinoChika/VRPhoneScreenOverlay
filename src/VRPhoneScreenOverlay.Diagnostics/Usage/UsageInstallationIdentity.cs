namespace VRPhoneScreenOverlay.Diagnostics.Usage;

/// <summary>Random per-Windows-profile identity, outside the portable installation and diagnostics.</summary>
public static class UsageInstallationIdentity
{
    public static async Task<string> LoadAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (!File.Exists(path))
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, Guid.NewGuid().ToString("N"), cancellationToken)
                    .ConfigureAwait(false);
                try { File.Move(temporary, path, overwrite: false); }
                catch (IOException) when (File.Exists(path)) { } // Another process won publication.
            }
            finally { File.Delete(temporary); }
        }
        // Invalid/unreadable identities disable this session instead of inflating totals with a new ID.
        if (new FileInfo(path).Length > 64) { throw new InvalidDataException("Invalid usage identity."); }
        string value = (await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)).Trim();
        if (!Guid.TryParseExact(value, "N", out Guid identity) || identity == Guid.Empty)
        { throw new InvalidDataException("Invalid usage identity."); }
        return identity.ToString("N");
    }
}
