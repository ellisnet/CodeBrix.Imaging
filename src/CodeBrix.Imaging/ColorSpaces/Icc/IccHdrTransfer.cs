// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// HDR transfer functions and reference OOTF adapted from skcms skcms.cc and
// src/Transform_inl.h, 7e572ada698a60ff50bd9038fd6e01e44f40868a (BSD-3-Clause).
// Copyright (c) 2018 Google Inc. All rights reserved.
// See THIRD-PARTY-NOTICES.txt for the complete upstream notice.

using System;
using System.Numerics;

namespace CodeBrix.Imaging.ColorSpaces.Icc;

internal static class IccHdrTransfer
{
    internal static float Decode(float value, byte transfer)
    {
        double a = Math.Abs(value), result;
        if (transfer == 16)
        {
            double power = Math.Pow(a, 32.0 / 2523);
            result = Math.Pow(Math.Max(power - (107.0 / 128), 0) / ((2413.0 / 128) - (2392.0 / 128 * power)), 8192.0 / 1305) * (10000.0 / 203);
        }
        else { result = a <= .5 ? a * a / 3 : (Math.Exp((a - .55991073) / .17883277) + .28466892) / 12; }
        return MathF.CopySign((float)result, value);
    }
    internal static float Encode(float value, byte transfer)
    {
        double a = Math.Abs(value), result;
        if (transfer == 16)
        {
            double power = Math.Pow(a * (203.0 / 10000), 1305.0 / 8192);
            result = Math.Pow(((2413.0 / 128 * power) + (107.0 / 128)) / ((2392.0 / 128 * power) + 1), 2523.0 / 32);
        }
        else { result = a <= 1.0 / 12 ? Math.Sqrt(3 * a) : (.17883277 * Math.Log((12 * a) - .28466892)) + .55991073; }
        return MathF.CopySign((float)result, value);
    }
    internal static Vector3 HlgOotf(Vector3 xyz, bool inverse)
    {
        if (inverse) { xyz *= 203f / 1000; }
        // Rec.2100 uses D65 luminance, expressed here in the D50 PCS.
        float y = (-.02831655f * xyz.X) + (1.00995452f * xyz.Y) + (.02102382f * xyz.Z);
        if (y == 0) { return xyz; }
        float scale = MathF.CopySign(MathF.Pow(Math.Abs(y), inverse ? (1f / 1.2f) - 1 : .2f), y);
        return xyz * (inverse ? scale : scale * (1000f / 203));
    }
}
