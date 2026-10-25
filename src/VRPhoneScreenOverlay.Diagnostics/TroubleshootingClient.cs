using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using VRPhoneScreenOverlay.Network;
using VRPhoneScreenOverlay.Protocols;

namespace VRPhoneScreenOverlay.Diagnostics;

public interface ITroubleshootingClient : IDisposable
{
    public Task<TroubleshootingCase[]> HistoryAsync(CancellationToken cancellationToken);
    public Task<TroubleshootingCase> AnalyzeAsync(string requestId, string uploadId, CancellationToken cancellationToken);
    public Task<TroubleshootingCase> BeginExecutionAsync(string id, CancellationToken cancellationToken);
    public Task<TroubleshootingCase> CompleteAsync(string id, TroubleshootingOutcome outcome, CancellationToken cancellationToken);
    public Task<TroubleshootingCase> FeedbackAsync(string id, string feedback, CancellationToken cancellationToken);
    public Task<TroubleshootingCase> CancelAsync(string id, CancellationToken cancellationToken);
}

public interface ILiveTroubleshootingClient
{
    public Task<TroubleshootingAvailability> AvailabilityAsync(CancellationToken token) => Task.FromResult(new TroubleshootingAvailability(true));
    public Task<TroubleshootingCase> BeginInspectionAsync(string id, string uploadId, CancellationToken token);
    public Task<TroubleshootingCase> InspectAsync(string id, TroubleshootingInspectionRequest request, CancellationToken token);
}

