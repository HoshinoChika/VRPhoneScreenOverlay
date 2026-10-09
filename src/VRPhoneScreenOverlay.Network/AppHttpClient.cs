using System.Net;
using System.Net.Http.Headers;

namespace VRPhoneScreenOverlay.Network;

/// <summary>
/// The one HTTPS client every outbound feature uses: updates, diagnostics upload, and later
/// sharing signalling.
/// </summary>
/// <remarks>
/// Centralising this is what makes timeout, cancellation, retry policy, HTTPS enforcement and
/// error shape identical across features instead of re-decided per caller.
/// </remarks>
public interface IAppHttpClient : IDisposable
{
    /// <summary>
    /// Sends a request and returns the successful response. The caller owns and disposes it.
    /// </summary>
    /// <param name="createRequest">
    /// Builds the request. Called once per attempt, because an <see cref="HttpRequestMessage"/>
    /// cannot be resent.
    /// </param>
    /// <param name="retryPolicy">Whether this request may run more than once.</param>
    /// <param name="completionOption">When to consider the response complete.</param>
    /// <param name="isSuccess">
    /// Decides which statuses count as success. Defaults to 2xx. Use it when a status such as 404
    /// is a meaningful answer rather than a failure.
    /// </param>
    /// <param name="cancellationToken">Cancels the whole operation, including backoff waits.</param>
    /// <exception cref="NetworkException">The request failed after all permitted attempts.</exception>
    public ValueTask<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> createRequest,
        NetworkRetryPolicy retryPolicy,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseHeadersRead,
        Func<HttpStatusCode, bool>? isSuccess = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IAppHttpClient" />
public sealed class AppHttpClient : IAppHttpClient
{
    // User agent product tokens cannot contain spaces, so this is not AppIdentity.ProductName.
    private const string _userAgentProduct = "VRPhoneScreenOverlay";

    private readonly HttpClient _httpClient;
    private readonly NetworkClientOptions _options;
    private readonly bool _ownsHttpClient;
    private bool _disposed;

    /// <summary>Creates a client configured from <paramref name="options"/>.</summary>
    public AppHttpClient(NetworkClientOptions options)
        : this(options, CreateConfiguredClient(options), ownsHttpClient: true)
    {
    }

    /// <summary>Creates a client over a caller-supplied <see cref="HttpClient"/>, for tests.</summary>
    /// <param name="options">Timeout and retry configuration.</param>
    /// <param name="httpClient">Transport to use.</param>
    /// <param name="ownsHttpClient">Whether disposing this also disposes <paramref name="httpClient"/>.</param>
    public AppHttpClient(NetworkClientOptions options, HttpClient httpClient, bool ownsHttpClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(httpClient);
        options.Validate();
        _options = options;
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
    }

    /// <inheritdoc />
    public async ValueTask<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> createRequest,
        NetworkRetryPolicy retryPolicy,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseHeadersRead,
        Func<HttpStatusCode, bool>? isSuccess = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(createRequest);
        Func<HttpStatusCode, bool> succeeded = isSuccess ?? IsSuccessStatus;
        int maximumAttempts = retryPolicy == NetworkRetryPolicy.Idempotent
            ? _options.MaximumAttempts
            : 1;

        NetworkException? lastFailure = null;
        for (int attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using HttpRequestMessage request = createRequest();
            EnsureHttps(request.RequestUri);

            HttpResponseMessage? response = null;
            try
            {
                response = await _httpClient
                    .SendAsync(request, completionOption, cancellationToken)
                    .ConfigureAwait(false);
                if (succeeded(response.StatusCode))
                {
                    return response;
                }

                lastFailure = new NetworkException(
                    NetworkReasonCodes.HttpStatus,
                    $"服务器返回 {(int)response.StatusCode}",
                    (int)response.StatusCode);
                if (!IsTransientStatus(response.StatusCode) || attempt == maximumAttempts)
                {
                    response.Dispose();
                    response = null;
                    throw lastFailure;
                }

                TimeSpan retryAfter = ReadRetryAfter(response.Headers) ?? BackoffFor(attempt);
                response.Dispose();
                response = null;
                await Task.Delay(retryAfter, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                // HttpClient surfaces its own timeout as a cancellation the caller did not request.
                response?.Dispose();
                lastFailure = new NetworkException(
                    NetworkReasonCodes.Timeout,
                    $"网络请求超过 {_options.RequestTimeout.TotalSeconds:F0} 秒未响应",
                    null,
                    exception);
                if (attempt == maximumAttempts)
                {
                    throw lastFailure;
                }

                await Task.Delay(BackoffFor(attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException exception)
            {
                response?.Dispose();
                lastFailure = new NetworkException(
                    NetworkReasonCodes.Unavailable,
                    "无法连接到服务器，请检查网络",
                    exception.StatusCode is null ? null : (int)exception.StatusCode,
                    exception);
                if (attempt == maximumAttempts)
                {
                    throw lastFailure;
                }

                await Task.Delay(BackoffFor(attempt), cancellationToken).ConfigureAwait(false);
            }
        }

        throw lastFailure ?? new NetworkException(
            NetworkReasonCodes.Unavailable,
            "无法连接到服务器，请检查网络");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private static HttpClient CreateConfiguredClient(NetworkClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        HttpClientHandler? handler = new() { UseProxy = options.UseSystemProxy };
        HttpClient? client = null;
        try
        {
            client = new HttpClient(handler, disposeHandler: true);
            handler = null; // Ownership transferred to HttpClient.
            client.Timeout = options.RequestTimeout;
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(_userAgentProduct, options.ClientName));
            return client;
        }
        catch
        {
            client?.Dispose();
            throw;
        }
        finally { handler?.Dispose(); }
    }

    private static bool IsSuccessStatus(HttpStatusCode status) =>
        (int)status is >= 200 and <= 299;

    private static bool IsTransientStatus(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
        (int)status >= 500;

    private static TimeSpan? ReadRetryAfter(HttpResponseHeaders headers)
    {
        RetryConditionHeaderValue? retryAfter = headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is TimeSpan delta && delta > TimeSpan.Zero)
        {
            return delta > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delta;
        }

        if (retryAfter.Date is DateTimeOffset date)
        {
            TimeSpan wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                return wait > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : wait;
            }
        }

        return null;
    }

    private TimeSpan BackoffFor(int attempt) =>
        _options.ResolvedRetryBaseDelay * Math.Pow(2, attempt - 1);

    private static void EnsureHttps(Uri? requestUri)
    {
        if (requestUri is null || !requestUri.IsAbsoluteUri ||
            !string.Equals(requestUri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            throw new NetworkException(
                NetworkReasonCodes.InsecureScheme,
                "只允许通过 HTTPS 访问网络服务");
        }
    }
}
