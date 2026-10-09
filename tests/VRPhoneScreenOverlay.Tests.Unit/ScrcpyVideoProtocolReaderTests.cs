using System.Buffers.Binary;
using System.Text;
using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class ScrcpyVideoProtocolReaderTests
{
    [Fact]
    public async Task ParsesHandshakeRotationConfigurationAndKeyFrame()
    {
        using MemoryStream stream = new();
        byte[] name = new byte[64];
        Encoding.UTF8.GetBytes("Test Phone").CopyTo(name, 0);
        await stream.WriteAsync(name);
        byte[] codec = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(codec, 0x68323634);
        await stream.WriteAsync(codec);

        byte[] sessionHeader = new byte[12];
        sessionHeader[0] = 0x80;
        sessionHeader[3] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(sessionHeader.AsSpan(4, 4), 1080);
        BinaryPrimitives.WriteUInt32BigEndian(sessionHeader.AsSpan(8, 4), 2400);
        await stream.WriteAsync(sessionHeader);

        await WritePacketAsync(stream, 1UL << 62, [0x00, 0x00, 0x00, 0x01, 0x67]);
        await WritePacketAsync(stream, (1UL << 61) | 123_456, [0x00, 0x00, 0x00, 0x01, 0x65, 0x01]);
        stream.Position = 0;

        ScrcpyVideoProtocolReader reader = new(stream);
        ScrcpyVideoHandshake handshake = await reader.ReadHandshakeAsync(CancellationToken.None);
        using AndroidVideoStreamItem session = await reader.ReadNextAsync(CancellationToken.None);
        using AndroidVideoStreamItem configuration = await reader.ReadNextAsync(CancellationToken.None);
        using AndroidVideoStreamItem keyFrame = await reader.ReadNextAsync(CancellationToken.None);

        Assert.Equal("Test Phone", handshake.DeviceName);
        Assert.Equal(AndroidVideoCodec.H264, handshake.Codec);
        Assert.Equal(AndroidVideoStreamItemKind.Session, session.Kind);
        Assert.Equal(1080, session.Width);
        Assert.Equal(2400, session.Height);
        Assert.True(session.ClientResized);
        Assert.Equal(AndroidVideoStreamItemKind.Configuration, configuration.Kind);
        Assert.Null(configuration.PresentationTimeMicroseconds);
        Assert.Equal(5, configuration.Payload.Length);
        Assert.Equal(AndroidVideoStreamItemKind.Media, keyFrame.Kind);
        Assert.Equal(123_456, keyFrame.PresentationTimeMicroseconds);
        Assert.True(keyFrame.IsKeyFrame);
        Assert.Equal(6, keyFrame.Payload.Length);
    }

    [Fact]
    public async Task RejectsOversizedMediaPacketBeforeAllocation()
    {
        using MemoryStream stream = new();
        byte[] header = new byte[12];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8, 4), 16 * 1024 * 1024 + 1);
        await stream.WriteAsync(header);
        stream.Position = 0;
        ScrcpyVideoProtocolReader reader = new(stream);

        AndroidConnectionException exception = await Assert.ThrowsAsync<AndroidConnectionException>(
            () => reader.ReadNextAsync(CancellationToken.None).AsTask());

        Assert.Equal("ANDROID_VIDEO_PACKET_SIZE_INVALID", exception.ReasonCode);
    }

    private static async ValueTask WritePacketAsync(
        Stream stream,
        ulong presentationTimeAndFlags,
        byte[] payload)
    {
        byte[] header = new byte[12];
        BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(0, 8), presentationTimeAndFlags);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8, 4), checked((uint)payload.Length));
        await stream.WriteAsync(header);
        await stream.WriteAsync(payload);
    }
}
