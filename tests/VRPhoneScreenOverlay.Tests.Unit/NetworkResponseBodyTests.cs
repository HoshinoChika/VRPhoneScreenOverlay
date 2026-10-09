using System.Text;
using System.Text.Json;
using VRPhoneScreenOverlay.Network;
using VRPhoneScreenOverlay.Update;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class NetworkResponseBodyTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromMilliseconds(150);
    private static readonly JsonSerializerOptions _jsonOptions = new();

    [Fact]
    public async Task UnknownLengthJsonCannotExceedTheByteBudget()
    {
        using TestReadStream source = new("\"" + new string('x', NetworkResponseBody.MaximumJsonBytes) + "\"");
        using StreamContent content = new(source);
        NetworkException error = await Assert.ThrowsAsync<NetworkException>(async () =>
            await NetworkResponseBody.ReadJsonAsync<string>(content, _jsonOptions, TimeSpan.FromSeconds(5), CancellationToken.None));
        Assert.Equal(NetworkReasonCodes.ResponseTooLarge, error.ReasonCode);
        Assert.True(source.Disposed);
    }

    [Fact]
    public async Task StalledManifestBodyTimesOutAfterSuccessfulHeaders()
    {
        using TestReadStream source = new("{\"payload\":", stallAtEnd: true);
        using StubClient client = new(source);
        using HttpUpdateService update = new(
            new UpdateServiceOptions(new Uri("https://example.invalid/manifest"), "beta", 1024, _timeout),
            client, ownsHttpClient: false);
        using CancellationTokenSource guard = new(TimeSpan.FromSeconds(5));

        NetworkException failure = await Assert.ThrowsAsync<NetworkException>(async () =>
            await update.CheckAsync("0.2.6-beta.5", guard.Token));

        Assert.Equal(NetworkReasonCodes.Timeout, failure.ReasonCode);
        Assert.False(guard.IsCancellationRequested);
        Assert.True(source.Disposed);
        Assert.Equal(1, client.RequestCount);
    }

    [Fact]
    public async Task StalledDownloadReadTimesOutAndIsNotReplayed()
    {
        using TestReadStream source = new("a", stallAtEnd: true);
        using StreamContent content = new(source);
        using CancellationTokenSource guard = new(TimeSpan.FromSeconds(5));
        await using Stream download = await NetworkResponseBody.OpenDownloadAsync(content, _timeout, guard.Token);
        byte[] buffer = new byte[8];
        Assert.Equal(1, await download.ReadAsync(buffer, guard.Token));

        NetworkException failure = await Assert.ThrowsAsync<NetworkException>(() =>
            download.ReadAsync(buffer.AsMemory(), guard.Token).AsTask());

        Assert.Equal(NetworkReasonCodes.Timeout, failure.ReasonCode);
        Assert.False(guard.IsCancellationRequested);
        Assert.Equal(2, source.ReadCount);
    }

    [Fact]
    public async Task ProgressingDownloadMayOutliveOneIdleTimeout()
    {
        TimeSpan idleTimeout = TimeSpan.FromMilliseconds(500);
        using TestReadStream source = new("12345678", delay: TimeSpan.FromMilliseconds(90), chunkSize: 1);
        using StreamContent content = new(source);
        using CancellationTokenSource guard = new(TimeSpan.FromSeconds(8));
        await using Stream download = await NetworkResponseBody.OpenDownloadAsync(content, idleTimeout, guard.Token);
        using MemoryStream destination = new();

        await download.CopyToAsync(destination, guard.Token);

        Assert.Equal("12345678", Encoding.UTF8.GetString(destination.ToArray()));
        Assert.Equal(9, source.ReadCount);
    }

    [Fact]
    public async Task JsonBodyHasATotalDeadlineEvenIfBytesKeepArriving()
    {
        using TestReadStream source = new("{\"value\":123}", delay: TimeSpan.FromMilliseconds(60), chunkSize: 1);
        using StreamContent content = new(source);
        using CancellationTokenSource guard = new(TimeSpan.FromSeconds(5));

        NetworkException failure = await Assert.ThrowsAsync<NetworkException>(async () =>
            await NetworkResponseBody.ReadJsonAsync<JsonElement>(content, _jsonOptions, _timeout, guard.Token));

        Assert.Equal(NetworkReasonCodes.Timeout, failure.ReasonCode);
        Assert.True(source.Disposed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CallerCancellationRemainsCancellation(bool json)
    {
        using TestReadStream source = new(string.Empty, stallAtEnd: true);
        using StreamContent content = new(source);
        using CancellationTokenSource caller = new();
        Task pending;
        if (json)
        {
            pending = NetworkResponseBody.ReadJsonAsync<JsonElement>(
                content, _jsonOptions, TimeSpan.FromSeconds(5), caller.Token).AsTask();
        }
        else
        {
            await using Stream download = await NetworkResponseBody.OpenDownloadAsync(
                content, TimeSpan.FromSeconds(5), caller.Token);
            pending = download.ReadAsync(new byte[8], caller.Token).AsTask();
            await caller.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            return;
        }
        await caller.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task MalformedJsonBecomesASafeReasonCodedFailure()
    {
        using StringContent content = new("{invalid");

        NetworkException failure = await Assert.ThrowsAsync<NetworkException>(async () =>
            await NetworkResponseBody.ReadJsonAsync<JsonElement>(
                content, _jsonOptions, TimeSpan.FromSeconds(5), CancellationToken.None));

        Assert.Equal(NetworkReasonCodes.ResponseInvalid, failure.ReasonCode);
        Assert.DoesNotContain("{invalid", failure.Message, StringComparison.Ordinal);
    }

    private sealed class StubClient(Stream stream) : IAppHttpClient
    {
        private readonly HttpResponseMessage _response = new(System.Net.HttpStatusCode.OK)
        {
            Content = new StreamContent(stream),
        };
        public int RequestCount { get; private set; }
        public ValueTask<HttpResponseMessage> SendAsync(
            Func<HttpRequestMessage> createRequest,
            NetworkRetryPolicy retryPolicy,
            HttpCompletionOption completionOption = HttpCompletionOption.ResponseHeadersRead,
            Func<System.Net.HttpStatusCode, bool>? isSuccess = null,
            CancellationToken cancellationToken = default)
        {
            RequestCount++;
            using HttpRequestMessage request = createRequest();
            return ValueTask.FromResult(_response);
        }
        public void Dispose() => _response.Dispose();
    }

    private sealed class TestReadStream(
        string text,
        bool stallAtEnd = false,
        TimeSpan delay = default,
        int chunkSize = int.MaxValue) : Stream
    {
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes(text);
        private int _position;
        public int ReadCount { get; private set; }
        public bool Disposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            if (_position == _bytes.Length)
            {
                if (stallAtEnd) { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
                return 0;
            }
            if (delay > TimeSpan.Zero) { await Task.Delay(delay, cancellationToken); }
            int count = Math.Min(Math.Min(buffer.Length, chunkSize), _bytes.Length - _position);
            _bytes.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
