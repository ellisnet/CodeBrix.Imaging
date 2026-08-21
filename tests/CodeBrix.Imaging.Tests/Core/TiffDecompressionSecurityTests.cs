using CodeBrix.Imaging.Formats.Tiff;
using CodeBrix.Imaging.Formats.Tiff.Compression.Decompressors;
using CodeBrix.Imaging.Formats.Tiff.Constants;
using CodeBrix.Imaging.IO;
using CodeBrix.Imaging.PixelFormats;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace CodeBrix.Imaging.Tests.Core;

/// <summary>
/// Security regression tests for the TIFF decompressors. Every case here feeds hostile or
/// malformed compressed data and asserts the decoder reports it as a catchable
/// <see cref="ImageFormatException"/> rather than overrunning a buffer or leaking a raw
/// <see cref="IndexOutOfRangeException"/> / <see cref="ArgumentException"/> to the caller.
/// </summary>
public class TiffDecompressionSecurityTests
{
    private readonly ITestOutputHelper _output;

    public TiffDecompressionSecurityTests(ITestOutputHelper output)
        => _output = output ?? throw new ArgumentNullException(nameof(output));

    #region LZW: LzwString.WriteTo ignores the write offset

    [Fact]
    public void LzwString_WriteTo_MultiByteRunNearBufferEnd_DoesNotOverrun()
    {
        // Arrange - a 4 byte code word. The bounds check only compared the code word length
        // against buffer.Length while the write actually lands at buffer[offset + i], so a run
        // starting near the tail overruns the destination.
        var str = new LzwString(1).Concatenate(2).Concatenate(3).Concatenate(4);
        Assert.Equal(4, str.Length);

        var buffer = new byte[4];

        // Act & Assert - offset 2 + length 4 needs 6 bytes of room in a 4 byte buffer.
        var ex = Assert.ThrowsAny<Exception>(() => str.WriteTo(buffer, 2));
        _output.WriteLine($"WriteTo(offset: 2, len: 4) into byte[4] threw {ex.GetType().Name}");
        Assert.IsAssignableFrom<ImageFormatException>(ex);
    }

    [Fact]
    public void LzwString_WriteTo_MultiByteRunFittingExactly_Succeeds()
    {
        // Arrange - the same run, this time with exactly enough room at the offset.
        var str = new LzwString(1).Concatenate(2).Concatenate(3).Concatenate(4);
        var buffer = new byte[6];

        // Act
        var written = str.WriteTo(buffer, 2);

        // Assert - valid data must still decode untouched by the new bounds check.
        Assert.Equal(4, written);
        Assert.Equal(new byte[] { 0, 0, 1, 2, 3, 4 }, buffer);
        _output.WriteLine("Exact-fit multi-byte run wrote correctly");
    }

    #endregion

    #region LZW: string table overflow off-by-one

    [Fact]
    public void TiffLzwDecoder_StreamThatOverflowsCodeTable_ThrowsImageFormatException()
    {
        // Arrange - the table holds 4096 entries and the guard read
        // "tableLength > table.Length", which still permits the write at index 4096.
        // Emit a clear code followed by a long run of in-table codes and never clear again,
        // so the table fills and the decoder walks off the end of the array.
        var lzw = BuildTableOverflowingLzwStream();
        using var stream = new BufferedReadStream(Configuration.Default, new MemoryStream(lzw));
        var decoder = new TiffLzwDecoder(stream);

        // A generous destination so decoding is not cut short before the table fills.
        var pixels = new byte[16384];

        // Act & Assert
        var ex = Assert.ThrowsAny<Exception>(() => decoder.DecodePixels(pixels));
        _output.WriteLine($"Table-overflowing LZW stream threw {ex.GetType().Name}: {ex.Message}");
        Assert.IsAssignableFrom<ImageFormatException>(ex);
    }

    /// <summary>
    /// Produces an LZW code stream that drives the decoder string table past its capacity.
    /// The bit width schedule below mirrors the decoder so the emitted codes stay in sync.
    /// </summary>
    private static byte[] BuildTableOverflowingLzwStream()
    {
        const int clearCode = 256;
        var writer = new MsbBitWriter();

        var bitsPerCode = 9;
        var tableLength = 258;
        var maxCode = (1 << bitsPerCode) - 2;

        // Clear code, then a first literal - neither adds a table entry.
        writer.Write(clearCode, bitsPerCode);
        writer.Write(0, bitsPerCode);

        // Every subsequent code adds exactly one table entry.
        for (var i = 0; i < 4000; i++)
        {
            writer.Write(0, bitsPerCode);

            tableLength++;
            if (tableLength > maxCode)
            {
                bitsPerCode = Math.Min(bitsPerCode + 1, 12);
                maxCode = (1 << bitsPerCode) - 2;
            }
        }

        return writer.ToArray();
    }

    private sealed class MsbBitWriter
    {
        private readonly List<byte> bytes = new();
        private int accumulator;
        private int bitCount;

