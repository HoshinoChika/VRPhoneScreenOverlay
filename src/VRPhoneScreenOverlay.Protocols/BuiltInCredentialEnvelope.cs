using System.Security.Cryptography;
using System.Text.Json;

namespace VRPhoneScreenOverlay.Protocols;

public sealed record BuiltInCredentialRequest(int Version, string PublicKey, string Nonce);
public sealed record BuiltInCredentialResponse(int Version, byte[] EncryptedPayload);

/// <summary>Transport envelope; expiry is not enforced by the model provider.</summary>
public static class BuiltInCredentialEnvelope
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    public static bool IsKeyFormatValid(string? key) => key is { Length: >= 16 and <= 128 } &&
        key.StartsWith("sk-", StringComparison.Ordinal) && key.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    public static BuiltInCredentialResponse Seal(BuiltInCredentialRequest request, string key, DateTimeOffset now)
    {
        if (request.Version != 1 || request.PublicKey is null || request.PublicKey.Length > 1024 || request.Nonce is null || request.Nonce.Length != 44 ||
            !IsKeyFormatValid(key))
        { throw new InvalidDataException("CREDENTIAL_REQUEST_INVALID"); }
        if (Convert.FromBase64String(request.Nonce).Length != 32) { throw new InvalidDataException("CREDENTIAL_REQUEST_INVALID"); }
        byte[] encoded = Convert.FromBase64String(request.PublicKey);
        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(encoded, out int consumed);
        if (consumed != encoded.Length || rsa.KeySize != 3072) { throw new InvalidDataException("CREDENTIAL_REQUEST_INVALID"); }
        byte[] clear = JsonSerializer.SerializeToUtf8Bytes(new Payload(1, request.Nonce, now.AddMinutes(2).ToUnixTimeSeconds(), key), _json);
        try { return new(1, rsa.Encrypt(clear, RSAEncryptionPadding.OaepSHA256)); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    public static string Open(BuiltInCredentialResponse response, RSA rsa, string nonce, DateTimeOffset now)
    {
        if (response.Version != 1 || response.EncryptedPayload is null || response.EncryptedPayload.Length != 384)
        { throw new InvalidDataException("TRANSLATION_BUILTIN_UNAVAILABLE"); }
        byte[] clear = rsa.Decrypt(response.EncryptedPayload, RSAEncryptionPadding.OaepSHA256);
        try
        {
            Payload? value = JsonSerializer.Deserialize<Payload>(clear, _json);
            long time = now.ToUnixTimeSeconds();
            if (value is null || value.Version != 1 || value.Nonce != nonce || value.ExpiresAt <= time || value.ExpiresAt > time + 180 ||
                !IsKeyFormatValid(value.Key)) { throw new InvalidDataException("TRANSLATION_BUILTIN_UNAVAILABLE"); }
            return value.Key;
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    private sealed record Payload(int Version, string Nonce, long ExpiresAt, string Key);
}
