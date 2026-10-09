namespace VRPhoneScreenOverlay.Android;

public sealed record AndroidSessionFactories(
    IAndroidVideoSessionFactory Video,
    IAndroidAudioSessionFactory Audio,
    IAndroidControlSessionFactory Control);

public static class AndroidConnectionFactory
{
    public static IAndroidConnectionLogSink CreateDefaultLog()
    {
        try
        {
            return new JsonLineAndroidConnectionLog();
        }
        catch (UnauthorizedAccessException)
        {
            return NullAndroidConnectionLog.Instance;
        }
        catch (IOException)
        {
            return NullAndroidConnectionLog.Instance;
        }
    }

    public static IAndroidConnectionService CreateDefault(IAndroidConnectionLogSink logSink)
    {
        ArgumentNullException.ThrowIfNull(logSink);
        string resources = Path.Combine(
            AppContext.BaseDirectory,
            "resources",
            "android-platform-tools");
        return new AndroidConnectionService(
            resources,
            logSink,
            new AndroidConnectionServiceOptions
            {
                ConnectionPreferencesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VRPhoneScreenOverlay", "connections.json"),
                WirelessReconnectPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VRPhoneScreenOverlay", "wireless-endpoint.txt"),
            });
    }

    public static AndroidSessionFactories CreateDefaultSessionFactories(
        IAndroidConnectionService connection,
        IAndroidConnectionLogSink logSink)
    {
        AndroidConnectionService service = RequireDefaultConnection(connection);
        ArgumentNullException.ThrowIfNull(logSink);

        string resources = Path.Combine(AppContext.BaseDirectory, "resources");
        string toolPath = Path.Combine(resources, "android-platform-tools", "adb.exe");
        string serverPath = Path.Combine(resources, "scrcpy", "scrcpy-server-v4.1");
        AdbCommandRunner commandRunner = new(toolPath);
        ScrcpySessionLauncher launcher = new(commandRunner, serverPath, logSink);
        return new AndroidSessionFactories(
            new ScrcpyVideoSessionFactory(service, launcher, logSink),
            new ScrcpyAudioSessionFactory(service, launcher, logSink),
            new ScrcpyControlSessionFactory(service, launcher, logSink));
    }

    public static IAndroidVideoSessionFactory CreateDefaultVideoSessions(
        IAndroidConnectionService connection,
        IAndroidConnectionLogSink logSink) =>
        CreateDefaultSessionFactories(connection, logSink).Video;

    public static IAndroidVideoProbeService CreateDefaultVideoProbe(
        IAndroidConnectionService connection,
        IAndroidConnectionLogSink logSink) =>
        new AndroidVideoProbeService(CreateDefaultVideoSessions(connection, logSink));

    public static IAndroidControlSessionFactory CreateDefaultControlSessions(
        IAndroidConnectionService connection,
        IAndroidConnectionLogSink logSink) =>
        CreateDefaultSessionFactories(connection, logSink).Control;

    public static IAndroidAudioSessionFactory CreateDefaultAudioSessions(
        IAndroidConnectionService connection,
        IAndroidConnectionLogSink logSink) =>
        CreateDefaultSessionFactories(connection, logSink).Audio;

    public static IAndroidMediaPlaybackControl CreateDefaultMediaPlaybackControl(
        IAndroidConnectionService connection,
        IAndroidConnectionLogSink logSink)
    {
        AndroidConnectionService service = RequireDefaultConnection(connection);
        ArgumentNullException.ThrowIfNull(logSink);
        string toolPath = Path.Combine(
            AppContext.BaseDirectory,
            "resources",
            "android-platform-tools",
            "adb.exe");
        return new AndroidMediaPlaybackControl(service, new AdbCommandRunner(toolPath), logSink);
    }

    public static IAndroidKeyguardStateService CreateDefaultKeyguardStateService(
        IAndroidConnectionService connection)
    {
        AndroidConnectionService service = RequireDefaultConnection(connection);
        string toolPath = Path.Combine(
            AppContext.BaseDirectory,
            "resources",
            "android-platform-tools",
            "adb.exe");
        return new AndroidKeyguardStateService(service, new AdbCommandRunner(toolPath));
    }

    private static AndroidConnectionService RequireDefaultConnection(
        IAndroidConnectionService connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (connection is not AndroidConnectionService service)
        {
            throw new ArgumentException(
                "The operation requires the default Android connection service.",
                nameof(connection));
        }

        return service;
    }
}

internal sealed record AndroidConnectionServiceOptions
{
    public string? ConnectionPreferencesPath { get; init; }
    public string? WirelessReconnectPath { get; init; }

    public TimeSpan ReadyScanInterval { get; init; } = TimeSpan.FromSeconds(3);

    public TimeSpan SearchingScanInterval { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan FaultedScanInterval { get; init; } = TimeSpan.FromSeconds(5);

    // A cold ADB daemon start can take longer than five seconds on Windows.
    // Keep the operation bounded without killing the daemon while it is still booting.
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(15);
}
