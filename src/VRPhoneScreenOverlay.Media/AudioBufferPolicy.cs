namespace VRPhoneScreenOverlay.Media;

public static class AudioBufferPolicy
{
    public static readonly TimeSpan MaximumBufferedDuration = TimeSpan.FromMilliseconds(160);

    public static bool ShouldReset(TimeSpan bufferedDuration) =>
        bufferedDuration > MaximumBufferedDuration;
}
