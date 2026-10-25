using System.Net;
using System.Net.Sockets;

namespace VRPhoneScreenOverlay.Android;

internal sealed partial class AndroidConnectionService : IAndroidConnectionInspectionService
{
    public async Task<AndroidConnectionInspection> InspectConnectionAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token, _stopSource.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        await _operationGate.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            await InitializeConnectionPreferencesAsync(deadline.Token).ConfigureAwait(false);
            ConnectionPreferences preferences = _connectionPreferences.Value;
            bool wireless = preferences.Method != PhoneConnectionMethod.Usb;
            AndroidConnectionInspectionStage[] stages = await AndroidConnectionInspector.InspectAsync(CreateCommandRunner(), wireless, deadline.Token).ConfigureAwait(false);
            SavedAndroidConnection[] saved = preferences.Devices.Where(d => ConnectionPreferencesStore.Supports(preferences.Method, d.Transport)).ToArray();
            string reachability = "not_applicable";
            if (wireless)
            {
                // Only the most recently connected saved target, never an arbitrary model-provided destination.
                string? endpoint = saved.OrderByDescending(d => d.LastConnectedAt).FirstOrDefault()?.Endpoint;
                reachability = "no_saved_endpoint";
                if (endpoint is not null && IPEndPoint.TryParse(endpoint, out IPEndPoint? address))
                {
                    using TcpClient client = new(address.AddressFamily);
                    using CancellationTokenSource connect = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                    connect.CancelAfter(TimeSpan.FromSeconds(2));
                    try { await client.ConnectAsync(address.Address, address.Port, connect.Token).ConfigureAwait(false); reachability = "reachable"; }
                    catch (SocketException) { reachability = "unreachable"; }
                    catch (OperationCanceledException) when (!deadline.IsCancellationRequested) { reachability = "timeout"; }
                }
            }
            return new(preferences.Method.ToString(), saved.Length, saved.Count(d => d.AutoConnect), reachability, stages);
        }
        finally { _operationGate.Release(); }
    }
}
