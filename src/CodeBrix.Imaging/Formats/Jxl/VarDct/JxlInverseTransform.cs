// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-render/src/vardct/{generic/transform,
// transform_common,dct_common}.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

using System;

namespace CodeBrix.Imaging.Formats.Jxl.VarDct;

internal static partial class JxlInverseTransform
{
    internal static void Apply(Span<float> coefficients, JxlTransformType type)
    {
        switch (type)
        {
            case JxlTransformType.Dct2: Aux(coefficients, 2); Aux(coefficients, 4); Aux(coefficients, 8); return;
            case JxlTransformType.Dct4: Quadrants(coefficients, false); return;
            case JxlTransformType.Hornuss: Quadrants(coefficients, true); return;
            case JxlTransformType.Dct4x8: Halves(coefficients, false); return;
            case JxlTransformType.Dct8x4: Halves(coefficients, true); return;
            case >= JxlTransformType.Afv0 and <= JxlTransformType.Afv3: Afv(coefficients, (int)type - (int)JxlTransformType.Afv0); return;
            default:
                (int width, int height) = type.Blocks();
                JxlDct.Transform2D(coefficients, width * 8, height * 8, true); return;
        }
    }

    private static void Aux(Span<float> values, int size)
    {
        Span<float> scratch = stackalloc float[64];
        int half = size / 2;
        for (int y = 0; y < half; y++)
        {
            for (int x = 0; x < half; x++)
            {
                float a = values[(y * 8) + x], b = values[(y * 8) + x + half];
                float c = values[((y + half) * 8) + x], d = values[((y + half) * 8) + x + half];
                int i = (y * 16) + (x * 2);
                scratch[i] = a + b + c + d; scratch[i + 1] = a + b - c - d;
                scratch[i + 8] = a - b + c - d; scratch[i + 9] = a - b - c + d;
            }
        }
        for (int y = 0; y < size; y++) { scratch.Slice(y * 8, size).CopyTo(values.Slice(y * 8, size)); }
    }

    private static void Quadrants(Span<float> values, bool hornuss)
    {
        Aux(values, 2);
        Span<float> scratch = stackalloc float[64];
        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 2; x++)
            {
                Span<float> part = scratch.Slice(((y * 2) + x) * 16, 16);
                for (int iy = 0; iy < 4; iy++)
                {
                    for (int ix = 0; ix < 4; ix++)
                    { part[hornuss ? (iy * 4) + ix : (ix * 4) + iy] = values[((y + (iy * 2)) * 8) + x + (ix * 2)]; }
                }
                if (hornuss)
                {
                    float sum = 0;
                    for (int i = 1; i < 16; i++) { sum += part[i]; }
                    float average = part[0] - (sum / 16);
                    part[0] = part[5]; part[5] = 0;
                    for (int i = 0; i < 16; i++) { part[i] += average; }
                }
                else { JxlDct.Transform2D(part, 4, 4, true); }
            }
        }
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            { values[(y * 8) + x] = scratch[(((y / 4 * 2) + (x / 4)) * 16) + ((y % 4) * 4) + (x % 4)]; }
        }
    }

    private static void Halves(Span<float> values, bool transpose)
    {
        float a = values[0], b = values[8]; values[0] = a + b; values[8] = a - b;
        Span<float> scratch = stackalloc float[64];
        for (int part = 0; part < 2; part++)
        {
            Span<float> half = scratch.Slice(part * 32, 32);
            for (int y = 0; y < 4; y++) { values.Slice(((y * 2) + part) * 8, 8).CopyTo(half.Slice(y * 8, 8)); }
            JxlDct.Transform2D(half, 8, 4, true);
        }
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++) { values[transpose ? (x * 8) + y : (y * 8) + x] = scratch[(y * 8) + x]; }
        }
    }

    private static void Afv(Span<float> values, int variant)
    {
        int flipX = variant & 1, flipY = variant >> 1;
        Span<float> samples = stackalloc float[16]; samples.Clear();
        for (int i = 0; i < 16; i++)
        {
            float coefficient = i == 0 ? (values[0] + values[1] + values[8]) * 4 : values[(i / 4 * 16) + (i % 4 * 2)];
            for (int j = 0; j < 16; j++) { samples[j] = MathF.FusedMultiplyAdd(coefficient, AfvBasis[(i * 16) + j], samples[j]); }
        }
        Span<float> small = stackalloc float[16], large = stackalloc float[32];
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++) { small[(x * 4) + y] = values[(y * 16) + (x * 2) + 1]; }
            for (int x = 0; x < 8; x++) { large[(y * 8) + x] = values[(y * 16) + x + 8]; }
        }
        small[0] = values[0] - values[1] + values[8]; large[0] = values[0] - values[8];
        JxlDct.Transform2D(small, 4, 4, true); JxlDct.Transform2D(large, 8, 4, true);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                values[(((flipY * 4) + y) * 8) + (flipX * 4) + x] = samples[((flipY == 0 ? y : 3 - y) * 4) + (flipX == 0 ? x : 3 - x)];
                values[(((flipY * 4) + y) * 8) + ((1 - flipX) * 4) + x] = small[(y * 4) + x];
            }
            large.Slice(y * 8, 8).CopyTo(values.Slice((((1 - flipY) * 4) + y) * 8, 8));
        }
    }
}
