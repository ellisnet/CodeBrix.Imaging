// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlBitstreamTests
{
    [Fact]
    public void reads_little_endian_bits_across_byte_boundaries()
    {
        //Arrange: independent byte vector; no matching writer is used.
        using MemoryStream source = new([0x96, 0x3c, 0xf0, 0xa5, 0x81]);
        JxlBitReader reader = new(source);
        //Act / Assert
        Assert.Equal(6U, reader.ReadBits(3));
        Assert.Equal(0x92U, reader.ReadBits(8));
        Assert.Equal(0x14be07U, reader.ReadBits(21));
        Assert.Equal(0x81U, reader.ReadBits(8));
        Assert.Throws<InvalidImageContentException>(() => reader.ReadBits(1));
    }

    [Fact]
    public void u32_matches_upstream_documented_vector()
    {
        using MemoryStream source = new([0b110010]);
        JxlBitReader reader = new(source);
        Assert.Equal(15U, reader.ReadU32(1, JxlU32.U(2), JxlU32.U(4, 3), JxlU32.U(8, 19)));
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(16UL)]
    [InlineData(17UL)]
    [InlineData(272UL)]
    [InlineData(273UL)]
    [InlineData(4095UL)]
    [InlineData(4096UL)]
    [InlineData(ulong.MaxValue)]
    public void u64_round_trips_all_selector_boundaries(ulong value)
    {
        using MemoryStream stream = new();
        JxlBitWriter writer = new(stream);
        writer.WriteBits(3, 5);
        writer.WriteU64(value);
        writer.ZeroPadToByte();
        stream.Position = 0;
        JxlBitReader reader = new(stream);
        Assert.Equal(5U, reader.ReadBits(3));
        Assert.Equal(value, reader.ReadU64());
        reader.ZeroPadToByte();
    }

    [Theory]
    [InlineData(0x0000, 0f)]
    [InlineData(0x3c00, 1f)]
    [InlineData(0xc000, -2f)]
    [InlineData(0x0001, 0.000000059604644775390625f)]
    public void f16_decodes_known_values(int bits, float expected)
    {
        using MemoryStream stream = new([(byte)bits, (byte)(bits >> 8)]);
        Assert.Equal(expected, new JxlBitReader(stream).ReadF16());
    }

    [Theory]
    [InlineData(0x7c00)]
    [InlineData(0xfc00)]
    [InlineData(0x7e00)]
    public void f16_rejects_infinite_and_nan_fields(int bits)
    {
        using MemoryStream stream = new([(byte)bits, (byte)(bits >> 8)]);
        Assert.Throws<InvalidImageContentException>(() => new JxlBitReader(stream).ReadF16());
    }

    [Fact]
    public void lookahead_never_allows_consuming_truncated_bits()
    {
        using MemoryStream stream = new([0xff]);
        JxlBitReader reader = new(stream);
        Assert.Equal(255U, reader.PeekBits(32));
        Assert.Throws<InvalidImageContentException>(() => reader.ConsumeBits(9));
        Assert.Equal(255U, reader.ReadBits(8));
    }

    [Fact]
    public void padding_must_be_zero()
    {
        using MemoryStream stream = new([0x80]);
        JxlBitReader reader = new(stream);
        reader.ReadBits(3);
        Assert.Throws<InvalidImageContentException>(() => reader.ZeroPadToByte());
    }
}
