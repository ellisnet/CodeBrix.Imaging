// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from libjxl lib/jxl/butteraugli/butteraugli.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Threading;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Butteraugli;

internal static class JxlButteraugliMalta
{
    internal static void Add(Buffer2D<float> original, Buffer2D<float> distorted, Buffer2D<float> padded,
        Buffer2D<float> destination, double greaterWeight, double lesserWeight, double normalization,
        bool lowFrequency, CancellationToken cancellationToken)
    {
        double multiplier = lowFrequency ? .611612573796 : .39905817637;
        float first = (float)(multiplier * Math.Sqrt(.5f * greaterWeight) / 8.5 * normalization);
        float second = (float)(multiplier * Math.Sqrt(.33f * lesserWeight) / 8.5 * normalization);
        JxlParallel.ForEncoderRows(padded.Height, 32, cancellationToken, (rowStart, rowEnd) =>
        {
        for (int y = rowStart; y < rowEnd; y++)
        { padded.DangerousGetRowSpan(y).Clear(); }
        });
        JxlParallel.ForEncoderRows(original.Height, 16, cancellationToken, (rowStart, rowEnd) =>
        {
        for (int y = rowStart; y < rowEnd; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Span<float> a = original.DangerousGetRowSpan(y), b = distorted.DangerousGetRowSpan(y), output = padded.DangerousGetRowSpan(y + 4).Slice(4, original.Width);
            for (int x = 0; x < original.Width; x++)
            {
                float scale = (float)normalization + .5f * (Math.Abs(a[x]) + Math.Abs(b[x]));
                float difference = first / scale * (a[x] - b[x]), secondary = second / scale;
                double small = .55 * Math.Abs(a[x]), big = 1.05 * Math.Abs(a[x]);
                if (a[x] < 0)
                {
                    if (b[x] > -small) { difference = (float)(difference - secondary * (b[x] + small)); }
                    else if (b[x] < -big) { difference = (float)(difference + secondary * (-b[x] - big)); }
                }
                else
                {
                    if (b[x] < small) { difference = (float)(difference + secondary * (small - b[x])); }
                    else if (b[x] > big) { difference = (float)(difference - secondary * (b[x] - big)); }
                }
                output[x] = difference;
            }
        }
        });
        JxlParallel.ForEncoderRows(original.Height, 16, cancellationToken, (rowStart, rowEnd) =>
        {
        for (int y = rowStart; y < rowEnd; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Span<float> output = destination.DangerousGetRowSpan(y);
            ReadOnlySpan<float> r0 = padded.DangerousGetRowSpan(y), r1 = padded.DangerousGetRowSpan(y + 1), r2 = padded.DangerousGetRowSpan(y + 2),
                r3 = padded.DangerousGetRowSpan(y + 3), r4 = padded.DangerousGetRowSpan(y + 4), r5 = padded.DangerousGetRowSpan(y + 5),
                r6 = padded.DangerousGetRowSpan(y + 6), r7 = padded.DangerousGetRowSpan(y + 7), r8 = padded.DangerousGetRowSpan(y + 8);
            for (int x = 0; x < original.Width; x++)
            {
                output[x] += lowFrequency ? LowFrequency(r0, r1, r2, r3, r4, r5, r6, r7, r8, x + 4)
                    : FullFrequency(r0, r1, r2, r3, r4, r5, r6, r7, r8, x + 4);
            }
        }
        });
    }

