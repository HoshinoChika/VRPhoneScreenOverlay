using System.Net.Http.Json;
using VRPhoneScreenOverlay.Network;
using VRPhoneScreenOverlay.Protocols;

namespace VRPhoneScreenOverlay.Diagnostics.Usage;

/// <summary>One cancellable heartbeat loop. No queue, retry burst, or device data.</summary>
public sealed class UsageHeartbeatClient : IAsyncDisposable, IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);
    private readonly IAppHttpClient _http;
    private readonly string _identityPath;
    private readonly TimeProvider _time;
    private readonly Uri? _endpoint;
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private int _disposed;
    private string _reasonCode = "HEARTBEAT_NOT_STARTED";

    public UsageHeartbeatClient(string identityPath, IAppHttpClient http, TimeProvider? timeProvider = null, Uri? endpoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identityPath);
        ArgumentNullException.ThrowIfNull(http);
        _identityPath = identityPath;
        _http = http;
        _time = timeProvider ?? TimeProvider.System;
        _endpoint = endpoint;
    }

    public string ReasonCode => Volatile.Read(ref _reasonCode);

    public static UsageHeartbeatClient CreateDefault(ClientServiceConfiguration configuration) => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VRPhoneScreenOverlay", "usage-installation-id.txt"),
        new AppHttpClient(new NetworkClientOptions("usage-heartbeat", TimeSpan.FromSeconds(5), 1)
        { UseSystemProxy = false }), endpoint: configuration.UsageHeartbeatUri);

    // Called once by the composition root; smoke and snapshot modes return before this point.
    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_loop is not null) { return; }
        if (_endpoint is null)
        { Volatile.Write(ref _reasonCode, "HEARTBEAT_NOT_CONFIGURED"); return; }
        if (Environment.GetEnvironmentVariable("VRPSO_USAGE_HEARTBEAT") == "0")
        {
            Volatile.Write(ref _reasonCode, "HEARTBEAT_DISABLED");
            return;
        }
        _loop = Task.Run(() => RunAsync(_stop.Token));
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }
        try
        {
            await _stop.CancelAsync().ConfigureAwait(false);
            if (_loop is not null) { await _loop.ConfigureAwait(false); }
        }
        finally
        {
            try { _http.Dispose(); }
            finally
            {
                _stop.Dispose();
                Volatile.Write(ref _reasonCode, "HEARTBEAT_STOPPED");
            }
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            string identity = await UsageInstallationIdentity.LoadAsync(_identityPath, cancellationToken)
                .ConfigureAwait(false);
            // Spread launches without delaying UI or running network work on its thread.
            await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(5, 16)), _time, cancellationToken)
                .ConfigureAwait(false);
            using PeriodicTimer timer = new(Interval, _time);
            do
            {
                Volatile.Write(ref _reasonCode, "HEARTBEAT_SENDING");
                using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                try
                {
                    using HttpResponseMessage response = await _http.SendAsync(
                        () => new HttpRequestMessage(HttpMethod.Post, _endpoint)
                        { Content = JsonContent.Create(new UsageHeartbeatRequest(1, identity)) },
                        NetworkRetryPolicy.None, cancellationToken: deadline.Token).ConfigureAwait(false);
                    Volatile.Write(ref _reasonCode, "HEARTBEAT_SENT");
                }
                catch (NetworkException exception)
                {
                    Volatile.Write(ref _reasonCode, exception.ReasonCode);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    Volatile.Write(ref _reasonCode, "HEARTBEAT_TIMEOUT");
                }
            } while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (IOException) { Volatile.Write(ref _reasonCode, "HEARTBEAT_IDENTITY_UNAVAILABLE"); }
        catch (UnauthorizedAccessException) { Volatile.Write(ref _reasonCode, "HEARTBEAT_IDENTITY_UNAVAILABLE"); }
        catch (InvalidDataException) { Volatile.Write(ref _reasonCode, "HEARTBEAT_IDENTITY_INVALID"); }
    }
}
