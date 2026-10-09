namespace VRPhoneScreenOverlay.Media;

/// <summary>
/// Stable reason codes raised by this module. Values are protocol: they are only ever
/// added, never re-spelled, and never localised.
/// </summary>
public static class MediaReasonCodes
{
    /// <summary><c>AUDIO_CODEC_UNSUPPORTED</c></summary>
    public const string AudioCodecUnsupported = "AUDIO_CODEC_UNSUPPORTED";

    /// <summary><c>AUDIO_OPUS_DECODE_FAILED</c></summary>
    public const string AudioOpusDecodeFailed = "AUDIO_OPUS_DECODE_FAILED";

    /// <summary><c>AUDIO_OUTPUT_UNAVAILABLE</c></summary>
    public const string AudioOutputUnavailable = "AUDIO_OUTPUT_UNAVAILABLE";

    /// <summary><c>AUDIO_PIPELINE_FAILED</c></summary>
    public const string AudioPipelineFailed = "AUDIO_PIPELINE_FAILED";

    /// <summary><c>AUDIO_PLAYING</c></summary>
    public const string AudioPlaying = "AUDIO_PLAYING";

    /// <summary><c>AUDIO_RECOVERY_EXHAUSTED</c></summary>
    public const string AudioRecoveryExhausted = "AUDIO_RECOVERY_EXHAUSTED";

    /// <summary><c>AUDIO_START_ENDED</c></summary>
    public const string AudioStartEnded = "AUDIO_START_ENDED";

    /// <summary><c>AUDIO_START_TIMEOUT</c></summary>
    public const string AudioStartTimeout = "AUDIO_START_TIMEOUT";

    /// <summary><c>AUDIO_STARTING</c></summary>
    public const string AudioStarting = "AUDIO_STARTING";

    /// <summary><c>AUDIO_STOPPED</c></summary>
    public const string AudioStopped = "AUDIO_STOPPED";

    /// <summary><c>AUDIO_STOPPING</c></summary>
    public const string AudioStopping = "AUDIO_STOPPING";

    /// <summary><c>AUDIO_STREAM_ENDED</c></summary>
    public const string AudioStreamEnded = "AUDIO_STREAM_ENDED";

    /// <summary><c>MEDIA_H264_DECODE_FAILED</c></summary>
    public const string H264DecodeFailed = "MEDIA_H264_DECODE_FAILED";

    /// <summary><c>MEDIA_H264_DECODER_NOT_FOUND</c></summary>
    public const string H264DecoderNotFound = "MEDIA_H264_DECODER_NOT_FOUND";

    /// <summary><c>MEDIA_H264_DRAIN_FAILED</c></summary>
    public const string H264DrainFailed = "MEDIA_H264_DRAIN_FAILED";

    /// <summary><c>MEDIA_H264_INITIALIZATION_FAILED</c></summary>
    public const string H264InitializationFailed = "MEDIA_H264_INITIALIZATION_FAILED";

    /// <summary><c>MEDIA_H264_NV12_NOT_SUPPORTED</c></summary>
    public const string H264Nv12NotSupported = "MEDIA_H264_NV12_NOT_SUPPORTED";

    /// <summary><c>MEDIA_H264_OUTPUT_EMPTY</c></summary>
    public const string H264OutputEmpty = "MEDIA_H264_OUTPUT_EMPTY";

    /// <summary><c>MEDIA_H264_OUTPUT_MISSING</c></summary>
    public const string H264OutputMissing = "MEDIA_H264_OUTPUT_MISSING";

    /// <summary><c>AUDIO_NOT_STARTED</c> — 手机音频从未启动，与已停止区分。</summary>
    public const string AudioNotStarted = "AUDIO_NOT_STARTED";

    /// <summary><c>AUDIO_START_CANCELLED</c> — 手机音频启动被取消。</summary>
    public const string AudioStartCancelled = "AUDIO_START_CANCELLED";
}
