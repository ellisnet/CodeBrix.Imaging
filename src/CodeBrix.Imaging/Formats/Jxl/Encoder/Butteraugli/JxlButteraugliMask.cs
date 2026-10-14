// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from libjxl lib/jxl/butteraugli/butteraugli.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Memory;
using static CodeBrix.Imaging.Formats.Jxl.Encoder.Butteraugli.JxlButteraugliKernels;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Butteraugli;

internal static class JxlButteraugliMask
{
    internal static void Compare(JxlFloatFrame original, JxlFloatFrame distorted, Buffer2D<float> result,
        float asymmetry, float xMultiplier, CancellationToken cancellationToken)
    {
        int width = result.Width, height = result.Height;
        using JxlFloatFrame ac = Create(width, height, 3, original.Allocator);
        using JxlFloatFrame dc = Create(width, height, 3, original.Allocator);
        using Buffer2D<float> padded = original.Allocator.Allocate2D<float>(checked(width + 8), checked(height + 8));
        for (int c = 0; c < 3; c++) { for (int y = 0; y < height; y++) { ac.Planes[c].DangerousGetRowSpan(y).Clear(); } }
        float rootAsymmetry = MathF.Sqrt(asymmetry);
        AddMalta(9, 1, 1.10039032555 * asymmetry, 1.10039032555 / asymmetry, 71.7800275169, false);
        AddMalta(8, 0, 173.5 * asymmetry, 173.5 / asymmetry, 5, false);
        AddMalta(7, 1, 18.7237414387 * rootAsymmetry, 18.7237414387 / rootAsymmetry, 4498534.45232, true);
        AddMalta(6, 0, 6923.99476109 * rootAsymmetry, 6923.99476109 / rootAsymmetry, 8051.15833247, true);
        AddMalta(4, 1, 37.0819870399, 37.0819870399, 130262059.556, true);
        AddMalta(3, 0, 8246.75321353, 8246.75321353, 1009002.70582, true);
        ReadOnlySpan<double> weights = [400, 1.50815703118, 0, 2150, 10.6195433239, 16.2176043152, 29.2353797994, .844626970982, .703646627719];
        for (int c = 0; c < 3; c++)
        {
            float highWeight = (float)(weights[c] * asymmetry), secondary = (float)(weights[c] / asymmetry);
            highWeight = (float)(highWeight * .8); secondary = (float)(secondary * .8);
            float middleWeight = (float)weights[c + 3], lowWeight = (float)weights[c + 6];
            JxlParallel.ForEncoderRows(height, 16, cancellationToken, (rowStart, rowEnd) =>
            {
            for (int y = rowStart; y < rowEnd; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Span<float> a = ac.Planes[c].DangerousGetRowSpan(y), d = dc.Planes[c].DangerousGetRowSpan(y);
                Span<float> m0 = original.Planes[3 + c].DangerousGetRowSpan(y), m1 = distorted.Planes[3 + c].DangerousGetRowSpan(y);
                Span<float> l0 = original.Planes[c].DangerousGetRowSpan(y), l1 = distorted.Planes[c].DangerousGetRowSpan(y);
                Span<float> h0 = c < 2 ? original.Planes[6 + c].DangerousGetRowSpan(y) : default,
                    h1 = c < 2 ? distorted.Planes[6 + c].DangerousGetRowSpan(y) : default;
                for (int x = 0; x < width; x++)
                {
                    if (c < 2)
                    {
                        float difference = h0[x] - h1[x], small = .4f * Math.Abs(h0[x]), big = Math.Abs(h0[x]);
                        a[x] = MathF.FusedMultiplyAdd(difference * difference, highWeight, a[x]);
                        float v = h0[x] < 0 ? h1[x] > -small ? h1[x] + small : h1[x] < -big ? -h1[x] - big : 0 :
                            h1[x] < small ? small - h1[x] : h1[x] > big ? h1[x] - big : 0;
                        a[x] = MathF.FusedMultiplyAdd(secondary, v * v, a[x]);
                    }
                    float mf = m0[x] - m1[x], lf = l0[x] - l1[x];
                    a[x] = MathF.FusedMultiplyAdd(mf * mf, middleWeight, a[x]); d[x] = lf * lf * lowWeight;
                }
            }
            });
        }
        using Buffer2D<float> mask = original.Allocator.Allocate2D<float>(width, height);
        ComputeMask(original, distorted, mask, ac.Planes[1], cancellationToken);
        const float globalScale = (float)(1.0 / (17.83f * .79079917404f));
        JxlParallel.ForEncoderRows(height, 16, cancellationToken, (rowStart, rowEnd) =>
        {
        for (int y = rowStart; y < rowEnd; y++)
        {
            cancellationToken.ThrowIfCancellationRequested(); Span<float> row = result.DangerousGetRowSpan(y), masking = mask.DangerousGetRowSpan(y);
            for (int x = 0; x < width; x++)
            {
                double ma = globalScale * (1 + 2.5485944793 / (.451936922203 * masking[x] + .829591754942));
                double md = globalScale * (1 + .505054525019 / (3.87449418804 * masking[x] + .20025578522));
                float maskAc = (float)(ma * ma), maskDc = (float)(md * md);
                float a = (ac.Planes[0].DangerousGetRowSpan(y)[x] * xMultiplier * maskAc + ac.Planes[1].DangerousGetRowSpan(y)[x] * maskAc) + ac.Planes[2].DangerousGetRowSpan(y)[x] * maskAc;
                float d = (dc.Planes[0].DangerousGetRowSpan(y)[x] * xMultiplier * maskDc + dc.Planes[1].DangerousGetRowSpan(y)[x] * maskDc) + dc.Planes[2].DangerousGetRowSpan(y)[x] * maskDc;
                row[x] = MathF.Sqrt(d + a);
            }
        }
        });

        void AddMalta(int source, int channel, double greater, double lesser, double normalization, bool low)
            => JxlButteraugliMalta.Add(original.Planes[source], distorted.Planes[source], padded, ac.Planes[channel], greater, lesser, normalization, low, cancellationToken);
    }

