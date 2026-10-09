using System.Text;
using System.Text.Json.Nodes;

namespace VRPhoneScreenOverlay.SteamVR;

public interface IBindingLiveApplication
{
    public ValueTask<IBindingLiveTransaction> BeginAsync(CancellationToken cancellationToken);
}

public interface IBindingLiveTransaction : IAsyncDisposable
{
    public IReadOnlyList<string> ControllerTypes => [];
    public ValueTask ApplyAsync(IReadOnlyDictionary<string, string> runtimeBindings, CancellationToken cancellationToken);
    public ValueTask ApplySavedAsync(IReadOnlyDictionary<string, string> runtimeBindings, CancellationToken cancellationToken)
        => ApplyAsync(runtimeBindings, cancellationToken);
    public void Commit();
    public void Commit(string revision) => Commit();
}

// Same local selection protocol as SteamVR's controller binding editor. It
// changes bindings inside the existing input session; no media/native restart.
public sealed class OpenVrLiveBindingApplication : IBindingLiveApplication
{
    private readonly Func<HttpClient> _clients;
    private readonly Func<HttpClient, CancellationToken, ValueTask<IOpenVrBindingSelectionChannel>> _channels;
    private readonly string _snapshotRoot;
    private readonly Action _acceptPrepared;
    private readonly Action<string>? _acceptRevision;

    public OpenVrLiveBindingApplication() : this(CreateClient, OpenVrBindingSelectionChannel.ConnectAsync,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRPhoneScreenOverlay", "steamvr", "applied-bindings"),
        () => OpenVrBindingStartup.AcceptLivePrepared(), revision => OpenVrBindingStartup.AcceptLivePrepared(revision))
    { }

    internal OpenVrLiveBindingApplication(Func<HttpClient> clients,
        Func<HttpClient, CancellationToken, ValueTask<IOpenVrBindingSelectionChannel>> channels,
        string snapshotRoot, Action acceptPrepared, Action<string>? acceptRevision = null)
    { _clients = clients; _channels = channels; _snapshotRoot = snapshotRoot; _acceptPrepared = acceptPrepared; _acceptRevision = acceptRevision; }

    public async ValueTask<IBindingLiveTransaction> BeginAsync(CancellationToken cancellationToken)
    {
        HttpClient client = _clients();
        IOpenVrBindingSelectionChannel? channel = null;
        try
        {
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            JsonObject actions = await ReadActionsAsync(client, deadline.Token).ConfigureAwait(false);
            JsonObject current = actions["current_binding_url"] as JsonObject ?? throw new InvalidDataException("Missing binding selection.");
            JsonObject defaults = actions["default_bindings"] as JsonObject ?? throw new InvalidDataException("Missing default selections.");
            Dictionary<string, string> previous = new(StringComparer.Ordinal);
            if (current.Count > 32) { throw new InvalidDataException("Too many registered controller types."); }
            foreach (string type in current.Select(item => item.Key))
            {
                if (!OpenVrGenericBinding.ValidType(type)) { throw new InvalidDataException("Invalid registered controller type."); }
                string? uri = current[type]?.GetValue<string>();
                if (string.IsNullOrEmpty(uri)) { uri = defaults[type]?.GetValue<string>(); }
                if (!string.IsNullOrEmpty(uri)) { previous.Add(type, uri); }
            }
            if (previous.Count == 0) { throw new InvalidDataException("No binding profile is registered."); }
            channel = await _channels(client, deadline.Token).ConfigureAwait(false);
            return new Transaction(client, channel, previous, _snapshotRoot, _acceptPrepared, _channels, _acceptRevision);
        }
        catch
        {
            if (channel is not null) { await channel.DisposeAsync().ConfigureAwait(false); }
            client.Dispose();
            throw;
        }
    }

    private static HttpClient CreateClient()
    {
        SocketsHttpHandler? handler = new() { UseProxy = false, AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(3) };
        HttpClient? client = null;
        try
        {
            client = new HttpClient(handler, disposeHandler: true);
            handler = null; // Ownership transferred to HttpClient.
            // SteamVR listens on IPv4. Avoid localhost's failed IPv6 attempt
            // before both the HTTP and WebSocket control connections.
            client.BaseAddress = new Uri("http://127.0.0.1:27062/");
            client.Timeout = TimeSpan.FromSeconds(8);
            client.DefaultRequestHeaders.Add("Origin", "http://localhost:27062");
            return client;
        }
        catch { client?.Dispose(); throw; }
        finally { handler?.Dispose(); }
    }

