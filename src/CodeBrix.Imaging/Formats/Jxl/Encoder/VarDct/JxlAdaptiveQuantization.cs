// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from libjxl lib/jxl/enc_adaptive_quantization.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;

internal sealed class JxlAdaptiveQuantization : IDisposable
{
    internal readonly Buffer2D<float> Quantization, Mask, FineMask;

    internal JxlAdaptiveQuantization(JxlFloatFrame xyb, float distance, CancellationToken cancellationToken)
    {
        if (!float.IsFinite(distance) || distance <= 0 || distance > 25) { throw new ArgumentOutOfRangeException(nameof(distance)); }
        int width = xyb.Planes[0].Width, height = xyb.Planes[0].Height, bw = (width + 7) / 8, bh = (height + 7) / 8;
        try
        {
            this.Quantization = xyb.Allocator.Allocate2D<float>(bw, bh);
            this.Mask = xyb.Allocator.Allocate2D<float>(bw, bh);
            this.FineMask = MakeFineMask(xyb, bw * 8, bh * 8, cancellationToken);
            using Buffer2D<float> quarter = xyb.Allocator.Allocate2D<float>(bw * 2, bh * 2);
            // Every loop below computes each output row from shared inputs only.
            JxlParallel.ForEncoderRows(quarter.Height, 8, cancellationToken, (rowStart, rowEnd) =>
            {
            for (int y = rowStart; y < rowEnd; y++)
            {
                Span<float> row = quarter.DangerousGetRowSpan(y);
                for (int x = 0; x < quarter.Width; x++)
                {
                    float sum = 0;
                    for (int dy = 0; dy < 4; dy++)
                    {
                        for (int dx = 0; dx < 4; dx++)
                        {
                            int px = x * 4 + dx, py = y * 4 + dy;
                            float value = Sample(1, px, py);
                            float basis = .25f * (Sample(1, px - 1, py) + Sample(1, px + 1, py) + Sample(1, px, py - 1) + Sample(1, px, py + 1));
                            float difference = Ratio(value + .019f, false) * (value - basis);
                            difference = Math.Min(.2f, difference * difference);
                            sum += .25f * MathF.Sqrt(difference * MathF.Sqrt(211.66567973503678f * 1e8f) + 27.505837037000106f);
                        }
                    }
                    row[x] = sum * .25f;
                }
            }
            });
            float[] weights = new float[4];
            float ramp = Math.Max(0, (2 - distance) * .5f);
            weights[0] = .125f; weights[1] = .1f * (1 - ramp); weights[2] = .09f * (1 - ramp); weights[3] = .06f * (1 - ramp);
            float norm = .29959705784054957f / (weights[0] + weights[1] + weights[2] + weights[3]);
            for (int i = 0; i < weights.Length; i++) { weights[i] *= norm; }
            float dampen = Math.Clamp(1 - (distance - 2) / 12, 0, 1), scale = .765f / Math.Max(.01f, distance);
            JxlParallel.ForEncoderRows(bh, 2, cancellationToken, (rowStart, rowEnd) =>
            {
            Span<float> neighborhood = stackalloc float[9];
            for (int by = rowStart; by < rowEnd; by++)
            {
                for (int bx = 0; bx < bw; bx++)
                {
                    float erosion = 0;
                    for (int dy = 0; dy < 2; dy++)
                    {
                        for (int dx = 0; dx < 2; dx++)
                        {
                            int index = 0;
                            for (int iy = -1; iy <= 1; iy++)
                            {
                                Span<float> row = quarter.DangerousGetRowSpan(Math.Clamp(by * 2 + dy + iy, 0, quarter.Height - 1));
                                for (int ix = -1; ix <= 1; ix++) { neighborhood[index++] = row[Math.Clamp(bx * 2 + dx + ix, 0, quarter.Width - 1)]; }
                            }
                            neighborhood.Sort();
                            for (int i = 0; i < 4; i++) { erosion += neighborhood[i] * weights[i]; }
                        }
                    }
                    this.Mask.DangerousGetRowSpan(by)[bx] = 1 / (erosion + .001f);
                    float v = Math.Max(erosion * .80061762862741759f, .001f), square = v * v;
                    float mask = -.7647f + 9.4708735624378946f / (square + .25f * 3.7179635626140772f)
                        + 17.35036561631863f / (v + 302.59587815579727f) + 6.7943250517376494f / (square + 3.7179635626140772f);
                    float gamma = 0, differences = 0, blue = 0;
                    for (int dy = 0; dy < 8; dy++)
                    {
                        for (int dx = 0; dx < 8; dx++)
                        {
                            int px = bx * 8 + dx, py = by * 8 + dy;
                            float x = Sample(0, px, py), y = Sample(1, px, py), b = Sample(2, px, py);
                            gamma += Ratio(y + .16f - x, true) + Ratio(y + .16f + x, true);
                            if (dx < 7) { differences += Math.Min(.0206f, Math.Abs(y - Sample(1, px + 1, py))); }
                            if (dy < 7) { differences += Math.Min(.0206f, Math.Abs(y - Sample(1, px, py + 1))); }
                            blue += Math.Clamp(b - (y + .0031994768654636393f + Math.Abs(x)), 0, .010474084867598155f);
                        }
                    }
                    mask += .1005613337192697f * MathF.Log2(gamma * (.5f / 64));
                    if (blue >= 32 * .010474084867598155f) { blue = 64 * .010474084867598155f - blue; }
                    blue = Math.Min(blue, 15.463398341612438f * .010474084867598155f) * .90590804735610064f;
                    float exponent = mask + Math.Min(.42f - .38f * differences, blue);
                    this.Quantization.DangerousGetRowSpan(by)[bx] = MathF.Exp(exponent) * scale * dampen + (1 - dampen) * .48f * scale;
                }
            }
            });
        }
        catch { this.Dispose(); throw; }

        float Sample(int c, int x, int y)
        {
            float value = xyb.Planes[c].DangerousGetRowSpan(Math.Clamp(y, 0, height - 1))[Math.Clamp(x, 0, width - 1)];
            if (!float.IsFinite(value)) { throw new ArgumentException("Adaptive quantization requires finite XYB samples.", nameof(xyb)); }
            return value;
        }
    }