    private static float LowFrequency(ReadOnlySpan<float> r0, ReadOnlySpan<float> r1, ReadOnlySpan<float> r2, ReadOnlySpan<float> r3, ReadOnlySpan<float> r4, ReadOnlySpan<float> r5, ReadOnlySpan<float> r6, ReadOnlySpan<float> r7, ReadOnlySpan<float> r8, int x)
    {
        float s0 = Sum(r4[x - 4], r4[x - 2], r4[x], r4[x + 2], r4[x + 4]);
        float result = s0 * s0;
        float s1 = Sum(r0[x], r2[x], r4[x], r6[x], r8[x]);
        result = MathF.FusedMultiplyAdd(s1, s1, result);
        float s2 = Sum(r1[x - 3], r2[x - 2], r4[x], r6[x + 2], r7[x + 3]);
        result = MathF.FusedMultiplyAdd(s2, s2, result);
        float s3 = Sum(r1[x + 3], r2[x + 2], r4[x], r6[x - 2], r7[x - 3]);
        result = MathF.FusedMultiplyAdd(s3, s3, result);
        float s4 = Sum(r0[x + 1], r2[x + 1], r4[x], r6[x - 1], r8[x - 1]);
        result = MathF.FusedMultiplyAdd(s4, s4, result);
        float s5 = Sum(r0[x - 1], r2[x - 1], r4[x], r6[x + 1], r8[x + 1]);
        result = MathF.FusedMultiplyAdd(s5, s5, result);
        float s6 = Sum(r3[x - 4], r3[x - 2], r4[x], r5[x + 2], r5[x + 4]);
        result = MathF.FusedMultiplyAdd(s6, s6, result);
        float s7 = Sum(r5[x - 4], r5[x - 2], r4[x], r3[x + 2], r3[x + 4]);
        result = MathF.FusedMultiplyAdd(s7, s7, result);
        float s8 = Sum(r1[x - 2], r2[x - 1], r4[x], r6[x + 1], r7[x + 2]);
        result = MathF.FusedMultiplyAdd(s8, s8, result);
        float s9 = Sum(r1[x + 2], r2[x + 1], r4[x], r6[x - 1], r7[x - 2]);
        result = MathF.FusedMultiplyAdd(s9, s9, result);
        float s10 = Sum(r2[x - 3], r3[x - 2], r4[x], r5[x + 2], r6[x + 3]);
        result = MathF.FusedMultiplyAdd(s10, s10, result);
        float s11 = Sum(r2[x + 3], r3[x + 2], r4[x], r5[x - 2], r6[x - 3]);
        result = MathF.FusedMultiplyAdd(s11, s11, result);
        float s12 = Sum(r6[x - 4], r5[x - 2], r4[x], r3[x + 2], r2[x + 4]);
        result = MathF.FusedMultiplyAdd(s12, s12, result);
        float s13 = Sum(r2[x - 4], r3[x - 2], r4[x], r5[x + 2], r6[x + 4]);
        result = MathF.FusedMultiplyAdd(s13, s13, result);
        float s14 = Sum(r0[x - 2], r2[x - 1], r4[x], r6[x + 1], r8[x + 2]);
        result = MathF.FusedMultiplyAdd(s14, s14, result);
        float s15 = Sum(r0[x + 2], r2[x + 1], r4[x], r6[x - 1], r8[x - 2]);
        result = MathF.FusedMultiplyAdd(s15, s15, result);
        return result;
    }

