// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using CodeBrix.Imaging.Formats.Jxl.Color;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlSampleConverterTests
{
    [Fact]
    public void every_binary16_pattern_matches_bcl_half_conversion()
    {
        JxlBitDepth depth = new(16, 5);
        for (int bits = 0; bits <= ushort.MaxValue; bits++)
        {
            float expected = (float)BitConverter.UInt16BitsToHalf((ushort)bits);
            float actual = JxlSampleConverter.ToFloat(bits, depth);
            if (float.IsNaN(expected)) { Assert.True(float.IsNaN(actual)); }
            else { Assert.Equal(BitConverter.SingleToUInt32Bits(expected), BitConverter.SingleToUInt32Bits(actual)); }
        }
    }

    [Theory]
    [InlineData(0U)]
    [InlineData(0x80000000U)]
    [InlineData(1U)]
    [InlineData(0x7F800000U)]
    [InlineData(0xFF800000U)]
    [InlineData(0x7F812345U)]
    [InlineData(0xFFD12345U)]
    public void binary32_preserves_all_bits_including_nan_payloads(uint bits)
    {
        Assert.Equal(bits, BitConverter.SingleToUInt32Bits(JxlSampleConverter.ToFloat(unchecked((int)bits), new(32, 8))));
    }

    [Fact]
    public void custom_float_subnormals_and_integer_endpoints_are_correct()
    {
        Assert.Equal(MathF.ScaleB(1, -13), JxlSampleConverter.ToFloat(1, new(12, 4)));
        Assert.Equal(0x00100000U, BitConverter.SingleToUInt32Bits(JxlSampleConverter.ToFloat(1, new(12, 8))));
        Assert.Equal(0, JxlSampleConverter.ToFloat(0, new(31, 0)));
        Assert.Equal(1, JxlSampleConverter.ToFloat(int.MaxValue, new(31, 0)));
    }
}