        public void Write(int value, int bits)
        {
            this.accumulator = (this.accumulator << bits) | (value & ((1 << bits) - 1));
            this.bitCount += bits;

            while (this.bitCount >= 8)
            {
                this.bytes.Add((byte)((this.accumulator >> (this.bitCount - 8)) & 0xFF));
                this.bitCount -= 8;
            }
        }

        public byte[] ToArray()
        {
            if (this.bitCount > 0)
            {
                this.bytes.Add((byte)((this.accumulator << (8 - this.bitCount)) & 0xFF));
            }

            return this.bytes.ToArray();
        }
    }

    #endregion

    #region PackBits: unchecked destination writes

    [Fact]
    public void PackBits_LiteralRunLargerThanDestination_ThrowsImageFormatException()
    {
        // Arrange - header byte 0x7F means "copy the next 128 literal bytes", but the
        // destination strip buffer only has room for 8.
        var compressed = new byte[130];
        compressed[0] = 0x7F;
        var destination = new byte[8];

        // Act & Assert
        var ex = RunPackBits(compressed, destination);
        _output.WriteLine($"Oversized PackBits literal run threw {ex.GetType().Name}");
        Assert.IsAssignableFrom<ImageFormatException>(ex);
    }

    [Fact]
    public void PackBits_RepeatRunLargerThanDestination_ThrowsImageFormatException()
    {
        // Arrange - header byte 0x81 means "repeat the next byte 128 times" (257 - 129),
        // far past the 8 byte destination.
        var compressed = new byte[] { 0x81, 0xAB };
        var destination = new byte[8];

        // Act & Assert
        var ex = RunPackBits(compressed, destination);
        _output.WriteLine($"Oversized PackBits repeat run threw {ex.GetType().Name}");
        Assert.IsAssignableFrom<ImageFormatException>(ex);
    }

    [Fact]
    public void PackBits_RepeatHeaderWithoutItsDataByte_ThrowsImageFormatException()
    {
        // Arrange - a repeat header as the final byte: the value byte it needs is missing, so
        // the decoder must not read past the compressed data it was actually given.
        var compressed = new byte[] { 0x81 };
        var destination = new byte[64];

        // Act & Assert
        var ex = RunPackBits(compressed, destination);
        _output.WriteLine($"Truncated PackBits repeat header threw {ex.GetType().Name}");
        Assert.IsAssignableFrom<ImageFormatException>(ex);
    }

    [Fact]
    public void PackBits_ValidData_DecodesCorrectly()
    {
        // Arrange - a literal run of 3 bytes followed by a repeat of 4 bytes.
        var compressed = new byte[] { 0x02, 0x0A, 0x0B, 0x0C, 0xFD, 0x7E };
        var destination = new byte[7];

        // Act
        RunPackBitsExpectingSuccess(compressed, destination);

        // Assert
        Assert.Equal(new byte[] { 0x0A, 0x0B, 0x0C, 0x7E, 0x7E, 0x7E, 0x7E }, destination);
        _output.WriteLine("Valid PackBits data decoded correctly with the new bounds checks");
    }

    private static Exception RunPackBits(byte[] compressed, byte[] destination)
        => Assert.ThrowsAny<Exception>(() => RunPackBitsExpectingSuccess(compressed, destination));

    private static void RunPackBitsExpectingSuccess(byte[] compressed, byte[] destination)
    {
        using var decompressor = new PackBitsTiffCompression(Configuration.Default.MemoryAllocator, destination.Length, 8);
        using var stream = new BufferedReadStream(Configuration.Default, new MemoryStream(compressed));
        decompressor.Decompress(stream, 0, (ulong)compressed.Length, 1, destination);
    }

    #endregion

    #region Real image round trips (guards the new bounds checks against false positives)

    [Theory]
    [InlineData(TiffCompression.Lzw)]
    [InlineData(TiffCompression.PackBits)]
    [InlineData(TiffCompression.Deflate)]
    [InlineData(TiffCompression.None)]
    public void Tiff_RealImage_RoundTripsUnderEachCompression(TiffCompression compression)
    {
        // Arrange - valid data fills the strip buffer exactly, which is precisely the boundary the
        // new "run must fit the buffer" checks sit on. A gradient plus noise exercises both long
        // literal runs and long repeat runs in the PackBits/LZW coders.
        using var source = new Image<Rgba32>(97, 61);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var flat = (x / 16) * 16; // long identical runs
                source[x, y] = new Rgba32((byte)flat, (byte)(y * 3 % 256), (byte)((x * y) % 256), 255);
            }
        }

        var encoder = new TiffEncoder { Compression = compression };

        // Act
        using var ms = new MemoryStream();
        source.Save(ms, encoder);
        ms.Position = 0;
        using var decoded = Image.Load<Rgba32>(ms);

        // Assert - lossless codecs must reproduce the image exactly.
        Assert.Equal(source.Width, decoded.Width);
        Assert.Equal(source.Height, decoded.Height);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                Assert.Equal(source[x, y], decoded[x, y]);
            }
        }

        _output.WriteLine($"{compression} round-tripped {source.Width}x{source.Height} exactly ({ms.Length} bytes)");
    }

    #endregion
}
