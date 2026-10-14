// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.PixelFormats;

public class HalfTypeHelperTests
{
    [Fact]
    public void widening_half_matches_ieee_values_and_retains_nan_payloads()
    {
        for (int i = 0; i < 65536; i++)
        {
            float actual = HalfTypeHelper.Unpack((ushort)i);
            if ((i & 0x7fff) > 0x7c00)
            {
                Assert.True(float.IsNaN(actual));
                Assert.Equal(((uint)(i & 0x8000) << 16) | 0x7f800000U | ((uint)(i & 1023) << 13), BitConverter.SingleToUInt32Bits(actual));
            }
            else { Assert.Equal(BitConverter.SingleToUInt32Bits((float)BitConverter.UInt16BitsToHalf((ushort)i)), BitConverter.SingleToUInt32Bits(actual)); }
            Assert.Equal((ushort)i, HalfTypeHelper.Pack(actual));
        }
    }
}