    internal static async Task<JsonObject> ReadJsonAsync(HttpClient client, string relative, CancellationToken token)
    {
        using HttpResponseMessage response = await client.GetAsync(relative, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using Stream body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using MemoryStream data = new();
        byte[] buffer = new byte[4096];
        int length;
        while ((length = await body.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            if (data.Length + length > 1_048_576) { throw new InvalidDataException("SteamVR binding response is too large."); }
            await data.WriteAsync(buffer.AsMemory(0, length), token).ConfigureAwait(false);
        }
        return JsonNode.Parse(data.ToArray()) as JsonObject ?? throw new InvalidDataException("Invalid SteamVR binding response.");
    }

    private static Task<JsonObject> ReadActionsAsync(HttpClient client, CancellationToken token) =>
        ReadJsonAsync(client, "input/getactions.json?app_key=" + Uri.EscapeDataString(OpenVrInputManifest.ApplicationKey), token);

    private sealed class Transaction(HttpClient client, IOpenVrBindingSelectionChannel channel,
        Dictionary<string, string> previous, string snapshotRoot, Action acceptPrepared,
        Func<HttpClient, CancellationToken, ValueTask<IOpenVrBindingSelectionChannel>> channels, Action<string>? acceptRevision) : IBindingLiveTransaction
    {
        public IReadOnlyList<string> ControllerTypes => previous.Keys.ToArray();
        private IOpenVrBindingSelectionChannel _channel = channel;
        private readonly List<string> _selected = [];
        private readonly string _directory = Path.Combine(snapshotRoot, Guid.NewGuid().ToString("N"));
        private bool _committed;
        private bool _disposed;
        private bool _inputSuspended;

        public ValueTask ApplyAsync(IReadOnlyDictionary<string, string> runtimeBindings, CancellationToken cancellationToken)
            => ApplyCoreAsync(runtimeBindings, requireAll: true, cancellationToken);

        public ValueTask ApplySavedAsync(IReadOnlyDictionary<string, string> runtimeBindings, CancellationToken cancellationToken)
            => ApplyCoreAsync(runtimeBindings, requireAll: false, cancellationToken);

        private async ValueTask ApplyCoreAsync(IReadOnlyDictionary<string, string> runtimeBindings, bool requireAll, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_inputSuspended)
            {
                lock (OpenVrRuntimeHost.ApiGate) { OpenVrRuntimeHost.Events.BeginBindingChange(); }
                _inputSuspended = true;
            }
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            AssertSafeSnapshotPath(_directory);
            Directory.CreateDirectory(_directory);
            Dictionary<string, string> requested = new(StringComparer.Ordinal);
            foreach (string type in previous.Keys)
            {
                if (!runtimeBindings.TryGetValue(type, out string? path))
                {
                    if (requireAll) { throw new InvalidDataException("Missing prepared default."); }
                    continue;
                }
                if (new FileInfo(path).Length > 1_048_576) { throw new InvalidDataException("Default binding is too large."); }
                JsonObject expected = JsonNode.Parse(await File.ReadAllTextAsync(path, deadline.Token).ConfigureAwait(false)) as JsonObject
                    ?? throw new InvalidDataException("Invalid prepared binding.");
                string target = Path.Combine(_directory, type + ".json");
                await File.WriteAllTextAsync(target, expected.ToJsonString(), new UTF8Encoding(false), deadline.Token).ConfigureAwait(false);
                string uri = new Uri(target).AbsoluteUri;
                // A distinct immutable URL also works when the current binding
                // already points at a local file. No stale same-URL selection.
                _selected.Add(type);
                await _channel.SelectAsync(type, uri, deadline.Token).ConfigureAwait(false);
                requested.Add(type, uri);
            }
            // Each selection already has SteamVR's completion acknowledgement.
            // Confirm all target URLs in one query; do not reload the binding
            // files or compare their numeric parameters a second time.
            await VerifySelectionsAsync(requested, deadline.Token).ConfigureAwait(false);
        }

        private async Task VerifySelectionsAsync(IReadOnlyDictionary<string, string> requested, CancellationToken token)
        {
            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            while (true)
            {
                JsonObject actions = await ReadActionsAsync(client, deadline.Token).ConfigureAwait(false);
                if (requested.All(selection => SameSelection(actions["current_binding_url"]?[selection.Key]?.GetValue<string>(), selection.Value)))
                {
                    return;
                }
                await Task.Delay(100, deadline.Token).ConfigureAwait(false);
            }
        }

        private static bool SameSelection(string? actual, string expected) => actual == expected ||
            (Uri.TryCreate(actual, UriKind.Absolute, out Uri? first) && Uri.TryCreate(expected, UriKind.Absolute, out Uri? second) &&
                first.IsFile && second.IsFile && string.Equals(first.LocalPath, second.LocalPath, StringComparison.OrdinalIgnoreCase));

        public void Commit()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            acceptPrepared();
            _committed = true;
        }

        public void Commit(string revision)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (acceptRevision is not null) { acceptRevision(revision); }
            else { acceptPrepared(); }
            _committed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) { return; }
            _disposed = true;
            try
            {
                if (!_committed && _selected.Count > 0)
                {
                    using CancellationTokenSource recovery = new(TimeSpan.FromSeconds(20));
                    // Receive cancellation aborts a WebSocket. A fresh control
                    // connection also excludes stale acknowledgements on rollback.
                    await _channel.DisposeAsync().ConfigureAwait(false);
                    _channel = await channels(client, recovery.Token).ConfigureAwait(false);
                    List<Exception> errors = [];
                    foreach (string type in _selected.AsEnumerable().Reverse())
                    {
                        try
                        {
                            await _channel.SelectAsync(type, previous[type], recovery.Token).ConfigureAwait(false);
                        }
                        catch (Exception error) when (error is HttpRequestException or IOException or InvalidOperationException or OperationCanceledException)
                        { errors.Add(error); }
                    }
                    try
                    {
                        await VerifySelectionsAsync(_selected.ToDictionary(type => type, type => previous[type], StringComparer.Ordinal), recovery.Token).ConfigureAwait(false);
                    }
                    catch (Exception error) when (error is HttpRequestException or IOException or InvalidOperationException or OperationCanceledException)
                    { errors.Add(error); }
                    if (errors.Count > 0) { throw new AggregateException("Binding selection rollback failed.", errors); }
                }
                if (!_committed) { DeleteSnapshot(_directory); }
                else { PruneSnapshots(); }
            }
            finally
            {
                try { await _channel.DisposeAsync().ConfigureAwait(false); }
                finally
                {
                    client.Dispose();
                    if (_inputSuspended) { lock (OpenVrRuntimeHost.ApiGate) { OpenVrRuntimeHost.Events.EndBindingChange(); } }
                }
            }
        }

        private void PruneSnapshots()
        {
            // Keep current and one previous snapshot, with a bounded directory
            // scan. Cleanup never determines whether application succeeded.
            try
            {
                string? priorDirectory = previous.Values.Where(uri => Uri.TryCreate(uri, UriKind.Absolute, out Uri? value) && value.IsFile)
                    .Select(uri => Path.GetDirectoryName(new Uri(uri).LocalPath)).FirstOrDefault(path => path is not null && Path.GetDirectoryName(path) == snapshotRoot);
                foreach (string directory in Directory.EnumerateDirectories(snapshotRoot).Take(128))
                {
                    if (directory != _directory && directory != priorDirectory) { DeleteSnapshot(directory); }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }

        private static void DeleteSnapshot(string directory)
        {
            if (!Directory.Exists(directory) || !Guid.TryParseExact(Path.GetFileName(directory), "N", out _) ||
                (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) { return; }
            foreach (string file in Directory.EnumerateFiles(directory, "*.json").Take(32)) { File.Delete(file); }
            Directory.Delete(directory, recursive: false);
        }

        private static void AssertSafeSnapshotPath(string path)
        {
            for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                { throw new InvalidDataException("Binding snapshot path contains a reparse point."); }
            }
        }
    }
}
