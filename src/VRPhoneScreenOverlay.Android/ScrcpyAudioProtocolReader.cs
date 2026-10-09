using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace VRPhoneScreenOverlay.Android;

internal sealed class ScrcpyAudioProtocolReader(Stream stream)
{
    private const int _deviceNameFieldLength = 64;
    private const int _packetHeaderLength = 12;
    private const int _maximumPacketLength = 1024 * 1024;
    private const ulong _configurationFlag = 1UL << 62;
    private const ulong _presentationTimeMask = (1UL << 61) - 1;
    private readonly Stream _stream = stream ?? throw new ArgumentNullException(nameof(stream));

    public async ValueTask<ScrcpyAudioHandshake> ReadHandshakeAsync(
        CancellationToken cancellationToken)
    {
        byte[] nameBuffer = new byte[_deviceNameFieldLength];
        await _stream.ReadExactlyAsync(nameBuffer, cancellationToken).ConfigureAwait(false);
        int terminator = Array.IndexOf(nameBuffer, (byte)0);
        string deviceName = Encoding.UTF8.GetString(
            nameBuffer,
            0,
            terminator < 0 ? nameBuffer.Length : terminator);

        byte[] codecBuffer = new byte[sizeof(uint)];
        await _stream.ReadExactlyAsync(codecBuffer, cancellationToken).ConfigureAwait(false);
        uint rawCodec = BinaryPrimitives.ReadUInt32BigEndian(codecBuffer);
        AndroidAudioCodec codec = rawCodec switch
        {
            0x6f707573 => AndroidAudioCodec.Opus,
            0x00616163 => AndroidAudioCodec.Aac,
            0x666c6163 => AndroidAudioCodec.Flac,
            0x00726177 => AndroidAudioCodec.Raw,
            0 => throw new AndroidConnectionException(
                AndroidReasonCodes.AudioDisabled,
                "此 Android 版本或当前应用不允许捕获内部音频"),
            1 => throw new AndroidConnectionException(
                AndroidReasonCodes.AudioConfigurationFailed,
                "手机内部音频编码器配置失败"),
            _ => throw new AndroidConnectionException(
                AndroidReasonCodes.AudioCodecUnknown,
                $"手机返回了不支持的音频编码标识 0x{rawCodec:x8}"),
        };
        return new ScrcpyAudioHandshake(deviceName, codec);
    }

    public async ValueTask<AndroidAudioStreamItem> ReadNextAsync(
        CancellationToken cancellationToken)
    {
        byte[] header = new byte[_packetHeaderLength];
        await _stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        ulong presentationTimeAndFlags = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(0, 8));
        uint packetLengthRaw = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8, 4));
        if (packetLengthRaw is 0 or > _maximumPacketLength)
        {
            throw new AndroidConnectionException(
                AndroidReasonCodes.AudioPacketSizeInvalid,
                $"手机返回了无效音频包长度 {packetLengthRaw}");
        }

        int packetLength = checked((int)packetLengthRaw);
        IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(packetLength);
        try
        {
            Memory<byte> payload = owner.Memory[..packetLength];
            await _stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
            bool configuration = (presentationTimeAndFlags & _configurationFlag) != 0;
            long? presentationTime = configuration
                ? null
                : checked((long)(presentationTimeAndFlags & _presentationTimeMask));
            AndroidAudioStreamItem result = new(
                configuration
                    ? AndroidAudioStreamItemKind.Configuration
                    : AndroidAudioStreamItemKind.Media,
                presentationTime,
                payload,
                owner);
            owner = null!;
            return result;
        }
        finally
        {
            owner?.Dispose();
        }
    }
}

internal sealed record ScrcpyAudioHandshake(string DeviceName, AndroidAudioCodec Codec);
