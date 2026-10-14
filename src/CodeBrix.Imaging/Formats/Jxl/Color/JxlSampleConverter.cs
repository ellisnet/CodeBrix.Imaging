// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Floating-point sample reconstruction adapted from libjxl lib/jxl/dec_modular.cc,
// revision ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause).
// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// See THIRD-PARTY-NOTICES.txt for the retained license and patent grant.

using System;
using CodeBrix.Imaging.Formats.Jxl.Headers;

namespace CodeBrix.Imaging.Formats.Jxl.Color;

internal static class JxlSampleConverter
{
    internal static float ToFloat(int sample, JxlBitDepth depth)
    {
        int bits = (int)depth.Bits, exponentBits = (int)depth.ExponentBits;
        if (exponentBits == 0) { return sample / (float)((1UL << bits) - 1); }
        if (bits == 32) { return BitConverter.Int32BitsToSingle(sample); }
        uint sign = ((uint)sample >> (bits - 1)) << 31;
        uint magnitude = (uint)sample & ((1U << (bits - 1)) - 1);
        if (magnitude == 0) { return BitConverter.UInt32BitsToSingle(sign); }
        int mantissaBits = bits - exponentBits - 1;
        int exponent = (int)(magnitude >> mantissaBits);
        uint mantissa = (magnitude & ((1U << mantissaBits) - 1)) << (23 - mantissaBits);
        if (exponent == (1 << exponentBits) - 1) { return BitConverter.UInt32BitsToSingle(sign | 0x7F800000 | mantissa); }
        if (exponent == 0 && exponentBits < 8)
        {
            while ((mantissa & 0x800000) == 0) { mantissa <<= 1; exponent--; }
            exponent++;
            mantissa &= 0x7FFFFF;
        }
        exponent += 127 - ((1 << (exponentBits - 1)) - 1);
        return BitConverter.UInt32BitsToSingle(sign | ((uint)exponent << 23) | mantissa);
    }
}