    private static Buffer2D<float> MakeFineMask(JxlFloatFrame xyb, int width, int height, CancellationToken cancellationToken)
    {
        Buffer2D<float> output = xyb.Allocator.Allocate2D<float>(width, height);
        try
        {
            using Buffer2D<float> initial = xyb.Allocator.Allocate2D<float>(width, height);
            JxlParallel.ForEncoderRows(height, 16, cancellationToken, (rowStart, rowEnd) =>
            {
            for (int y = rowStart; y < rowEnd; y++)
            {
                Span<float> row = initial.DangerousGetRowSpan(y);
                for (int x = 0; x < width; x++)
                {
                    float value = Sample(x, y), basis = .25f * (Sample(x - 1, y) + Sample(x + 1, y) + Sample(x, y - 1) + Sample(x, y + 1));
                    float difference = Math.Abs(Ratio(value + .019f, false) * (value - basis));
                    row[x] = 1 / ((float)Math.Log(1 + (double)difference) + .01f);
                }
            }
            });
            ReadOnlySpan<float> coefficients = [.364911248f, .05f, .1688888021f, .221069183f, .306563504f];
            float norm = (float)(1 / (1 + 4.0 * (coefficients[0] + coefficients[1] + coefficients[2] + coefficients[4] + 2 * coefficients[3])));
            float[] weights =
            [ norm, norm * coefficients[0], norm * coefficients[2], norm * coefficients[0], norm * coefficients[1], norm * coefficients[3],
              norm * coefficients[2], norm * coefficients[3], norm * coefficients[4] ];
            JxlParallel.ForEncoderRows(height, 16, cancellationToken, (rowStart, rowEnd) =>
            {
            for (int y = rowStart; y < rowEnd; y++)
            {
                Span<float> row = output.DangerousGetRowSpan(y);
                for (int x = 0; x < width; x++)
                {
                    float sum = 0;
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        Span<float> source = initial.DangerousGetRowSpan(JxlRestoration.Mirror(y + dy, height));
                        for (int dx = -2; dx <= 2; dx++)
                        { sum += source[JxlRestoration.Mirror(x + dx, width)] * weights[Math.Abs(dy) * 3 + Math.Abs(dx)]; }
                    }
                    row[x] = sum;
                }
            }
            });
            return output;
        }
        catch { output.Dispose(); throw; }

        float Sample(int x, int y) => xyb.Planes[1].DangerousGetRowSpan(Math.Clamp(y, 0, xyb.Planes[1].Height - 1))[Math.Clamp(x, 0, xyb.Planes[1].Width - 1)];
    }

    // Ratio between cubic-root and the perceptual log-gamma derivatives.
    private static float Ratio(float value, bool inverse)
    {
        const double logTwo = .6931471805599453, multiplier = 226.77216153508914;
        const double resultMultiplier = 18.6580932135 * logTwo / 73.377132366608819;
        double v = Math.Max(0, value), square = v * v;
        double numerator = resultMultiplier * 3 * multiplier * square + .01;
        double denominator = logTwo * multiplier * v * square + 7.7825991679894591 * logTwo + .01;
        return (float)(inverse ? numerator / denominator : denominator / numerator);
    }

    public void Dispose() { this.Quantization?.Dispose(); this.Mask?.Dispose(); this.FineMask?.Dispose(); }
}
