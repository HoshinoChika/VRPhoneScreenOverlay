using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Network;

public sealed class DeepSeekTranslationService(IIncidentRecorder? incident = null) : IDisposable
{
    // DeepSeek must remain reachable when the user's local proxy is stopped.
    // Disable both system and environment-derived proxies for this client only.
    internal static SocketsHttpHandler CreateHandler() => new()
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
    };
    private readonly HttpClient _http = new(CreateHandler()) { Timeout = TimeSpan.FromSeconds(60) };
    private readonly HttpClient _cloudHttp = new(CreateHandler()) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly object _sync = new();
    private CloudTranslationClient? _builtIn;
    private readonly TranslationInstallationCredential _identity = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRPhoneScreenOverlay"));
    private bool _disposed;

    public ITranslationModelClient CreateClient(Func<string?> getKey, Action<string, string?>? reportKeyHealth = null) =>
        new DeepSeekTranslationClient(_http, getKey, incident, reportKeyHealth);
    public ITranslationModelClient CreateBuiltInClient(ClientServiceConfiguration configuration)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _builtIn ??= new CloudTranslationClient(_cloudHttp, configuration, _identity.GetAsync);
        }
    }

    public async Task ValidateAsync(string key, TranslationFrame probe, CancellationToken cancellationToken)
    {
        if (!DeepSeekTranslationClient.IsKeyFormatValid(key)) { throw new InvalidDataException("TRANSLATION_KEY_INVALID"); }
        TranslationRecord result = await new DeepSeekTranslationClient(_http, () => key, incident).ProbeAsync(probe, cancellationToken);
        if (!MatchesProbeText(result.Text)) { throw new InvalidDataException("TRANSLATION_VISION_PROBE_FAILED"); }
    }

    // Model formatting is not authentication: tolerate spacing/full-width digits,
    // but reject extra or different digits rather than accepting a substring.
    internal static bool MatchesProbeText(string text) =>
        string.Concat(text.Normalize(System.Text.NormalizationForm.FormKC).Where(char.IsAsciiDigit)) == "4729";

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) { return; }
            _disposed = true;
            _identity.Dispose();
            _builtIn = null;
        }
        _http.Dispose();
        _cloudHttp.Dispose();
    }
}
