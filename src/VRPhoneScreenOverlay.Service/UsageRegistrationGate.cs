using System.Security.Cryptography;
using System.Text;

namespace VRPhoneScreenOverlay.Service;

/// <summary>Admission only: existing identities bypass this gate. Caller serializes access.</summary>
internal sealed class UsageRegistrationGate(TimeProvider time)
{
    private const int _maximumAddresses = 4096;
    private const int _perAddress = 120;
    private const int _global = 6000;
    private readonly Dictionary<string, int> _addresses = new(StringComparer.Ordinal);
    private DateTimeOffset _window = time.GetUtcNow();
    private int _total;

    public bool TryRegister(string address)
    {
        if (address.Length > 256) { return false; }
        DateTimeOffset now = time.GetUtcNow();
        if (now >= _window.AddHours(1)) { _addresses.Clear(); _total = 0; _window = now; }
        if (_total >= _global) { return false; }
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(address)));
        _addresses.TryGetValue(key, out int count);
        if (count >= _perAddress || (count == 0 && _addresses.Count >= _maximumAddresses)) { return false; }
        _addresses[key] = count + 1;
        _total++;
        return true;
    }
}
