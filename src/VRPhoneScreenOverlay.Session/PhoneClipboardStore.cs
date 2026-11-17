using System.Text.RegularExpressions;
using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Session;

/// <summary>Session-only, capacity 50, oldest evicted. Never writes clipboard text to disk or logs.</summary>
public sealed partial class PhoneClipboardStore : IPhoneClipboardSink, IDisposable
{
    private readonly object _gate = new();
    private readonly List<PhoneClipboardEntry> _entries = [];
    private long _revision;
    private bool _connected;
    private bool _disposed;
    public long EvictedEntries { get; private set; }
    public (long Revision, bool Connected, PhoneClipboardEntry[] Entries) Snapshot
    {
        get { lock (_gate) { return (_revision, _connected, _entries.ToArray()); } }
    }
    public void SetConnection(bool connected)
    {
        lock (_gate) { if (_connected != connected) { _connected = connected; _revision++; } }
    }
    public void Receive(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 65536) { return; }
        lock (_gate)
        {
            if (_disposed) { return; }
            int old = _entries.FindIndex(item => item.Text == text);
            if (old >= 0) { _entries.RemoveAt(old); }
            _entries.Insert(0, new(Guid.NewGuid(), text, FindUrl(text), DateTimeOffset.Now));
            if (_entries.Count > 50) { _entries.RemoveAt(50); EvictedEntries++; }
            _revision++;
        }
    }
    public void Update(Guid id, string status, bool busy)
    {
        lock (_gate)
        {
            int index = _entries.FindIndex(item => item.Id == id);
            if (index < 0) { return; }
            _entries[index] = _entries[index] with { Status = status, Busy = busy }; _revision++;
        }
    }
    public static Uri? FindUrl(string text)
    {
        foreach (Match match in UrlPattern().Matches(text))
        {
            string candidate = match.Value.TrimEnd('。', '，', '！', '？', ')', '）', ']', '】', '>', '》', ';', '；');
            if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https" &&
                !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo)) { return uri; }
        }
        return null;
    }
    [GeneratedRegex("https?://[^\\s<>\\\"'“”]+", RegexOptions.IgnoreCase, 100)]
    private static partial Regex UrlPattern();
    public void Dispose()
    {
        lock (_gate) { _disposed = true; _entries.Clear(); _connected = false; _revision++; }
    }
}
