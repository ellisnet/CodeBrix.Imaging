// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Forward counterparts of jxl-oxide crates/jxl-render/src/vardct/
// {generic/transform,transform_common,dct_common}.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).
// Shares the attributed managed DCT and AFV basis tables with the decoder.

using System;
using CodeBrix.Imaging.Formats.Jxl.VarDct;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;

internal static class JxlForwardTransform
{
    internal static void Apply(Span<float> samples, JxlTransformType type)
    {
        (int width, int height) = type.Blocks();
        if ((uint)type > 26 || samples.Length != width * height * 64) { throw new ArgumentException("Invalid JPEG XL transform block."); }
        switch (type)
        {
            case JxlTransformType.Dct2: Aux(samples, 8); Aux(samples, 4); Aux(samples, 2); return;
            case JxlTransformType.Dct4: Quadrants(samples, false); return;
            case JxlTransformType.Hornuss: Quadrants(samples, true); return;
            case JxlTransformType.Dct4x8: Halves(samples, false); return;
            case JxlTransformType.Dct8x4: Halves(samples, true); return;
            case >= JxlTransformType.Afv0 and <= JxlTransformType.Afv3: Afv(samples, (int)type - (int)JxlTransformType.Afv0); return;
            default: JxlDct.Transform2D(samples, width * 8, height * 8, false); return;
        }
    }

    private static void Aux(Span<float> values, int size)
    {
        Span<float> scratch = stackalloc float[64]; int half = size / 2;
        for (int y = 0; y < half; y++)
        {
            for (int x = 0; x < half; x++)
            {
                int i = y * 16 + x * 2;
                float a = values[i], b = values[i + 1], c = values[i + 8], d = values[i + 9];
                scratch[y * 8 + x] = (a + b + c + d) * .25f;
                scratch[y * 8 + x + half] = (a + b - c - d) * .25f;
                scratch[(y + half) * 8 + x] = (a - b + c - d) * .25f;
                scratch[(y + half) * 8 + x + half] = (a - b - c + d) * .25f;
            }
        }
        for (int y = 0; y < size; y++) { scratch.Slice(y * 8, size).CopyTo(values.Slice(y * 8, size)); }
    }

    private static void Quadrants(Span<float> values, bool hornuss)
    {
        Span<float> source = stackalloc float[64], part = stackalloc float[16]; values.CopyTo(source);
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 2; x++)
            {
                for (int iy = 0; iy < 4; iy++) { source.Slice((y * 4 + iy) * 8 + x * 4, 4).CopyTo(part.Slice(iy * 4, 4)); }
                if (hornuss)
                {
                    float sum = 0; foreach (float value in part) { sum += value; }
                    float center = part[5], first = part[0];
                    for (int i = 1; i < 16; i++) { part[i] -= center; }
                    part[0] = sum / 16; part[5] = first - center;
                }
                else { JxlDct.Transform2D(part, 4, 4, false); }
                for (int iy = 0; iy < 4; iy++)
                {
                    for (int ix = 0; ix < 4; ix++)
                    { values[(y + iy * 2) * 8 + x + ix * 2] = part[hornuss ? iy * 4 + ix : ix * 4 + iy]; }
                }
            }
        }
        Aux(values, 2);
    }

    private static void Halves(Span<float> values, bool transpose)
    {
        Span<float> scratch = stackalloc float[64];
        for (int y = 0; y < 8; y++)
        { for (int x = 0; x < 8; x++) { scratch[y * 8 + x] = values[transpose ? x * 8 + y : y * 8 + x]; } }
        for (int part = 0; part < 2; part++)
        {
            Span<float> half = scratch.Slice(part * 32, 32); JxlDct.Transform2D(half, 8, 4, false);
            for (int y = 0; y < 4; y++) { half.Slice(y * 8, 8).CopyTo(values.Slice((y * 2 + part) * 8, 8)); }
        }
        float a = values[0], b = values[8]; values[0] = (a + b) * .5f; values[8] = (a - b) * .5f;
    }

    private static void Afv(Span<float> values, int variant)
    {
        int flipX = variant & 1, flipY = variant >> 1;
        Span<float> quarter = stackalloc float[16], small = stackalloc float[16], large = stackalloc float[32];
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                quarter[(flipY == 0 ? y : 3 - y) * 4 + (flipX == 0 ? x : 3 - x)] = values[(flipY * 4 + y) * 8 + flipX * 4 + x];
                small[y * 4 + x] = values[(flipY * 4 + y) * 8 + (1 - flipX) * 4 + x];
            }
            values.Slice(((1 - flipY) * 4 + y) * 8, 8).CopyTo(large.Slice(y * 8, 8));
        }
        JxlDct.Transform2D(small, 4, 4, false); JxlDct.Transform2D(large, 8, 4, false);
        for (int i = 0; i < 16; i++)
        {
            float value = 0;
            for (int j = 0; j < 16; j++) { value = MathF.FusedMultiplyAdd(quarter[j], JxlInverseTransform.AfvBasis[i * 16 + j], value); }
            values[i / 4 * 16 + i % 4 * 2] = value;
        }
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++) { values[y * 16 + x * 2 + 1] = small[x * 4 + y]; }
            large.Slice(y * 8, 8).CopyTo(values.Slice(y * 16 + 8, 8));
        }
        float a = values[0] * .25f, b = small[0], c = large[0];
        values[0] = (a + b + 2 * c) * .25f; values[1] = (a - b) * .5f; values[8] = (a + b - 2 * c) * .25f;
    }
}
