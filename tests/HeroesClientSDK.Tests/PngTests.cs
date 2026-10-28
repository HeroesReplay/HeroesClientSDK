using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using Xunit;

namespace HeroesClientSDK.Tests;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class PngTests
{
    [Fact]
    public void Encode_WritesRgbRowsOfTheBgraPixels()
    {
        // 2x1: a red pixel and a blue one, as GetDIBits returns them (B, G, R, unused).
        byte[] bgra = [0x00, 0x00, 0xFF, 0x00, 0xFF, 0x00, 0x00, 0x00];

        byte[] png = Png.Encode(bgra, 2, 1);

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(png, 12, 4));
        Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
        Assert.Equal(1, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
        Assert.Equal(8, png[24]);
        Assert.Equal(2, png[25]);
        // IHDR's CRC over "IHDR" and its 13 bytes.
        Assert.Equal(0x7B40E8DDu, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(29)));

        int idat = 33;
        int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(idat));
        Assert.Equal("IDAT", System.Text.Encoding.ASCII.GetString(png, idat + 4, 4));
        using var zlib = new ZLibStream(
            new MemoryStream(png, idat + 8, length),
            CompressionMode.Decompress
        );
        using var raw = new MemoryStream();
        zlib.CopyTo(raw);
        Assert.Equal(new byte[] { 0, 0xFF, 0, 0, 0, 0, 0xFF }, raw.ToArray());
        Assert.Equal("IEND", System.Text.Encoding.ASCII.GetString(png, png.Length - 8, 4));
    }

    [Fact]
    public void IsMostlyBlack_BlackFrame_IsTrue()
    {
        Assert.True(Png.IsMostlyBlack(new byte[400 * 4]));
    }

    [Fact]
    public void IsMostlyBlack_TwoPercentLit_IsFalse()
    {
        byte[] bgra = new byte[100 * 4];
        bgra[0] = 200;
        bgra[4] = 200;

        Assert.False(Png.IsMostlyBlack(bgra));
    }
}
