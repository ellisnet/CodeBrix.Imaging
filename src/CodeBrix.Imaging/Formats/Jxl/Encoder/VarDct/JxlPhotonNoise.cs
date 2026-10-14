// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from libjxl lib/jxl/enc_photon_noise.cc and enc_noise.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;

internal static class JxlPhotonNoise
{
    internal static void Write(JxlBitWriter writer, int width, int height, float iso)
    {
        if (width <= 0 || height <= 0 || !float.IsFinite(iso) || iso <= 0) { throw new ArgumentOutOfRangeException(nameof(iso)); }
        // Double intermediates avoid overflow for finite extreme ISO values.
        const double bias = .0037930732552754493;
        double rootBias = Math.Cbrt(bias);
        double electrons18 = .20 * 11260 * (10.0 / iso) * (36000.0 * 24000 / ((long)width * height));
        for (int i = 0; i < 8; i++)
        {
            double y = 2 * (i / 6.0), shifted = y - rootBias;
            double linear = Math.Max(0, shifted * shifted * shifted + bias);
            double electrons = electrons18 * (linear / .18), nonuniformity = .005 * electrons;
            double noise = Math.Sqrt(9 + electrons + nonuniformity * nonuniformity) * (.18 / electrons18);
            double root = Math.Cbrt(linear - bias), derivative = (1.0 / 3) / (root * root);
            double strength = Math.Clamp(noise * derivative / (.22 * Math.Sqrt(2) * 1.13), 0, 1023.4999 / 1024);
            writer.WriteBits(10, (uint)Math.Round(strength * 1024, MidpointRounding.AwayFromZero));
        }
    }
}
