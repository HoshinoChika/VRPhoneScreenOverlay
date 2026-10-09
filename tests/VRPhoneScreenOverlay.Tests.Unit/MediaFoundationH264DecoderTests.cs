using System.Buffers;
using VRPhoneScreenOverlay.Media;
using VRPhoneScreenOverlay.SteamVR;

namespace VRPhoneScreenOverlay.Tests.Unit;

public sealed class MediaFoundationH264DecoderTests
{
    private const string _tenFrameBlackH264 =
        "AAAAAQkQAAAAAWdCwAraEJsBEAAAAwAQAAADA8jxImoAAAABaM4PyAAAAQYF//9O3EXpvebZSLeWLNgg2SPu73gyNjQgLSBjb3JlIDE2NCByMzEwNiBlYWE2OGZhIC0gSC4yNjQvTVBFRy00IEFWQyBjb2RlYyAtIENvcHlsZWZ0IDIwMDMtMjAyMyAtIGh0dHA6Ly93d3cudmlkZW9sYW4ub3JnL3gyNjQuaHRtbCAtIG9wdGlvbnM6IGNhYmFjPTAgcmVmPTEgZGVibG9jaz0wOjA6MCBhbmFseXNlPTA6MCBtZT1kaWEgc3VibWU9MCBwc3k9MSBwc3lfcmQ9MS4wMDowLjAwIG1peGVkX3JlZj0wIG1lX3JhbmdlPTE2IGNocm9tYV9tZT0xIHRyZWxsaXM9MCA4eDhkY3Q9MCBjcW09MCBkZWFkem9uZT0yMSwxMSBmYXN0X3Bza2lwPTEgY2hyb21hX3FwX29mZnNldD0wIHRocmVhZHM9MSBsb29rYWhlYWRfdGhyZWFkcz0xIHNsaWNlZF90aHJlYWRzPTAgbnI9MCBkZWNpbWF0ZT0xIGludGVybGFjZWQ9MCBibHVyYXlfY29tcGF0PTAgY29uc3RyYWluZWRfaW50cmE9MCBiZnJhbWVzPTAgd2VpZ2h0cD0wIGtleWludD0zMCBrZXlpbnRfbWluPTMgc2NlbmVjdXQ9MCBpbnRyYV9yZWZyZXNoPTAgcmM9Y3JmIG1idHJlZT0wIGNyZj0yMy4wIHFjb21wPTAuNjAgcXBtaW49MCBxcG1heD02OSBxcHN0ZXA9NCBpcF9yYXRpbz0xLjQwIGFxPTAAgAAAAWWIhDomKAAJAsnJyddddddddddddeAAAAABCTAAAAFBmiA2gjAAAAABCTAAAAFBmkA6gjAAAAABCTAAAAFBmmA6gjAAAAABCTAAAAFBmoA6gjAAAAABCTAAAAFBmqA6gjAAAAABCTAAAAFBmsA+gjAAAAABCTAAAAFBmuA+gjAAAAABCTAAAAFBmwA+gjAAAAABCTAAAAFBmyA+gjA=";

    [Fact]
    public void DecodesAnnexBIntoNv12WithoutDesktopCapture()
    {
        List<NalUnit> units = ParseAnnexB(Convert.FromBase64String(_tenFrameBlackH264));
        byte[] configuration = Concat(units.Where(unit => unit.Type is 7 or 8));
        List<byte[]> accessUnits = CreateAccessUnits(units);
        Assert.NotEmpty(configuration);
        Assert.Equal(10, accessUnits.Count);

        using IH264VideoDecoder decoder = VideoDecoderFactory.CreateMediaFoundationH264(
            64,
            64,
            30,
            configuration);
        DecodedVideoFrame? decoded = null;
        for (int index = 0; index < accessUnits.Count && decoded is null; index++)
        {
            decoded = decoder.DecodePacket(
                accessUnits[index],
                index * 33_333L,
                keyFrame: index == 0);
        }

        using (decoded)
        {
            Assert.True(decoded is not null, $"Decoder {decoder.BackendName} returned no frames.");
            Assert.Equal(VideoPixelFormat.Nv12, decoded.PixelFormat);
            Assert.Equal(64, decoded.Width);
            Assert.Equal(64, decoded.Height);
            Assert.Equal(64, decoded.VisibleWidth);
            Assert.Equal(64, decoded.VisibleHeight);
            Assert.True(decoded.Stride >= 64);
            Assert.True(decoded.Pixels.Length >= 64 * 64 * 3 / 2);

            using IGpuVideoFramePresenter presenter = GpuVideoFramePresenterFactory.CreateD3D11();
            GpuVideoFrame first = presenter.Present(decoded);
            GpuVideoFrame second = presenter.Present(decoded);
            GpuVideoFrame third = presenter.Present(decoded);
            GpuVideoFrame fourth = presenter.Present(decoded);
            Assert.NotEqual(nint.Zero, first.NativeTexturePointer);
            Assert.Equal([0, 1, 2, 0], [first.TextureSlot, second.TextureSlot, third.TextureSlot, fourth.TextureSlot]);
            Assert.Equal(first.NativeTexturePointer, fourth.NativeTexturePointer);
            Assert.Equal(3, new[]
            {
                first.NativeTexturePointer,
                second.NativeTexturePointer,
                third.NativeTexturePointer,
            }.Distinct().Count());
        }
    }

