namespace VRPhoneScreenOverlay.Media;

public static class AudioPipelineFactory
{
    public static IAudioDecoder CreateOpusDecoder() => new OpusAudioDecoder();

    public static ValueTask<IAudioSink> CreateDefaultWasapiSinkAsync() =>
        CreateDefaultWasapiSinkCoreAsync();

    public static async ValueTask<IAudioSink> CreateRegulatedWasapiSinkAsync() =>
        await WasapiAudioSink.CreateAsync(regulateClock: true).ConfigureAwait(false);

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
