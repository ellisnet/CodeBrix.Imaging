// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from libjxl lib/jxl/enc_gaborish.cc and convolve.h,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;

internal static class JxlInverseGabor
{
    internal static void Apply(JxlFloatFrame frame, CancellationToken cancellationToken)
    {
        ReadOnlySpan<float> coefficients = [-.09495815671340026f, -.041031725066768575f, .013710004822696948f, .006510206083837737f, -.0014789063378272242f];
        float norm = (float)(1 / (1 + 4.0 * (coefficients[0] + coefficients[1] + coefficients[2] + coefficients[4] + 2 * coefficients[3])));
        float[] weights =
        [ norm, norm * coefficients[0], norm * coefficients[2], norm * coefficients[0], norm * coefficients[1], norm * coefficients[3],
          norm * coefficients[2], norm * coefficients[3], norm * coefficients[4] ];
        int width = frame.Planes[0].Width, height = frame.Planes[0].Height;
        Buffer2D<float> scratch = frame.Allocator.Allocate2D<float>(width, height);
        try
        {
            for (int c = 0; c < 3; c++)
            {
                Buffer2D<float> input = frame.Planes[c], output = scratch;
                JxlParallel.ForEncoderRows(height, 16, cancellationToken, (rowStart, rowEnd) =>
                {
                    for (int y = rowStart; y < rowEnd; y++)
                    {
                        Span<float> target = output.DangerousGetRowSpan(y);
                        for (int x = 0; x < width; x++)
                        {
                            float sum = 0;
                            for (int dy = -2; dy <= 2; dy++)
                            {
                                Span<float> row = input.DangerousGetRowSpan(JxlRestoration.Mirror(y + dy, height));
                                for (int dx = -2; dx <= 2; dx++) { sum += row[JxlRestoration.Mirror(x + dx, width)] * weights[Math.Abs(dy) * 3 + Math.Abs(dx)]; }
                            }
                            target[x] = sum;
                        }
                    }
                });
                frame.Planes[c] = scratch; scratch = input;
            }
        }
        finally { scratch.Dispose(); }
    }
}