    [Fact]
    public void CropsDecoderAlignmentPaddingBeforePublishingGpuTexture()
    {
        const int codedWidth = 64;
        const int codedHeight = 64;
        const int visibleWidth = 62;
        const int visibleHeight = 60;
        const int length = codedWidth * codedHeight * 3 / 2;
        IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(length);
        Span<byte> pixels = owner.Memory.Span[..length];
        pixels[..(codedWidth * codedHeight)].Fill(16);
        pixels[(codedWidth * codedHeight)..].Fill(128);
        using DecodedVideoFrame frame = new(
            codedWidth,
            codedHeight,
            codedWidth,
            visibleWidth,
            visibleHeight,
            VideoPixelFormat.Nv12,
            0,
            owner,
            length);

        using IGpuVideoFramePresenter presenter = GpuVideoFramePresenterFactory.CreateD3D11();
        GpuVideoFrame presented = presenter.Present(frame);

        Assert.Equal(visibleWidth, presented.Width);
        Assert.Equal(visibleHeight, presented.Height);
        Assert.NotEqual(nint.Zero, presented.NativeTexturePointer);
    }

    private static List<NalUnit> ParseAnnexB(byte[] stream)
    {
        List<(int Offset, int PrefixLength)> starts = new();
        for (int index = 0; index <= stream.Length - 3; index++)
        {
            if (index <= stream.Length - 4 &&
                stream[index] == 0 && stream[index + 1] == 0 && stream[index + 2] == 0 &&
                stream[index + 3] == 1)
            {
                starts.Add((index, 4));
                index += 3;
            }
            else if (stream[index] == 0 && stream[index + 1] == 0 && stream[index + 2] == 1)
            {
                starts.Add((index, 3));
                index += 2;
            }
        }

        List<NalUnit> units = new();
        for (int index = 0; index < starts.Count; index++)
        {
            (int start, int prefixLength) = starts[index];
            int end = index + 1 < starts.Count ? starts[index + 1].Offset : stream.Length;
            if (end - start <= prefixLength)
            {
                continue;
            }

            units.Add(new NalUnit(stream[start + prefixLength] & 0x1f, stream[start..end]));
        }

        return units;
    }

    private static byte[] Concat(IEnumerable<NalUnit> units)
    {
        byte[][] selected = units.Select(unit => unit.Bytes).ToArray();
        byte[] result = new byte[selected.Sum(bytes => bytes.Length)];
        int offset = 0;
        foreach (byte[] bytes in selected)
        {
            bytes.CopyTo(result, offset);
            offset += bytes.Length;
        }

        return result;
    }

    private static List<byte[]> CreateAccessUnits(IEnumerable<NalUnit> units)
    {
        List<byte[]> result = new();
        List<NalUnit> current = new();
        foreach (NalUnit unit in units)
        {
            if (unit.Type == 9)
            {
                if (current.Count > 0)
                {
                    result.Add(Concat(current));
                    current.Clear();
                }

                continue;
            }

            if (unit.Type is not 7 and not 8)
            {
                current.Add(unit);
            }
        }

        if (current.Count > 0)
        {
            result.Add(Concat(current));
        }

        return result;
    }

    private sealed record NalUnit(int Type, byte[] Bytes);
}
