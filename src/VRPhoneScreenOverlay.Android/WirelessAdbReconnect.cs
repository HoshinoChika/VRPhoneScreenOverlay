namespace VRPhoneScreenOverlay.Android;

// Discovery owns one remembered endpoint, no queue or background worker. Never
// probe a subnet: retry only a previously authorized transport, at most every 10s.
internal sealed class WirelessAdbReconnect(string? path, Func<CancellationToken, ValueTask<IAdbCommandRunner>> createRunner)
{
    private string? _endpoint;
    private bool _loaded;
    private bool _suppressed;
    private long _nextAttempt;
    private IAdbCommandRunner? _runner;

    public void Resume() => _suppressed = false;

    public async ValueTask ForgetAsync(CancellationToken cancellationToken)
    {
        _suppressed = true;
        _loaded = true;
        _endpoint = null;
        await SaveAsync(string.Empty, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> ObserveAsync(AdbListResult list, CancellationToken cancellationToken, long? timestamp = null)
    {
        if (path is null || _suppressed) { return false; }
        try
        {
            if (!_loaded)
            {
                _loaded = true;
                if (File.Exists(path) && new FileInfo(path).Length <= 128)
                {
                    string saved = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                    if (WirelessAdbEndpoint.TryNormalize(saved, out string normalized)) { _endpoint = normalized; }
                }
            }
            AdbDeviceRecord? network = list.Devices.FirstOrDefault(device =>
                device.Transport == AndroidTransport.Network && device.Status == AndroidDeviceStatus.Ready);
            if (network is not null)
            {
                string? candidate = null;
                if (WirelessAdbEndpoint.TryNormalize(network.Serial, out string direct)) { candidate = direct; }
                else
                {
                    string? mdns = list.WirelessServices;
                    if (mdns is null)
                    {
                        _runner ??= await createRunner(cancellationToken).ConfigureAwait(false);
                        AdbCommandResult services = await _runner.RunAsync(["mdns", "services"], TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                        if (services.Succeeded) { mdns = services.StandardOutput; }
                    }
                    if (mdns is not null)
                    {
                        foreach (string line in mdns.Split('\n'))
                        {
                            string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                            if (fields.Length == 3 && fields[1].TrimEnd('.') == "_adb-tls-connect._tcp" &&
                                fields[0].TrimEnd('.') + "._adb-tls-connect._tcp" == network.Serial.TrimEnd('.') &&
                                WirelessAdbEndpoint.TryNormalize(fields[2], out string resolved)) { candidate = resolved; break; }
                        }
                    }
                }
                if (candidate is not null && candidate != _endpoint)
                {
                    await SaveAsync(candidate, cancellationToken).ConfigureAwait(false);
                    _endpoint = candidate;
                }
                return false;
            }
            // Do not disturb a ready USB session or an unauthorized transport.
            if (list.Devices.Any(device => device.Status is AndroidDeviceStatus.Ready or AndroidDeviceStatus.Unauthorized) || _endpoint is null) { return false; }
            long now = timestamp ?? Environment.TickCount64;
            if (now < _nextAttempt) { return false; }
            _nextAttempt = now + 10000;
            _runner ??= await createRunner(cancellationToken).ConfigureAwait(false);
            AdbCommandResult connect = await _runner.RunAsync(["connect", _endpoint], TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
            return connect.Succeeded && (connect.StandardOutput.StartsWith("connected to ", StringComparison.OrdinalIgnoreCase) ||
                connect.StandardOutput.StartsWith("already connected to ", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or AndroidConnectionException or
            InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Optional recovery must not replace authoritative discovery state.
            return false;
        }
    }

    private async ValueTask SaveAsync(string value, CancellationToken cancellationToken)
    {
        if (path is null) { return; }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, value, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Connection remains usable if its optional endpoint cache is unwritable.
        }
    }
}