public sealed class TroubleshootingClient : ITroubleshootingClient, ILiveTroubleshootingClient
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly Uri _baseUri;
    private readonly TroubleshootingCredential _credential;
    private readonly IAppHttpClient _http;
    private readonly bool _ownsHttp;

    public TroubleshootingClient(Uri diagnosticsInitUri, TroubleshootingCredential credential)
        : this(diagnosticsInitUri, credential, CreateTransport(), ownsHttp: true) { }

    public TroubleshootingClient(Uri diagnosticsInitUri, TroubleshootingCredential credential, IAppHttpClient http, bool ownsHttp = false)
    {
        ArgumentNullException.ThrowIfNull(diagnosticsInitUri);
        if (!diagnosticsInitUri.IsAbsoluteUri || diagnosticsInitUri.Scheme != Uri.UriSchemeHttps || diagnosticsInitUri.UserInfo.Length != 0)
        { throw new ArgumentException("AI_ENDPOINT_INVALID", nameof(diagnosticsInitUri)); }
        _baseUri = new(diagnosticsInitUri, "../troubleshooting/");
        _credential = credential;
        _http = http;
        _ownsHttp = ownsHttp;
    }

    public Task<TroubleshootingCase[]> HistoryAsync(CancellationToken cancellationToken) =>
        SendAsync<TroubleshootingCase[]>(HttpMethod.Get, "history", null, NetworkRetryPolicy.Idempotent, cancellationToken);

    public Task<TroubleshootingAvailability> AvailabilityAsync(CancellationToken token) =>
        SendAsync<TroubleshootingAvailability>(HttpMethod.Get, "availability", null, NetworkRetryPolicy.Idempotent, token);

    public async Task<TroubleshootingCase> BeginInspectionAsync(string id, string uploadId, CancellationToken token)
    {
        ValidateId(id);
        TroubleshootingCase result = await SendAsync<TroubleshootingCase>(HttpMethod.Post, "cases",
            new TroubleshootingCreateRequest(TroubleshootingProtocol.SchemaVersion, id, uploadId) { LiveInspection = true, InspectionVersion = TroubleshootingChecks.CurrentInspectionVersion },
            NetworkRetryPolicy.None, token).ConfigureAwait(false);
        if (result.Id != id || result.UploadId != uploadId || !result.LiveInspection ||
            (result.Plan is not null && !TroubleshootingProtocol.ValidPlan(result.Plan, result.Evidence)))
        { throw new InvalidDataException("AI_RESPONSE_INVALID"); }
        return result;
    }

    public async Task<TroubleshootingCase> InspectAsync(string id, TroubleshootingInspectionRequest request, CancellationToken token)
    {
        TroubleshootingCase result = await PostCaseAsync(id, "inspect", request, NetworkRetryPolicy.None, token).ConfigureAwait(false);
        if (!result.LiveInspection || result.InspectionRound != request.Round ||
            (result.Plan is not null && !TroubleshootingProtocol.ValidPlan(result.Plan, result.Evidence)))
        { throw new InvalidDataException("AI_RESPONSE_INVALID"); }
        return result;
    }

    public async Task<TroubleshootingCase> AnalyzeAsync(string requestId, string uploadId, CancellationToken cancellationToken)
    {
        ValidateId(requestId);
        TroubleshootingCase result = await SendAsync<TroubleshootingCase>(HttpMethod.Post, "cases",
            new TroubleshootingCreateRequest(TroubleshootingProtocol.SchemaVersion, requestId, uploadId), NetworkRetryPolicy.None, cancellationToken)
            .ConfigureAwait(false);
        if (result.Id != requestId || result.UploadId != uploadId ||
            (result.Plan is not null && !TroubleshootingProtocol.ValidPlan(result.Plan, result.Evidence)))
        { throw new InvalidDataException("AI_RESPONSE_INVALID"); }
        return result;
    }

    public Task<TroubleshootingCase> BeginExecutionAsync(string id, CancellationToken cancellationToken) =>
        PostCaseAsync(id, "execute", null, NetworkRetryPolicy.None, cancellationToken);

    public Task<TroubleshootingCase> CompleteAsync(string id, TroubleshootingOutcome outcome, CancellationToken cancellationToken) =>
        PostCaseAsync(id, "outcome", outcome, NetworkRetryPolicy.Idempotent, cancellationToken);

    public Task<TroubleshootingCase> FeedbackAsync(string id, string feedback, CancellationToken cancellationToken)
    {
        if (!TroubleshootingProtocol.ValidFeedback(feedback)) { throw new ArgumentException("AI_FEEDBACK_INVALID", nameof(feedback)); }
        return PostCaseAsync(id, "feedback", new TroubleshootingFeedback(feedback), NetworkRetryPolicy.Idempotent, cancellationToken);
    }

    public Task<TroubleshootingCase> CancelAsync(string id, CancellationToken cancellationToken) =>
        PostCaseAsync(id, "cancel", null, NetworkRetryPolicy.Idempotent, cancellationToken);

    private async Task<TroubleshootingCase> PostCaseAsync(string id, string operation, object? payload,
        NetworkRetryPolicy retry, CancellationToken cancellationToken)
    {
        ValidateId(id);
        TroubleshootingCase result = await SendAsync<TroubleshootingCase>(HttpMethod.Post, "cases/" + id + "/" + operation,
            payload, retry, cancellationToken).ConfigureAwait(false);
        if (result.Id != id) { throw new InvalidDataException("AI_RESPONSE_INVALID"); }
        return result;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body,
        NetworkRetryPolicy retry, CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(path == "cases" || path.EndsWith("/inspect", StringComparison.Ordinal) ? TimeSpan.FromSeconds(TroubleshootingChecks.AgentRequestSeconds) : TimeSpan.FromSeconds(15));
        using HttpResponseMessage response = await _http.SendAsync(() =>
        {
            HttpRequestMessage request = new(method, new Uri(_baseUri, path));
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(_credential.Authorization);
            if (body is not null) { request.Content = JsonContent.Create(body, options: _json); }
            return request;
        }, retry, HttpCompletionOption.ResponseHeadersRead, null, deadline.Token).ConfigureAwait(false);
        return await NetworkResponseBody.ReadJsonAsync<T>(response.Content, _json, TimeSpan.FromSeconds(15), deadline.Token)
            .ConfigureAwait(false) ?? throw new InvalidDataException("AI_RESPONSE_INVALID");
    }

    private static void ValidateId(string id)
    {
        if (!TroubleshootingProtocol.ValidRequestId(id)) { throw new ArgumentException("AI_ID_INVALID", nameof(id)); }
    }

    private static AppHttpClient CreateTransport() => new(new NetworkClientOptions("troubleshooting", TimeSpan.FromSeconds(TroubleshootingChecks.AgentTransportSeconds))
    { UseSystemProxy = false, AllowAutoRedirect = false });

    public void Dispose()
    {
        if (_ownsHttp) { _http.Dispose(); }
    }
}
