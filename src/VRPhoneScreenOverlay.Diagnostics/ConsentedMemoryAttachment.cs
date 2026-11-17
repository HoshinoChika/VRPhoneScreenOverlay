using System.Security.Cryptography;
using VRPhoneScreenOverlay.Protocols;

namespace VRPhoneScreenOverlay.Diagnostics;

/// <summary>Per-upload approval bound to the exact bytes of an existing private export.</summary>
public sealed class ConsentedMemoryAttachment
{
    private ConsentedMemoryAttachment(string path, long bytes, string sha256, DateTimeOffset? windowEnd)
    { Path = path; Bytes = bytes; Sha256 = sha256; ConsentedAt = DateTimeOffset.UtcNow; WindowEnd = windowEnd; }

    internal string Path { get; }
    public long Bytes { get; }
    public string Sha256 { get; }
    public DateTimeOffset ConsentedAt { get; }
    public DateTimeOffset? WindowEnd { get; }

    internal static async Task<ConsentedMemoryAttachment> CreateAsync(string path, CancellationToken token, DateTimeOffset? windowEnd = null)
    {
        IncidentStorage.ValidateDirectory(System.IO.Path.GetDirectoryName(path)!);
        FileInfo file = new(path);
        if (!file.Exists || file.Length is <= 0 or > DiagnosticTransferLimits.MaximumMemoryAttachmentBytes || (file.Attributes & FileAttributes.ReparsePoint) != 0)
        { throw new IOException("Invalid memory attachment."); }
        await using FileStream input = file.OpenRead();
        string hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(input, token).ConfigureAwait(false));
        return new(file.FullName, input.Length, hash, windowEnd);
    }
}
