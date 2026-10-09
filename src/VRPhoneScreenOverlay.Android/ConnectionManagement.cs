using System.Text.Json;

namespace VRPhoneScreenOverlay.Android;

public enum PhoneConnectionMethod { Usb, WirelessScan, WirelessCode, WirelessManual }

public sealed record ManagedAndroidDevice(string DeviceKey, string DisplayName, string Model,
    AndroidTransport Transport, bool IsAvailable, AndroidDeviceStatus Status, bool IsSelected,
    bool AutoConnect, DateTimeOffset? LastConnectedAt, string? CustomName = null);

public sealed record ConnectionManagementSnapshot(PhoneConnectionMethod Method, IReadOnlyList<ManagedAndroidDevice> Devices);

public interface IAndroidConnectionManagementService
{
    public ConnectionManagementSnapshot ManagementSnapshot { get; }
    public ValueTask SetConnectionMethodAsync(PhoneConnectionMethod method, CancellationToken cancellationToken);
    public ValueTask SetConnectionMethodAsync(PhoneConnectionMethod method, bool preserveCurrentConnection,
        CancellationToken cancellationToken) => SetConnectionMethodAsync(method, cancellationToken);
    public ValueTask SetDeviceAutoConnectAsync(string deviceKey, bool enabled, CancellationToken cancellationToken);
    public ValueTask ForgetDeviceAsync(string deviceKey, CancellationToken cancellationToken);
    public ValueTask DisconnectSelectionAsync(CancellationToken cancellationToken);
    public ValueTask ConnectKnownDeviceAsync(string deviceKey, CancellationToken cancellationToken);
    public ValueTask RenameDeviceAsync(string deviceKey, string name, CancellationToken cancellationToken);
    public ValueTask SaveDevicePreferencesAsync(string deviceKey, string? customName, bool autoConnect, CancellationToken cancellationToken) =>
        ValueTask.FromException(new NotSupportedException("Atomic device preference saving is unavailable."));
    public bool IsSelectedDevice(string deviceKey) => ManagementSnapshot.Devices.Any(device => device.DeviceKey == deviceKey && device.IsSelected);
}

internal sealed record SavedAndroidConnection(string DeviceKey, string DisplayName, string Model,
    AndroidTransport Transport, bool AutoConnect, DateTimeOffset? LastConnectedAt,
    string? Endpoint = null, string? ServiceName = null, string? CustomName = null,
    string? PhysicalDeviceKey = null, DateTimeOffset? PreferenceChangedAt = null);

internal sealed record ConnectionPreferences(int SchemaVersion, PhoneConnectionMethod Method,
    IReadOnlyList<SavedAndroidConnection> Devices)
{
    public static ConnectionPreferences Default { get; } = new(1, PhoneConnectionMethod.Usb, []);
}

// Discovery owns the store and serializes all operations. At most 64 local records;
// evict the oldest nonautomatic record first. No pairing codes or credentials.
internal sealed class ConnectionPreferencesStore(string? path)
{
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private ConnectionPreferences _value = ConnectionPreferences.Default;
    private bool _loaded;
    public bool Enabled => path is not null;
    public ConnectionPreferences Value => Volatile.Read(ref _value);

    public async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        if (_loaded) { return; }
        if (path is not null && File.Exists(path))
        {
            try
            {
                if (new FileInfo(path).Length > 256 * 1024) { throw new JsonException("Connection file too large."); }
                ConnectionPreferences? loaded = JsonSerializer.Deserialize<ConnectionPreferences>(
                    await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), _json);
                if (loaded is { SchemaVersion: 1, Devices: not null } && Enum.IsDefined(loaded.Method))
                {
                    _value = loaded with
                    {
                        Devices = loaded.Devices.Where(device => device is not null &&
                        !string.IsNullOrWhiteSpace(device.DeviceKey) && device.DeviceKey.Length <= 128 &&
                        device.Transport is AndroidTransport.Usb or AndroidTransport.Network)
                        .DistinctBy(device => device.DeviceKey).Take(64)
                        .Select(device => device with { CustomName = DeviceNamePolicy.IsValid(device.CustomName) ? device.CustomName : null }).ToArray()
                    };
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { }
        }
        _loaded = true;
    }

