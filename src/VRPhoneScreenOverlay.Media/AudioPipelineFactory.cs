namespace VRPhoneScreenOverlay.Media;

public static class AudioPipelineFactory
{
    public static IAudioDecoder CreateOpusDecoder() => new OpusAudioDecoder();

    public static ValueTask<IAudioSink> CreateDefaultWasapiSinkAsync() =>
        CreateDefaultWasapiSinkCoreAsync();

    private static async ValueTask<IAudioSink> CreateDefaultWasapiSinkCoreAsync() =>
        await WasapiAudioSink.CreateAsync().ConfigureAwait(false);
}

public sealed class AudioPipelineException(
    string reasonCode,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string ReasonCode { get; } = reasonCode;
}
