using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace VRPhoneScreenOverlay.SteamVR;

internal static class TranslationPngEncoder
{
    public static byte[] Encode(byte[] rgba, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if (rgba.Length != checked(width * height * 4)) { throw new ArgumentException("Invalid RGBA dimensions.", nameof(rgba)); }
        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(output, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true))
        {
            int stride = checked(width * 4);
            byte[] sub = new byte[stride];
            byte[] up = new byte[stride];
            try
            {
                for (int row = 0; row < height; row++)
                {
                    int offset = row * stride;
                    long noneScore = 0, subScore = 0, upScore = 0;
                    for (int x = 0; x < stride; x++)
                    {
                        byte value = rgba[offset + x];
                        sub[x] = unchecked((byte)(value - (x >= 4 ? rgba[offset + x - 4] : 0)));
                        up[x] = unchecked((byte)(value - (row > 0 ? rgba[offset + x - stride] : 0)));
                        noneScore += Math.Min((int)value, 256 - value);
                        subScore += Math.Min((int)sub[x], 256 - sub[x]);
                        upScore += Math.Min((int)up[x], 256 - up[x]);
                    }
                    if (subScore < noneScore && subScore <= upScore)
                    { zlib.WriteByte(1); zlib.Write(sub); }
                    else if (upScore < noneScore)
                    { zlib.WriteByte(2); zlib.Write(up); }
                    else
                    { zlib.WriteByte(0); zlib.Write(rgba, offset, stride); }
                }
            }
            finally { Array.Clear(sub); Array.Clear(up); }
        }
        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream stream, string name, byte[] bytes)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, bytes.Length);
        stream.Write(number);
        byte[] type = Encoding.ASCII.GetBytes(name);
        stream.Write(type);
        stream.Write(bytes);
        uint crc = uint.MaxValue;
        foreach (byte value in type) { crc = Update(crc, value); }
        foreach (byte value in bytes) { crc = Update(crc, value); }
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        stream.Write(number);
    }

    private static uint Update(uint crc, byte value)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++) { crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); }
        return crc;
    }
}