    public async ValueTask SaveAsync(ConnectionPreferences candidate, CancellationToken cancellationToken)
    {
        candidate = candidate with
        {
            Devices = candidate.Devices.GroupBy(Scope).SelectMany(group => group
                .OrderByDescending(device => device.LastConnectedAt).Take(4)).OrderByDescending(device => device.AutoConnect)
            .ThenByDescending(device => device.LastConnectedAt).Take(64).ToArray()
        };
        if (path is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(candidate, _json), cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                try { File.Delete(temporary); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        }
        Volatile.Write(ref _value, candidate);
    }

    public async ValueTask RememberAsync(AdbDeviceRecord device, string displayName, string androidModel,
        string? services, string? connectedEndpoint, CancellationToken cancellationToken)
    {
        SavedAndroidConnection? previous = Value.Devices.FirstOrDefault(saved => saved.DeviceKey == device.DeviceKey);
        string? endpoint = previous?.Endpoint;
        string? service = previous?.ServiceName;
        if (connectedEndpoint is not null && WirelessAdbEndpoint.TryNormalize(connectedEndpoint, out string verified)) { endpoint = verified; }
        if (device.Transport == AndroidTransport.Network)
        {
            if (WirelessAdbEndpoint.TryNormalize(device.Serial, out string normalized))
            { endpoint = normalized; service = services is null ? service : WirelessAdbEndpoint.FindConnectService(services, normalized) ?? service; }
            else
            {
                service = device.Serial.TrimEnd('.');
                endpoint = ResolveEndpoint(service, services) ?? endpoint;
            }
        }
        string physical = AndroidPhysicalDeviceIdentity.Current(device, services, Value);
        SavedAndroidConnection? shared = Value.Devices.Where(saved => saved.Transport == device.Transport && AndroidPhysicalDeviceIdentity.Saved(saved) == physical)
            .OrderByDescending(saved => saved.PreferenceChangedAt).ThenByDescending(saved => saved.LastConnectedAt).FirstOrDefault();
        SavedAndroidConnection value = new(device.DeviceKey, displayName, androidModel, device.Transport,
            shared?.AutoConnect == true, DateTimeOffset.UtcNow, endpoint, service, shared?.CustomName, physical, shared?.PreferenceChangedAt);
        await SaveAsync(Value with { Devices = [.. Value.Devices.Where(saved => saved.DeviceKey != device.DeviceKey), value] }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ReconcileAsync(IReadOnlyList<AdbDeviceRecord> routes, string? services, CancellationToken cancellationToken)
    {
        SavedAndroidConnection[] migrated = Value.Devices.Select(saved =>
        {
            AdbDeviceRecord? live = routes.FirstOrDefault(route => route.DeviceKey == saved.DeviceKey);
            string identity = live is null ? AndroidPhysicalDeviceIdentity.Saved(saved) : AndroidPhysicalDeviceIdentity.Current(live, services, Value);
            return saved with { PhysicalDeviceKey = identity, DisplayName = live?.RecognizedName ?? saved.DisplayName };
        }).GroupBy(Scope).SelectMany(group =>
        {
            SavedAndroidConnection latest = group.OrderByDescending(saved => saved.PreferenceChangedAt).ThenByDescending(saved => saved.LastConnectedAt).First();
            string name = routes.FirstOrDefault(route => Scope(AndroidPhysicalDeviceIdentity.Current(route, services, Value), route.Transport) == group.Key && route.RecognizedName is not null)?.RecognizedName ??
                group.OrderByDescending(saved => saved.LastConnectedAt).First().DisplayName;
            return group.Select(saved => saved with { CustomName = latest.CustomName, AutoConnect = latest.AutoConnect, PreferenceChangedAt = latest.PreferenceChangedAt, DisplayName = name });
        }).ToArray();
        if (!Value.Devices.OrderBy(saved => saved.DeviceKey).SequenceEqual(migrated.OrderBy(saved => saved.DeviceKey)))
        { await SaveAsync(Value with { Devices = migrated }, cancellationToken).ConfigureAwait(false); }
    }

    public static string? ResolveEndpoint(string? service, string? services)
    {
        if (service is null || services is null) { return null; }
        foreach (string line in services.Split('\n'))
        {
            string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 3 && fields[1].TrimEnd('.') == "_adb-tls-connect._tcp" &&
                fields[0].TrimEnd('.') + "._adb-tls-connect._tcp" == service.TrimEnd('.') &&
                WirelessAdbEndpoint.TryNormalize(fields[2], out string endpoint)) { return endpoint; }
        }
        return null;
    }

    public static string Scope(SavedAndroidConnection saved) => Scope(AndroidPhysicalDeviceIdentity.Saved(saved), saved.Transport);

    public static string Scope(string physical, AndroidTransport transport) => physical + ":" + transport;

    public static bool Supports(PhoneConnectionMethod method, AndroidTransport transport) =>
        method == PhoneConnectionMethod.Usb ? transport == AndroidTransport.Usb : transport == AndroidTransport.Network;

    public static AdbDeviceRecord? SelectAutomatic(IReadOnlyList<AdbDeviceRecord> devices, ConnectionPreferences preferences) =>
        preferences.Devices.Where(saved => saved.AutoConnect && Supports(preferences.Method, saved.Transport))
            .OrderByDescending(saved => saved.LastConnectedAt).ThenBy(saved => saved.DeviceKey, StringComparer.Ordinal)
            .Select(saved => devices.FirstOrDefault(device => device.Status == AndroidDeviceStatus.Ready &&
                device.Transport == saved.Transport && device.DeviceKey == saved.DeviceKey) ??
                devices.FirstOrDefault(device => device.Status == AndroidDeviceStatus.Ready && device.Transport == saved.Transport &&
                    AndroidPhysicalDeviceIdentity.Current(device, null, preferences) == AndroidPhysicalDeviceIdentity.Saved(saved)))
            .FirstOrDefault(device => device is not null);
}
