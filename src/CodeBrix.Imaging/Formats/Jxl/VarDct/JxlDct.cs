// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-render/src/vardct/generic/dct.rs and
// dct_common.rs, 0.12.6, f8ae722ef2d6b782941c89517d19cfbf605c4a9d
// (Apache-2.0 option).

using System;
using System.Numerics;
using System.Runtime.Intrinsics;

namespace CodeBrix.Imaging.Formats.Jxl.VarDct;

internal static partial class JxlDct
{
    private static readonly float[][] Secants = CreateSecants();

    // JPEG XL uses mean-normalized DC and sqrt(2)-normalized AC coefficients.
    internal static void Transform2D(Span<float> values, int width, int height, bool inverse)
    {
        if (inverse && width == 8 && height == 8 && Vector128.IsHardwareAccelerated)
        { Inverse8X8(values); return; }
        if (inverse && width >= 4 && height >= 4 && Vector128.IsHardwareAccelerated)
        { InverseVector(values, width, height); return; }
        if (!inverse && width >= 4 && height >= 4 && Vector128.IsHardwareAccelerated)
        { ForwardVector(values, width, height); return; }
        Span<float> scratch = stackalloc float[256];
        Span<float> column = stackalloc float[256];
        for (int y = 0; y < height; y++) { Transform(values.Slice(y * width, width), scratch[..width], inverse); }
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++) { column[y] = values[(y * width) + x]; }
            Transform(column[..height], scratch[..height], inverse);
            for (int y = 0; y < height; y++) { values[(y * width) + x] = column[y]; }
        }
    }

    private static void Transform(Span<float> values, Span<float> scratch, bool inverse)
    {
        int n = values.Length;
        if (n <= 1) { return; }
        if (n == 2)
        {
            float a = values[0] + values[1], b = values[0] - values[1];
            values[0] = inverse ? a : a * .5f; values[1] = inverse ? b : b * .5f;
            return;
        }
        if (n == 4)
        {
            float a = values[0], b = values[1], c = values[2], d = values[3];
            if (inverse)
            {
                float t0 = b * 1.4142135623730951f, t1 = b + d;
                float r0 = (t0 + t1) * .5411961f, r1 = (t0 - t1) * 1.306563f;
                values[0] = a + c + r0; values[1] = a - c + r1;
                values[2] = a - c - r1; values[3] = a + c - r0;
            }
            else
            {
                float s0 = a + d, s1 = b + c, t0 = (a - d) * .5411961f, t1 = (b - c) * 1.306563f;
                float r0 = (t0 + t1) * .25f, r1 = (t0 - t1) * .25f;
                values[0] = (s0 + s1) * .25f; values[1] = (r0 * 1.4142135623730951f) + r1;
                values[2] = (s0 - s1) * .25f; values[3] = r1;
            }
            return;
        }
        int half = n / 2;
        Span<float> even = scratch[..half], odd = scratch[half..];
        ReadOnlySpan<float> secants = Secants[BitOperations.Log2((uint)n)];
        if (inverse)
        {
            for (int i = 0; i < half; i++) { even[i] = values[i * 2]; odd[i] = values[(i * 2) + 1]; }
            for (int i = half - 1; i > 0; i--) { odd[i] += odd[i - 1]; }
            odd[0] *= 1.4142135623730951f;
            Transform(even, values[..half], true); Transform(odd, values[half..], true);
            for (int i = 0; i < half; i++)
            {
                float r = odd[i] * secants[i]; values[i] = even[i] + r; values[n - 1 - i] = even[i] - r;
            }
        }
        else
        {
            for (int i = 0; i < half; i++)
            {
                even[i] = (values[i] + values[n - 1 - i]) * .5f;
                odd[i] = (values[i] - values[n - 1 - i]) * .5f * secants[i];
            }
            Transform(even, values[..half], false); Transform(odd, values[half..], false);
            odd[0] *= 1.4142135623730951f;
            for (int i = 0; i < half - 1; i++) { odd[i] += odd[i + 1]; }
            for (int i = 0; i < half; i++) { values[i * 2] = even[i]; values[(i * 2) + 1] = odd[i]; }
        }
    }

    private static float[][] CreateSecants()
    {
        float[][] result = new float[9][];
        for (int log = 2; log <= 8; log++)
        {
            int n = 1 << log;
            result[log] = new float[n / 2];
            for (int k = 0; k < n / 2; k++) { result[log][k] = (float)(.5 / Math.Cos(((2 * k) + 1) * Math.PI / (2 * n))); }
        }
        return result;
    }
}
