using System.Buffers.Binary;
using System.Text;
using VRPhoneScreenOverlay.Android;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class ScrcpyAudioProtocolReaderTests
{
    [Fact]
    public async Task ReadsOpusHandshakeAndTimestampedPacket()
    {
        byte[] stream = new byte[64 + 4 + 12 + 3];
        Encoding.UTF8.GetBytes("Test Phone").CopyTo(stream, 0);
        BinaryPrimitives.WriteUInt32BigEndian(stream.AsSpan(64, 4), 0x6f707573);
        BinaryPrimitives.WriteUInt64BigEndian(stream.AsSpan(68, 8), 123_456);
        BinaryPrimitives.WriteUInt32BigEndian(stream.AsSpan(76, 4), 3);
        stream[^3] = 1;
        stream[^2] = 2;
        stream[^1] = 3;
        ScrcpyAudioProtocolReader reader = new(new MemoryStream(stream));

        ScrcpyAudioHandshake handshake = await reader.ReadHandshakeAsync(CancellationToken.None);
        using AndroidAudioStreamItem packet = await reader.ReadNextAsync(CancellationToken.None);

        Assert.Equal("Test Phone", handshake.DeviceName);
        Assert.Equal(AndroidAudioCodec.Opus, handshake.Codec);
        Assert.Equal(AndroidAudioStreamItemKind.Media, packet.Kind);
        Assert.Equal(123_456, packet.PresentationTimeMicroseconds);
        Assert.Equal([1, 2, 3], packet.Payload.ToArray());
    }
}
