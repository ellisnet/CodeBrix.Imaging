// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from libjxl lib/jxl/enc_chroma_from_luma.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Buffers;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Formats.Jxl.VarDct;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;

internal static class JxlChromaFromLuma
{
    // Without blocks this is the initial 8x8 search. The final search uses
    // selected transforms and their quantization-weighted AC coefficients.
    internal static void Compute(JxlFloatFrame xyb, JxlModularPlane xMap, JxlModularPlane bMap, int effort, CancellationToken cancellationToken,
        Buffer2D<JxlVarBlock> blocks = null, uint globalScale = 65536, JxlDequantMatrices matrices = null)
    {
        matrices ??= JxlDequantMatrices.Default;
        if (effort < 5) { return; }
        int width = xyb.Planes[0].Width, height = xyb.Planes[0].Height;
        int scratchSize = blocks == null ? 192 : 3 * 4096;
        // Each 64x64 color tile reads shared inputs and writes only its own map entries.
        JxlParallel.ForEncoder(xMap.Width * xMap.Height, cancellationToken,
            () => xyb.Allocator.Allocate<float>(scratchSize + 4 * 4096), (tile, memory) =>
        {
            Span<float> block = memory.Memory.Span[..scratchSize], values = memory.Memory.Span.Slice(scratchSize, 4 * 4096);
            int tx = tile % xMap.Width, ty = tile / xMap.Width;
            {
                cancellationToken.ThrowIfCancellationRequested(); int count = 0;
                for (int by = ty * 64; by < Math.Min(height, ty * 64 + 64); by += 8)
                {
                    for (int bx = tx * 64; bx < Math.Min(width, tx * 64 + 64); bx += 8)
                    {
                        JxlVarBlock strategy = blocks == null ? new(JxlTransformType.Dct8, 1) : blocks.DangerousGetRowSpan(by / 8)[bx / 8];
                        if (strategy.Multiplier <= 0) { continue; }
                        (int dw, int dh) = strategy.Transform.Blocks();
                        if (bx % 64 + dw * 8 > 64 || by % 64 + dh * 8 > 64) { continue; }
                        int w = dw * 8, h = dh * 8, size = w * h;
                        float q = blocks == null ? 1 : globalScale * (128f / 65536) * strategy.Multiplier;
                        float[] matrixX = matrices.Get(strategy.Transform, 0), matrixB = matrices.Get(strategy.Transform, 2);
                        for (int c = 0; c < 3; c++)
                        {
                            Span<float> channel = block.Slice(c * size, size);
                            for (int y = 0; y < h; y++)
                            {
                                Span<float> row = xyb.Planes[c].DangerousGetRowSpan(Math.Min(height - 1, by + y));
                                for (int x = 0; x < w; x++)
                                {
                                    float value = row[Math.Min(width - 1, bx + x)];
                                    if (!float.IsFinite(value)) { throw new ArgumentException("Lossy JPEG XL requires finite samples.", nameof(xyb)); }
                                    channel[y * w + x] = value;
                                }
                            }
                            JxlForwardTransform.Apply(channel, strategy.Transform);
                            for (int y = 0; y < dh; y++) { channel.Slice(y * w, dw).Clear(); }
                        }
                        bool transpose = strategy.Transform.Transpose();
                        for (int y = 0; y < h; y++)
                        {
                            for (int x = 0; x < w; x++)
                            {
                                int i = y * w + x, m = transpose ? x * h + y : i;
                                values[count + i] = block[size + i] * q / matrixX[m];
                                values[4096 + count + i] = block[i] * q / matrixX[m];
                                values[8192 + count + i] = block[size + i] * q / matrixB[m];
                                values[12288 + count + i] = block[2 * size + i] * q / matrixB[m];
                            }
                        }
                        count += size;
                    }
                }
                xMap.Set(tx, ty, Find(values[..count], values.Slice(4096, count), 0, effort <= 6));
                bMap.Set(tx, ty, Find(values.Slice(8192, count), values.Slice(12288, count), 1, effort <= 6));
            }
        });
    }

    internal static int Find(ReadOnlySpan<float> luma, ReadOnlySpan<float> chroma, float basis, bool fast)
    {
        const double regularization = 1e-9;
        double x = 0;
        if (fast)
        {
            double aa = 0, ab = 0;
            for (int i = 0; i < luma.Length; i++)
            { double a = luma[i] / 84.0, b = basis * (double)luma[i] - chroma[i]; aa += a * a; ab += a * b; }
            x = -ab / (aa + luma.Length * regularization * .5);
        }
        else
        {
            for (int iteration = 0; iteration < 20; iteration++)
            {
                Derivatives(luma, chroma, basis, x, out double derivative, out double second);
                double step = derivative / (second + .85);
                x -= Math.Clamp(step, -20, 20);
                if (Math.Abs(step) < .003) { break; }
            }
        }
        x = x >= 2.6 ? x - 2.6 : x <= -2.6 ? x + 2.6 : 0;
        return (int)Math.Clamp(Math.Round(x, MidpointRounding.AwayFromZero), -128, 127);

        static void Derivatives(ReadOnlySpan<float> luma, ReadOnlySpan<float> chroma, float basis, double x,
            out double derivative, out double second)
        {
            derivative = 2 * regularization * luma.Length * x;
            double plus = 2 * regularization * luma.Length * (x + 100);
            double minus = 2 * regularization * luma.Length * (x - 100);
            for (int i = 0; i < luma.Length; i++)
            {
                double a = luma[i] / 84.0, b = basis * (double)luma[i] - chroma[i];
                double value = a * x + b, absolute = Math.Abs(value);
                // Use the current residual's outlier mask for all three
                // derivatives. Reclassifying the perturbed residuals changes
                // the reference's approximate curvature and destabilizes fits.
                if (absolute >= 100) { continue; }
                double scale = 2.0 / 3 * a;
                double currentSlope = scale * (absolute + 1);
                derivative += value < 0 ? -currentSlope : currentSlope;
                double plusValue = a * (x + 100) + b, minusValue = a * (x - 100) + b;
                double plusSlope = scale * (Math.Abs(plusValue) + 1), minusSlope = scale * (Math.Abs(minusValue) + 1);
                plus += plusValue < 0 ? -plusSlope : plusSlope;
                minus += minusValue < 0 ? -minusSlope : minusSlope;
            }
            second = (plus - minus) / 200;
        }
    }
}
