// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from libjxl lib/jxl/enc_group.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.VarDct;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;

internal static class JxlAcQuantizer
{
    internal static int Refine(ReadOnlySpan<float> coefficients, JxlTransformType type, JxlFrameHeader frame,
        uint globalScale, int original, int effort, Span<float> lumaThresholds, JxlDequantMatrices matrices = null)
    {
        lumaThresholds.Fill(effort >= 5 ? .64f : .62f); lumaThresholds[0] = effort >= 5 ? .58f : .56f;
        if (effort < 5) { return original; }
        (int bw, int bh) = type.Blocks(); int count = bw * bh * 64, best = 0;
        Span<float> thresholds = stackalloc float[4];
        for (int order = 0; order < 3; order++)
        {
            int c = order == 0 ? 1 : order == 1 ? 0 : 2;
            thresholds.Fill(.64f); thresholds[0] = .58f;
            float channelScale = c == 1 ? 1 : MathF.Pow(.8f, 2 - (int)(c == 0 ? frame.XQuantScale : frame.BQuantScale));
            int quant = Adjust(coefficients.Slice(c * count, count), type, c, globalScale * (1f / 65536) * channelScale, original, thresholds, matrices ?? JxlDequantMatrices.Default);
            best = Math.Max(best, quant);
            if (c == 1) { thresholds.CopyTo(lumaThresholds); }
        }
        return best;
    }

    internal static float ChromaThreshold(int quadrant, int blockArea) =>
        Math.Max(.5f, (quadrant == 0 ? .58f : .62f) - (blockArea >= 4 ? .00744f * blockArea : 0));

    private static int Adjust(ReadOnlySpan<float> coefficients, JxlTransformType type, int channel, float scale, int original, Span<float> thresholds, JxlDequantMatrices matrices)
    {
        if (type is JxlTransformType.Hornuss or JxlTransformType.Dct2 or JxlTransformType.Dct4 or
            JxlTransformType.Dct4x8 or JxlTransformType.Dct8x4 or >= JxlTransformType.Afv0 and <= JxlTransformType.Afv3) { return original; }
        (int bw, int bh) = type.Blocks(); int width = bw * 8, height = bh * 8, area = bw * bh;
        if (area > 1)
        { for (int i = 0; i < 4; i++) { thresholds[i] = Math.Max(.54f, thresholds[i] - Math.Min(.003f * area, .08f)); } }
        Span<double> nonzero = stackalloc double[4], maximumError = stackalloc double[4];
        nonzero.Clear(); maximumError.Clear();
        double highest = 0, errorSum = 0, valueSum = 0;
        float[] matrix = matrices.Get(type, channel); bool transpose = type.Transpose();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (x < bw && y < bh) { continue; }
                int quadrant = (y >= height / 2 ? 2 : 0) + (x >= width / 2 ? 1 : 0);
                double value = coefficients[y * width + x] * (scale * original / matrix[transpose ? x * height + y : y * width + x]);
                double rounded = Math.Abs(value) < thresholds[quadrant] ? 0 : Math.Round(value);
                double error = Math.Abs(value - rounded); errorSum += error; valueSum += Math.Abs(rounded);
                if (channel == 1 && rounded == 0) { maximumError[quadrant] = Math.Max(maximumError[quadrant], error); }
                if (rounded != 0)
                {
                    nonzero[quadrant] += Math.Abs(rounded);
                    bool corner = y >= 7 * bh && x >= 7 * bw;
                    bool border = y == height - 1 || x == width - 1;
                    if (corner || (border && x >= 4 * bw && y >= 4 * bh)) { highest += Math.Abs(value); }
                }
            }
        }
        int quant = original;
        if (channel == 1 && valueSum * 8 < area)
        {
            for (int i = 1; i < 4; i++)
            { if (nonzero[i] == 0 && maximumError[i] > .46) { quant = Math.Min(256, original + 1); break; } }
            if (nonzero[3] == 0 && maximumError[3] > .46)
            { thresholds[3] = (float)(.9999 * maximumError[3] * quant / original); }
            else if ((nonzero[1] == 0 && maximumError[1] > .46) || (nonzero[2] == 0 && maximumError[2] > .46))
            { thresholds[1] = thresholds[2] = (float)(.9999 * Math.Max(maximumError[1], maximumError[2]) * quant / original); }
            else if (nonzero[0] == 0 && maximumError[0] > .46)
            { thresholds[0] = (float)(.9999 * maximumError[0] * quant / original); }
        }
        double all = nonzero[0] + nonzero[1] + nonzero[2] + nonzero[3] + 1;
        double increase = (channel == 0 ? 70 : channel == 1 ? 30 : 60) * highest / all;
        if (increase >= 1) { quant = (int)Math.Min(255, quant + increase); }
        if (type == JxlTransformType.Dct8 && all < 12) { quant = Math.Min(255, quant + 1); }
        if (type >= JxlTransformType.Dct16)
        {
            ReadOnlySpan<double> mul1 =
            [.22080615753848404, .45797479824262011, .29859235095977965,
             .70109486510286834, .16185281305512639, .14387691730035473,
             .114985964456218638, .44656840441027695, .10587658215149048,
             .46849665264409396, .41239077937781954, .088667407767185444];
            ReadOnlySpan<double> mul2 =
            [.27450281941822197, 1.1255766549984996, .98950459134128388,
             .4652168675598285, .40945807983455818, .36581899811751367,
             .28034972424715715, .9182653201929738, 1.5581531543057416,
             .26873118114033728, .68863712390392484, 1.2082185408666786];
            int index = type is JxlTransformType.Dct32x16 or JxlTransformType.Dct16x32 ? 1 : type == JxlTransformType.Dct16 ? 0 : type == JxlTransformType.Dct32 ? 2 : 3;
            const double normalizer = 2.2942708343284721;
            double denominator = mul1[index * 3 + channel] * area * 64 + mul2[index * 3 + channel] * valueSum * normalizer;
            if (errorSum * normalizer > denominator)
            { quant = Math.Min(255, quant + (int)Math.Clamp(errorSum * normalizer / denominator, 0, 2)); }
        }
        double minimum = Math.Min(Math.Min(nonzero[0], nonzero[1]), Math.Min(nonzero[2], nonzero[3]));
        int activity = minimum < 15 * area ? ((int)minimum + area / 2) / area : 15;
        if (channel == 1) { for (int i = 1; i < 4; i++) { thresholds[i] += .01f * activity; } }
        return Math.Clamp(Math.Max(quant - activity, Math.Max(4, quant / 2)), 1, 256);
    }
}