    private static float FullFrequency(ReadOnlySpan<float> r0, ReadOnlySpan<float> r1, ReadOnlySpan<float> r2, ReadOnlySpan<float> r3, ReadOnlySpan<float> r4, ReadOnlySpan<float> r5, ReadOnlySpan<float> r6, ReadOnlySpan<float> r7, ReadOnlySpan<float> r8, int x)
    {
        float s0 = Sum(r4[x - 4], r4[x - 3], r4[x - 2], r4[x - 1], r4[x], r4[x + 1], r4[x + 2], r4[x + 3], r4[x + 4]);
        float result = s0 * s0;
        float s1 = Sum(r0[x], r1[x], r2[x], r3[x], r4[x], r5[x], r6[x], r7[x], r8[x]);
        result = MathF.FusedMultiplyAdd(s1, s1, result);
        float s2 = Sum(r1[x - 3], r2[x - 2], r3[x - 1], r4[x], r5[x + 1], r6[x + 2], r7[x + 3]);
        result = MathF.FusedMultiplyAdd(s2, s2, result);
        float s3 = Sum(r1[x + 3], r2[x + 2], r3[x + 1], r4[x], r5[x - 1], r6[x - 2], r7[x - 3]);
        result = MathF.FusedMultiplyAdd(s3, s3, result);
        float s4 = Sum(r0[x + 1], r1[x + 1], r2[x + 1], r3[x], r4[x], r5[x], r6[x - 1], r7[x - 1], r8[x - 1]);
        result = MathF.FusedMultiplyAdd(s4, s4, result);
        float s5 = Sum(r0[x - 1], r1[x - 1], r2[x - 1], r3[x], r4[x], r5[x], r6[x + 1], r7[x + 1], r8[x + 1]);
        result = MathF.FusedMultiplyAdd(s5, s5, result);
        float s6 = Sum(r3[x - 4], r3[x - 3], r3[x - 2], r4[x - 1], r4[x], r4[x + 1], r5[x + 2], r5[x + 3], r5[x + 4]);
        result = MathF.FusedMultiplyAdd(s6, s6, result);
        float s7 = Sum(r5[x - 4], r5[x - 3], r5[x - 2], r4[x - 1], r4[x], r4[x + 1], r3[x + 2], r3[x + 3], r3[x + 4]);
        result = MathF.FusedMultiplyAdd(s7, s7, result);
        float s8 = Sum(r1[x - 2], r2[x - 1], r3[x - 1], r4[x], r5[x + 1], r6[x + 1], r7[x + 2]);
        result = MathF.FusedMultiplyAdd(s8, s8, result);
        float s9 = Sum(r1[x + 2], r2[x + 1], r3[x + 1], r4[x], r5[x - 1], r6[x - 1], r7[x - 2]);
        result = MathF.FusedMultiplyAdd(s9, s9, result);
        float s10 = Sum(r2[x - 3], r3[x - 2], r3[x - 1], r4[x], r5[x + 1], r5[x + 2], r6[x + 3]);
        result = MathF.FusedMultiplyAdd(s10, s10, result);
        float s11 = Sum(r2[x + 3], r3[x + 2], r3[x + 1], r4[x], r5[x - 1], r5[x - 2], r6[x - 3]);
        result = MathF.FusedMultiplyAdd(s11, s11, result);
        float s12 = Sum(r5[x - 4], r5[x - 3], r5[x - 2], r4[x - 1], r4[x], r4[x + 1], r3[x + 2], r3[x + 3], r3[x + 4]);
        result = MathF.FusedMultiplyAdd(s12, s12, result);
        float s13 = Sum(r3[x - 4], r3[x - 3], r3[x - 2], r4[x - 1], r4[x], r4[x + 1], r5[x + 2], r5[x + 3], r5[x + 4]);
        result = MathF.FusedMultiplyAdd(s13, s13, result);
        float s14 = Sum(r0[x - 1], r1[x - 1], r2[x - 1], r3[x], r4[x], r5[x], r6[x + 1], r7[x + 1], r8[x + 1]);
        result = MathF.FusedMultiplyAdd(s14, s14, result);
        float s15 = Sum(r0[x + 1], r1[x + 1], r2[x + 1], r3[x], r4[x], r5[x], r6[x - 1], r7[x - 1], r8[x - 1]);
        result = MathF.FusedMultiplyAdd(s15, s15, result);
        return result;
    }

    private static float Sum(float a, float b, float c, float d) => (a + b) + (c + d);
    private static float Sum(float a, float b, float c, float d, float e) => Sum(a, b, c, d + e);
    private static float Sum(float a, float b, float c, float d, float e, float f, float g) => Sum(a, b, c, Sum(d, e, f, g));
    private static float Sum(float a, float b, float c, float d, float e, float f, float g, float h, float i) => (Sum(a, b, c, d) + Sum(e, f, g, h)) + i;
}
