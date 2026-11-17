namespace VRPhoneScreenOverlay.Android;

public interface IAndroidImageTransfer
{
    public Task SendAsync(string deviceKey, byte[] png, CancellationToken token);
}

public sealed class AndroidImageTransfer(IAndroidConnectionService connection) : IAndroidImageTransfer
{
    public async Task SendAsync(string deviceKey, byte[] png, CancellationToken token)
    {
        if (png.Length is < 8 or > 8 * 1024 * 1024 || !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        { throw new InvalidDataException("SCREENSHOT_INVALID"); }
        if (connection is not AndroidConnectionService service || !service.TryResolveReadyDevice(deviceKey, out var target))
        { throw new InvalidOperationException("SCREENSHOT_PHONE_UNAVAILABLE"); }
        await TransferAsync(service.CreateCommandRunner(), target.Serial,
            () => service.TryResolveReadyDevice(deviceKey, out var current) && current.Serial == target.Serial &&
                current.SessionEpoch == target.SessionEpoch && connection.Snapshot.SelectedDevice?.DeviceKey == deviceKey,
            png, token).ConfigureAwait(false);
    }

    public async Task SendPhotoAsync(string deviceKey, string path, CancellationToken token)
    {
        await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (source.Length is < 20 or > 128 * 1024 * 1024)
        { throw new InvalidDataException("PHOTO_INVALID"); }
        byte[] png = new byte[(int)source.Length];
        await source.ReadExactlyAsync(png, token).ConfigureAwait(false);
        try
        {
            if (!png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
                !png.AsSpan(png.Length - 12).SequenceEqual(new byte[] { 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 }))
            { throw new InvalidDataException("PHOTO_INVALID"); }
            if (connection is not AndroidConnectionService service || !service.TryResolveReadyDevice(deviceKey, out var target))
            { throw new InvalidOperationException("SCREENSHOT_PHONE_UNAVAILABLE"); }
            await TransferAsync(service.CreateCommandRunner(), target.Serial,
                () => service.TryResolveReadyDevice(deviceKey, out var current) && current.Serial == target.Serial &&
                    current.SessionEpoch == target.SessionEpoch && connection.Snapshot.SelectedDevice?.DeviceKey == deviceKey,
                png, token, Path.GetFileNameWithoutExtension(path)).ConfigureAwait(false);
        }
        finally { Array.Clear(png); }
    }

    internal static async Task TransferAsync(IAdbCommandRunner runner, string serial, Func<bool> isCurrent, byte[] png, CancellationToken token, string? photoName = null)
    {
        string name = photoName ?? "VRPSO-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8];
        string local = Path.Combine(Path.GetTempPath(), name + ".png");
        string remote = "/sdcard/Pictures/VRPSO/" + name + ".png";
        string temporary = "/sdcard/Pictures/VRPSO/." + name + ".part";
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await File.WriteAllBytesAsync(local, png, deadline.Token).ConfigureAwait(false);
            await Run(["shell", "mkdir", "-p", "/sdcard/Pictures/VRPSO"]);
            await Run(["push", local, temporary]);
            await Run(["shell", "mv", temporary, remote]);
            await Run(["shell", "am", "broadcast", "-a", "android.intent.action.MEDIA_SCANNER_SCAN_FILE", "-d", "file://" + remote]);
        }
        finally
        {
            try { File.Delete(local); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            // Only the generated partial path on the pinned endpoint is removed.
            try { await runner.RunAsync(["-s", serial, "shell", "rm", "-f", temporary], TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or InvalidOperationException or AndroidConnectionException) { }
        }

        async Task Run(string[] arguments)
        {
            deadline.Token.ThrowIfCancellationRequested();
            if (!isCurrent())
            { throw new InvalidOperationException("SCREENSHOT_PHONE_CHANGED"); }
            var result = await runner.RunAsync(["-s", serial, .. arguments], TimeSpan.FromSeconds(20), deadline.Token).ConfigureAwait(false);
            if (!result.Succeeded) { throw new IOException("SCREENSHOT_TRANSFER_FAILED"); }
            if (arguments.Contains("broadcast") && !result.StandardOutput.Contains("Broadcast completed", StringComparison.Ordinal))
            { throw new IOException("SCREENSHOT_SCAN_FAILED"); }
        }
    }
}
