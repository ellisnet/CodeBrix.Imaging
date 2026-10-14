// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlHalfEncoderTests
{
    [Fact]
    public void lossless_half_pixel_encoding_preserves_all_65536_bit_patterns()
    {
        using Image<HalfSingle> single = new(256, 256);
        using Image<HalfVector2> pair = new(256, 256);
        using Image<HalfVector4> four = new(256, 256);
        for (int i = 0; i < 65536; i++)
        {
            single[i & 255, i >> 8] = new() { PackedValue = (ushort)i };
            pair[i & 255, i >> 8] = new() { PackedValue = (uint)(i | ((65535 - i) << 16)) };
            four[i & 255, i >> 8] = new() { PackedValue = (ulong)(uint)i | ((ulong)(uint)(i ^ 0x8000) << 16) |
                ((ulong)(uint)(65535 - i) << 32) | ((ulong)(uint)((i + 113) & 65535) << 48) };
        }
        Verify(single, 1); Verify(pair, 1); Verify(four, 1);
    }

    [Theory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    public void half_special_values_remain_exact_at_all_efforts(int effort)
    {
        ReadOnlySpan<ushort> bits = [0, 0x8000, 1, 0x8001, 0x03ff, 0x83ff, 0x0400, 0x8400, 0x3c00, 0xbc00, 0x7bff, 0xfbff, 0x7c00, 0xfc00, 0x7c01, 0x7fff, 0xfc01, 0xffff];
        using Image<HalfVector4> image = new(bits.Length, 3);
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            { image[x, y] = new() { PackedValue = (ulong)bits[x] | ((ulong)bits[(x + 1) % bits.Length] << 16) | ((ulong)bits[(x + 3) % bits.Length] << 32) | ((ulong)bits[(x + 7) % bits.Length] << 48) }; }
        }
        Verify(image, effort);
    }

    [Theory]
    [InlineData(1)] [InlineData(7)]
    [Trait("Category", "JxlNativeReference")]
    public async Task pinned_djxl_preserves_half_float_numeric_values(int effort)
    {
        JxlExternalResources.RequireEnabled();
        Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken));
        using Image<HalfVector4> image = new(256, 256);
        for (int i = 0; i < 65536; i++)
        { image[i & 255, i >> 8] = new() { PackedValue = (ulong)(uint)i | ((ulong)(uint)i << 16) | ((ulong)(uint)i << 32) | (0x3c00UL << 48) }; }
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-half-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.jxl"), output = Path.Combine(directory, "decoded.pfm");
            using (FileStream stream = File.Create(input)) { new JxlEncoder { Lossless = true, Effort = effort }.Encode(image, stream); }
            await JxlNativeReference.RunAsync("djxl", [input, output, "--num_threads=1"], TestContext.Current.CancellationToken);
            byte[] pfm = await File.ReadAllBytesAsync(output, TestContext.Current.CancellationToken); int offset = 0;
            string magic = Line(), size = Line(), scale = Line(); Assert.Equal("PF", magic); Assert.Equal("256 256", size);
            Assert.True(scale is "-1.0" or "1.0"); bool little = scale[0] == '-';
            Assert.Equal(256 * 256 * 3 * 4, pfm.Length - offset);
            for (int y = 0; y < 256; y++)
            {
                for (int x = 0; x < 256; x++)
                {
                    float expected = HalfTypeHelper.Unpack((ushort)(y * 256 + x));
                    for (int c = 0; c < 3; c++)
                    {
                        int index = offset + ((((255 - y) * 256 + x) * 3 + c) * 4);
                        uint bits = little ? BinaryPrimitives.ReadUInt32LittleEndian(pfm.AsSpan(index)) : BinaryPrimitives.ReadUInt32BigEndian(pfm.AsSpan(index));
                        if (float.IsNaN(expected)) { Assert.True(float.IsNaN(BitConverter.UInt32BitsToSingle(bits))); }
                        else { Assert.Equal(BitConverter.SingleToUInt32Bits(expected), bits); }
                    }
                }
            }
            string Line()
            { int begin = offset; while (offset < pfm.Length && pfm[offset] != 10) { offset++; } Assert.True(offset < pfm.Length); return Encoding.ASCII.GetString(pfm, begin, offset++ - begin); }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void Verify<TPixel>(Image<TPixel> image, int effort) where TPixel : unmanaged, IPixel<TPixel>
    {
        using MemoryStream stream = new();
        new JxlEncoder { Lossless = true, Effort = effort, AllowExpertEffort = true }.Encode(image, stream);
        stream.Position = 0; using Image<TPixel> decoded = Image.Load<TPixel>(stream);
        Assert.Equal(16, decoded.Metadata.GetJxlMetadata().BitsPerSample);
        Assert.Equal(5, decoded.Metadata.GetJxlMetadata().ExponentBitsPerSample);
        for (int y = 0; y < image.Height; y++)
        { Assert.True(image.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y).SequenceEqual(decoded.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y)), $"{typeof(TPixel).Name}, effort {effort}, row {y}"); }
    }
}
