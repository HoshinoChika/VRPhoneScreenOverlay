using System.Net;
using VRPhoneScreenOverlay.Network;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class AppHttpClientTests
{
    private static readonly Uri _secureUri = new("https://example.invalid/resource");

    [Fact]
    public async Task IdempotentRequestRetriesTransientServerErrors()
    {
        using StubHandler handler = new(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            _ => new HttpResponseMessage(HttpStatusCode.OK));
        using AppHttpClient client = CreateClient(handler);

        using HttpResponseMessage response = await client.SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, _secureUri),
            NetworkRetryPolicy.Idempotent);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.Attempts);
    }

    [Fact]
    public async Task NonIdempotentRequestIsSentExactlyOnce()
    {
        // An upload that runs twice creates a duplicate on the server, so a transient failure
        // must surface rather than being retried.
        using StubHandler handler = new(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            _ => new HttpResponseMessage(HttpStatusCode.OK));
        using AppHttpClient client = CreateClient(handler);

        NetworkException failure = await Assert.ThrowsAsync<NetworkException>(async () =>
            await client.SendAsync(
                () => new HttpRequestMessage(HttpMethod.Put, _secureUri),
                NetworkRetryPolicy.None));

        Assert.Equal(NetworkReasonCodes.HttpStatus, failure.ReasonCode);
        Assert.Equal(503, failure.StatusCode);
        Assert.Equal(1, handler.Attempts);
    }

    [Fact]
    public async Task NonTransientStatusIsNotRetried()
    {
        using StubHandler handler = new(
            _ => new HttpResponseMessage(HttpStatusCode.BadRequest),
            _ => new HttpResponseMessage(HttpStatusCode.OK));
        using AppHttpClient client = CreateClient(handler);

        NetworkException failure = await Assert.ThrowsAsync<NetworkException>(async () =>
            await client.SendAsync(
                () => new HttpRequestMessage(HttpMethod.Get, _secureUri),
                NetworkRetryPolicy.Idempotent));

        Assert.Equal(400, failure.StatusCode);
        Assert.Equal(1, handler.Attempts);
    }

    [Fact]
    public async Task RetriesAreBoundedByMaximumAttempts()
    {
        using StubHandler handler = new(Enumerable.Repeat<Func<HttpRequestMessage, HttpResponseMessage>>(
            _ => new HttpResponseMessage(HttpStatusCode.BadGateway),
            5).ToArray());
        using AppHttpClient client = CreateClient(handler, maximumAttempts: 3);

        await Assert.ThrowsAsync<NetworkException>(async () =>
            await client.SendAsync(
                () => new HttpRequestMessage(HttpMethod.Get, _secureUri),
                NetworkRetryPolicy.Idempotent));

        Assert.Equal(3, handler.Attempts);
    }

    [Fact]
    public async Task CallerSuppliedSuccessPredicateKeepsMeaningfulStatuses()
    {
        using StubHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using AppHttpClient client = CreateClient(handler);

        using HttpResponseMessage response = await client.SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, _secureUri),
            NetworkRetryPolicy.Idempotent,
            HttpCompletionOption.ResponseHeadersRead,
            static status => status is HttpStatusCode.OK or HttpStatusCode.NotFound);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, handler.Attempts);
    }

    [Fact]
    public async Task PlainHttpIsRejectedBeforeAnyRequestIsSent()
    {
        using StubHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using AppHttpClient client = CreateClient(handler);

        NetworkException failure = await Assert.ThrowsAsync<NetworkException>(async () =>
            await client.SendAsync(
                () => new HttpRequestMessage(HttpMethod.Get, new Uri("http://example.invalid/x")),
                NetworkRetryPolicy.Idempotent));

        Assert.Equal(NetworkReasonCodes.InsecureScheme, failure.ReasonCode);
        Assert.Equal(0, handler.Attempts);
    }

    [Fact]
    public async Task TransportFailureBecomesAReasonCodedException()
    {
        using StubHandler handler = new(_ => throw new HttpRequestException("no route"));
        using AppHttpClient client = CreateClient(handler, maximumAttempts: 1);

        NetworkException failure = await Assert.ThrowsAsync<NetworkException>(async () =>
            await client.SendAsync(
                () => new HttpRequestMessage(HttpMethod.Get, _secureUri),
                NetworkRetryPolicy.Idempotent));

        Assert.Equal(NetworkReasonCodes.Unavailable, failure.ReasonCode);
    }

    [Fact]
    public async Task CallerCancellationIsNotConvertedIntoATimeout()
    {
        using CancellationTokenSource cancellation = new();
        using StubHandler handler = new(_ =>
        {
            cancellation.Cancel();
            throw new TaskCanceledException();
        });
        using AppHttpClient client = CreateClient(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await client.SendAsync(
                () => new HttpRequestMessage(HttpMethod.Get, _secureUri),
                NetworkRetryPolicy.Idempotent,
                HttpCompletionOption.ResponseHeadersRead,
                null,
                cancellation.Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void MaximumAttemptsOutsideTheSupportedRangeIsRejected(int attempts)
    {
        NetworkClientOptions options = new("test", TimeSpan.FromSeconds(5), attempts);
        Assert.Throws<ArgumentOutOfRangeException>(options.Validate);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, 1)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 1)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 3)]
    public async Task FullAuditTerminalFailuresDisposeEveryResponse(HttpStatusCode status, int attempts)
    {
        using TrackingContent first = new();
        using TrackingContent second = new();
        using TrackingContent third = new();
        TrackingContent[] contents = [first, second, third];
        using StubHandler handler = new(contents.Take(attempts).Select<TrackingContent,
            Func<HttpRequestMessage, HttpResponseMessage>>(content =>
                _ => new HttpResponseMessage(status) { Content = content }).ToArray());
        using AppHttpClient client = CreateClient(handler, maximumAttempts: attempts);
        await Assert.ThrowsAsync<NetworkException>(async () =>
            await client.SendAsync(() => new HttpRequestMessage(HttpMethod.Get, _secureUri),
                NetworkRetryPolicy.Idempotent, HttpCompletionOption.ResponseHeadersRead));
        Assert.All(contents.Take(attempts), content => Assert.True(content.WasDisposed));
    }

    [Fact]
    public async Task FullAuditSuccessfulResponseTransfersOwnershipToCaller()
    {
        using TrackingContent content = new();
        using StubHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        using AppHttpClient client = CreateClient(handler);
        using HttpResponseMessage response = await client.SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, _secureUri), NetworkRetryPolicy.None);
        Assert.False(content.WasDisposed);
        response.Dispose();
        Assert.True(content.WasDisposed);
    }

    private sealed class TrackingContent() : StringContent("synthetic response")
    {
        public bool WasDisposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }

    private static AppHttpClient CreateClient(StubHandler handler, int maximumAttempts = 3) =>
#pragma warning disable CA2000 // AppHttpClient takes ownership via ownsHttpClient: true and
        // disposes the HttpClient, which in turn disposes the handler. The analyzer cannot see
        // ownership transferred through a constructor argument.
        new(
            new NetworkClientOptions(
                "test-client",
                TimeSpan.FromSeconds(5),
                maximumAttempts,
                TimeSpan.Zero),
            new HttpClient(handler),
            ownsHttpClient: true);
#pragma warning restore CA2000

    private sealed class StubHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage>[] _responses = responses;
        private int _attempts;

        public int Attempts => _attempts;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            int index = Interlocked.Increment(ref _attempts) - 1;
            Func<HttpRequestMessage, HttpResponseMessage> responder =
                _responses[Math.Min(index, _responses.Length - 1)];
            return Task.FromResult(responder(request));
        }
    }
}
