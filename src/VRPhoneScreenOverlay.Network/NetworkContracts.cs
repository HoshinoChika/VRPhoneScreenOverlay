using VRPhoneScreenOverlay.Contracts;

namespace VRPhoneScreenOverlay.Network;

/// <summary>Reason codes raised by the shared network layer.</summary>
public static class NetworkReasonCodes
{
    /// <summary>The request URI was not absolute HTTPS.</summary>
    public const string InsecureScheme = "NETWORK_INSECURE_SCHEME";

    /// <summary>The request timed out, including after any permitted retries.</summary>
    public const string Timeout = "NETWORK_TIMEOUT";

    /// <summary>The host could not be reached, including after any permitted retries.</summary>
    public const string Unavailable = "NETWORK_UNAVAILABLE";

    /// <summary>The server answered with a status the caller treats as failure.</summary>
    public const string HttpStatus = "NETWORK_HTTP_STATUS";

    /// <summary>The small JSON response exceeded its byte budget.</summary>
    public const string ResponseTooLarge = "NETWORK_RESPONSE_TOO_LARGE";

    /// <summary>The response body was missing or could not be parsed.</summary>
    public const string ResponseInvalid = "NETWORK_RESPONSE_INVALID";
}

/// <summary>
/// Whether a request may be retried automatically.
/// </summary>
/// <remarks>
/// Retrying is a correctness decision, not a reliability knob. A request that streams a body or
/// creates a server-side resource must run at most once, otherwise a retry uploads a duplicate or
/// resends a consumed stream. Callers state the guarantee; the client never guesses.
/// </remarks>
public enum NetworkRetryPolicy
{
    /// <summary>Send exactly once. Correct for uploads and any non-idempotent request.</summary>
    None,

    /// <summary>Safe to repeat. Transient failures are retried with backoff.</summary>
    Idempotent,
}

/// <summary>How the shared client is configured for one caller.</summary>
/// <param name="ClientName">User agent component, e.g. <c>update-client</c>.</param>
/// <param name="RequestTimeout">Per-attempt timeout.</param>
/// <param name="MaximumAttempts">Attempts for idempotent requests. One disables retrying.</param>
/// <param name="RetryBaseDelay">First backoff delay; doubles per attempt.</param>
public sealed record NetworkClientOptions(
    string ClientName,
    TimeSpan RequestTimeout,
    int MaximumAttempts = 3,
    TimeSpan? RetryBaseDelay = null)
{
    /// <summary>Use the operating-system proxy. First-party service clients use direct HTTPS.</summary>
    public bool UseSystemProxy { get; init; } = true;

    /// <summary>Backoff delay before the second attempt.</summary>
    public TimeSpan ResolvedRetryBaseDelay => RetryBaseDelay ?? TimeSpan.FromMilliseconds(400);

    /// <summary>Throws when any value is outside the supported range.</summary>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ClientName);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(RequestTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(RequestTimeout, TimeSpan.FromMinutes(10));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumAttempts, 5);
        ArgumentOutOfRangeException.ThrowIfLessThan(ResolvedRetryBaseDelay, TimeSpan.Zero);
    }
}

/// <summary>A network failure carrying a stable reason code and, when known, the status code.</summary>
public sealed class NetworkException(
    string reasonCode,
    string message,
    int? statusCode = null,
    Exception? inner = null) : Exception(message, inner), IReasonCoded
{
    /// <inheritdoc />
    public string ReasonCode { get; } = reasonCode;

    /// <summary>HTTP status code when the failure came from a response.</summary>
    public int? StatusCode { get; } = statusCode;
}