    private static void ComputeMask(JxlFloatFrame original, JxlFloatFrame distorted, Buffer2D<float> mask,
        Buffer2D<float> acY, CancellationToken cancellationToken)
    {
        int width = mask.Width, height = mask.Height;
        using JxlFloatFrame scratch = Create(width, height, 4, original.Allocator);
        Buffer2D<float> current = scratch.Planes[0], blurred0 = scratch.Planes[1], blurred1 = scratch.Planes[2], temporary = scratch.Planes[3];
        for (int pass = 0; pass < 2; pass++)
        {
            JxlFloatFrame input = pass == 0 ? original : distorted;
            const float mul = 6.19424080439f, bias = mul * 12.61050594197f;
            float rootBias = MathF.Sqrt(bias);
            JxlParallel.ForEncoderRows(height, 16, cancellationToken, (rowStart, rowEnd) =>
            {
            for (int y = rowStart; y < rowEnd; y++)
            {
                cancellationToken.ThrowIfCancellationRequested(); Span<float> row = current.DangerousGetRowSpan(y);
                Span<float> hx = input.Planes[6].DangerousGetRowSpan(y), hy = input.Planes[7].DangerousGetRowSpan(y), ux = input.Planes[8].DangerousGetRowSpan(y), uy = input.Planes[9].DangerousGetRowSpan(y);
                for (int x = 0; x < width; x++)
                {
                    float dx = (ux[x] + hx[x]) * 2.5f, dy = uy[x] * .4f + hy[x] * .4f;
                    float activity = MathF.Sqrt(dx * dx + dy * dy);
                    row[x] = MathF.Sqrt(mul * activity + bias) - rootBias;
                }
            }
            });
            Blur(current, pass == 0 ? blurred0 : blurred1, temporary, 2.7f, cancellationToken);
        }
        JxlParallel.ForEncoderRows(height, 16, cancellationToken, (rowStart, rowEnd) =>
        {
        for (int y = rowStart; y < rowEnd; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Span<float> row = mask.DangerousGetRowSpan(y), a = blurred0.DangerousGetRowSpan(y), b = blurred1.DangerousGetRowSpan(y), error = acY.DangerousGetRowSpan(y);
            for (int x = 0; x < width; x++)
            {
                float first = a[x], second = 2 * first, third = second;
                for (int dx = -3; dx <= 3; dx += 3)
                {
                    for (int dy = -3; dy <= 3; dy += 3)
                    {
                        if ((dx == 0 && dy == 0) || (uint)(x + dx) >= width || (uint)(y + dy) >= height) { continue; }
                        float value = blurred0.DangerousGetRowSpan(y + dy)[x + dx];
                        if (value < third)
                        {
                            if (value < first) { third = second; second = first; first = value; }
                            else if (value < second) { third = second; second = value; }
                            else { third = value; }
                        }
                    }
                }
                row[x] = .45f * first + .3f * second + .25f * third;
                float delta = a[x] - b[x]; error[x] += 10 * delta * delta;
            }
        }
        });
    }
}
