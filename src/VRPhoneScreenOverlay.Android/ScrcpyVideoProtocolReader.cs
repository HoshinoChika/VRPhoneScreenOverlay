using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace VRPhoneScreenOverlay.Android;

internal sealed class ScrcpyVideoProtocolReader(Stream stream)
{
    private const int _deviceNameFieldLength = 64;
    private const int _packetHeaderLength = 12;
    private const int _maximumPacketLength = 16 * 1024 * 1024;
    private const ulong _configurationFlag = 1UL << 62;
    private const ulong _keyFrameFlag = 1UL << 61;
    private const ulong _presentationTimeMask = _keyFrameFlag - 1;
    private readonly Stream _stream = stream ?? throw new ArgumentNullException(nameof(stream));

    public async ValueTask<ScrcpyVideoHandshake> ReadHandshakeAsync(
        CancellationToken cancellationToken)
    {
        byte[] deviceNameBuffer = new byte[_deviceNameFieldLength];
        await _stream.ReadExactlyAsync(deviceNameBuffer, cancellationToken).ConfigureAwait(false);
        int terminator = Array.IndexOf(deviceNameBuffer, (byte)0);
        int nameLength = terminator < 0 ? _deviceNameFieldLength : terminator;
        string deviceName = Encoding.UTF8.GetString(deviceNameBuffer, 0, nameLength);

        byte[] codecBuffer = new byte[sizeof(uint)];
        await _stream.ReadExactlyAsync(codecBuffer, cancellationToken).ConfigureAwait(false);
        uint rawCodec = BinaryPrimitives.ReadUInt32BigEndian(codecBuffer);
        AndroidVideoCodec codec = rawCodec switch
        {
            0x68323634 => AndroidVideoCodec.H264,
            0x68323635 => AndroidVideoCodec.H265,
            0x00617631 => AndroidVideoCodec.Av1,
            0x00767038 => AndroidVideoCodec.Vp8,
            0x00767039 => AndroidVideoCodec.Vp9,
            0 => throw new AndroidConnectionException(
                AndroidReasonCodes.VideoDisabled,
                "手机关闭了视频流"),
            1 => throw new AndroidConnectionException(
                AndroidReasonCodes.VideoConfigurationFailed,
                "手机视频编码器配置失败"),
            _ => throw new AndroidConnectionException(
                AndroidReasonCodes.VideoCodecUnknown,
                $"手机返回了不支持的视频编码标识 0x{rawCodec:x8}"),
        };
        return new ScrcpyVideoHandshake(deviceName, codec);
    }

    public async ValueTask<AndroidVideoStreamItem> ReadNextAsync(
        CancellationToken cancellationToken)
    {
        byte[] header = new byte[_packetHeaderLength];
        await _stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        if ((header[0] & 0x80) != 0)
        {
            int width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4, 4)));
            int height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8, 4)));
            if (width is < 1 or > 16_384 || height is < 1 or > 16_384)
            {
                throw new AndroidConnectionException(
                    AndroidReasonCodes.VideoSizeInvalid,
                    $"手机返回了无效视频尺寸 {width}x{height}");
            }

            return new AndroidVideoStreamItem(
                AndroidVideoStreamItemKind.Session,
                width,
                height,
                (header[3] & 1) != 0,
                null,
                false,
                ReadOnlyMemory<byte>.Empty,
                null);
        }

        ulong presentationTimeAndFlags = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(0, 8));
        uint packetLengthRaw = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8, 4));
        if (packetLengthRaw is 0 or > _maximumPacketLength)
        {
            throw new AndroidConnectionException(
                AndroidReasonCodes.VideoPacketSizeInvalid,
                $"手机返回了无效视频包长度 {packetLengthRaw}");
        }

        int packetLength = checked((int)packetLengthRaw);
        IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(packetLength);
        try
        {
            Memory<byte> payload = owner.Memory[..packetLength];
            await _stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
            bool configuration = (presentationTimeAndFlags & _configurationFlag) != 0;
            bool keyFrame = (presentationTimeAndFlags & _keyFrameFlag) != 0;
            long? presentationTime = configuration
                ? null
                : checked((long)(presentationTimeAndFlags & _presentationTimeMask));
            return new AndroidVideoStreamItem(
                configuration
                    ? AndroidVideoStreamItemKind.Configuration
                    : AndroidVideoStreamItemKind.Media,
                0,
                0,
                false,
                presentationTime,
                keyFrame,
                payload,
                owner);
        }
        catch
        {
            owner.Dispose();
            throw;
        }
    }
}

internal sealed record ScrcpyVideoHandshake(
    string DeviceName,
    AndroidVideoCodec Codec);
