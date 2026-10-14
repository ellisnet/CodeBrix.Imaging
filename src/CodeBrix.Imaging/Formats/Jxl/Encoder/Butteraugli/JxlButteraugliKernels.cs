// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from libjxl lib/jxl/butteraugli/butteraugli.cc,
// enc_convolve_separable5.cc and base/fast_math-inl.h,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Butteraugli;

internal static class JxlButteraugliKernels
{
    // Psycho-image plane layout: LF[0..2], MF[3..5], HF[6..7], UHF[8..9].
    internal static JxlFloatFrame Create(int width, int height, int channels, MemoryAllocator allocator)
    {
        JxlFloatFrame result = new(allocator, channels, channels);
        try
        {
            for (int c = 0; c < channels; c++) { result.Planes[c] = allocator.Allocate2D<float>(width, height); }
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    internal static JxlFloatFrame PsychoImage(JxlFloatFrame rgb, float intensity, CancellationToken cancellationToken)
    {
        int width = rgb.Planes[0].Width, height = rgb.Planes[0].Height;
        using JxlFloatFrame xyb = Create(width, height, 3, rgb.Allocator);
        using JxlFloatFrame blurred = Create(width, height, 3, rgb.Allocator);
        using Buffer2D<float> temporary = rgb.Allocator.Allocate2D<float>(width, height);
        for (int c = 0; c < 3; c++) { Blur(rgb.Planes[c], blurred.Planes[c], temporary, 1.2f, cancellationToken); }
        JxlParallel.ForEncoderRows(height, 32, cancellationToken, (rowStart, rowEnd) =>
        {
        for (int y = rowStart; y < rowEnd; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Span<float> r = rgb.Planes[0].DangerousGetRowSpan(y), g = rgb.Planes[1].DangerousGetRowSpan(y), b = rgb.Planes[2].DangerousGetRowSpan(y);
            Span<float> br = blurred.Planes[0].DangerousGetRowSpan(y), bg = blurred.Planes[1].DangerousGetRowSpan(y), bb = blurred.Planes[2].DangerousGetRowSpan(y);
            Span<float> ox = xyb.Planes[0].DangerousGetRowSpan(y), oy = xyb.Planes[1].DangerousGetRowSpan(y), ob = xyb.Planes[2].DangerousGetRowSpan(y);
            for (int x = 0; x < width; x++)
            {
                (float s0, float s1, float s2) = Absorb(br[x] * intensity, bg[x] * intensity, bb[x] * intensity);
                s0 = Math.Max(1.7557483643287353f, s0); s1 = Math.Max(1.7557483643287353f, s1); s2 = Math.Max(12.226454707163354f, s2);
                (float c0, float c1, float c2) = Absorb(r[x] * intensity, g[x] * intensity, b[x] * intensity);
                c0 = Math.Max(1.7557483643287353f, c0 * Math.Max(1e-4f, Gamma(s0) / s0));
                c1 = Math.Max(1.7557483643287353f, c1 * Math.Max(1e-4f, Gamma(s1) / s1));
                c2 = Math.Max(12.226454707163354f, c2 * Math.Max(1e-4f, Gamma(s2) / s2));
                ox[x] = c0 - c1; oy[x] = c0 + c1; ob[x] = c2;
            }
        }
        });
        JxlFloatFrame result = Create(width, height, 10, rgb.Allocator);
        try
        {
            for (int c = 0; c < 3; c++)
            {
                Buffer2D<float> lf = result.Planes[c], mf = result.Planes[3 + c];
                Blur(xyb.Planes[c], lf, temporary, 7.15593339443f, cancellationToken);
                JxlParallel.ForEncoderRows(height, 32, cancellationToken, (rowStart, rowEnd) =>
                {
                for (int y = rowStart; y < rowEnd; y++)
                {
                    Span<float> source = xyb.Planes[c].DangerousGetRowSpan(y), low = lf.DangerousGetRowSpan(y), mid = mf.DangerousGetRowSpan(y);
                    for (int x = 0; x < width; x++) { mid[x] = source[x] - low[x]; }
                }
                });
                Blur(mf, blurred.Planes[c], temporary, 3.22489901262f, cancellationToken);
                JxlParallel.ForEncoderRows(height, 32, cancellationToken, (rowStart, rowEnd) =>
                {
                for (int y = rowStart; y < rowEnd; y++)
                {
                    Span<float> mid = mf.DangerousGetRowSpan(y), smooth = blurred.Planes[c].DangerousGetRowSpan(y);
                    Span<float> high = c < 2 ? result.Planes[6 + c].DangerousGetRowSpan(y) : default;
                    for (int x = 0; x < width; x++)
                    {
                        if (c < 2) { high[x] = mid[x] - smooth[x]; }
                        mid[x] = c == 0 ? Remove(smooth[x], .29f) : c == 1 ? Amplify(smooth[x], .1f) : smooth[x];
                    }
                }
                });
            }
            JxlParallel.ForEncoderRows(height, 32, cancellationToken, (rowStart, rowEnd) =>
            {
            for (int y = rowStart; y < rowEnd; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Span<float> lx = result.Planes[0].DangerousGetRowSpan(y), ly = result.Planes[1].DangerousGetRowSpan(y), lb = result.Planes[2].DangerousGetRowSpan(y);
                Span<float> hx = result.Planes[6].DangerousGetRowSpan(y), hy = result.Planes[7].DangerousGetRowSpan(y);
                for (int x = 0; x < width; x++)
                {
                    lb[x] = MathF.FusedMultiplyAdd(-.362267051518f, ly[x], lb[x]) * 49.87984651440f;
                    lx[x] *= 33.832837186260f; ly[x] *= 14.458268100570f;
                    hx[x] *= MathF.FusedMultiplyAdd(46f / MathF.FusedMultiplyAdd(hy[x], hy[x], 46), (float)(1 - .653020556257), .653020556257f);
                }
            }
            });
            for (int c = 0; c < 2; c++)
            {
                Blur(result.Planes[6 + c], blurred.Planes[c], temporary, 1.56416327805f, cancellationToken);
                JxlParallel.ForEncoderRows(height, 32, cancellationToken, (rowStart, rowEnd) =>
                {
                for (int y = rowStart; y < rowEnd; y++)
                {
                    Span<float> high = result.Planes[6 + c].DangerousGetRowSpan(y), ultra = result.Planes[8 + c].DangerousGetRowSpan(y), smooth = blurred.Planes[c].DangerousGetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        if (c == 0) { ultra[x] = Remove(high[x] - smooth[x], .04f); high[x] = Remove(smooth[x], 1.5f); }
                        else
                        {
                            float h = Clamp(smooth[x], 28.4691806922f);
                            ultra[x] = Clamp(high[x] - h, 5.19175294647f) * 2.69313763794f;
                            high[x] = Amplify(h * 2.155f, .132f);
                        }
                    }
                }
                });
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    internal static void Blur(Buffer2D<float> input, Buffer2D<float> output, Buffer2D<float> temporary,
        float sigma, CancellationToken cancellationToken)
    {
        int radius = Math.Max(1, (int)(2.25f * Math.Abs(sigma))), length = radius * 2 + 1;
        float[] kernel = new float[length]; double scale = -1 / (2.0 * sigma * sigma); float weight = 0;
        for (int i = -radius; i <= radius; i++) { kernel[i + radius] = (float)Math.Exp(scale * i * i); weight += kernel[i + radius]; }
        float inverse = 1 / weight; float[] normalized = new float[radius + 1];
        for (int i = 0; i <= radius; i++) { normalized[i] = kernel[i] * inverse; }
        int width = input.Width, height = input.Height;
        // Both passes compute each output row independently; the vertical pass
        // starts after every horizontal row is complete.
        JxlParallel.ForEncoderRows(height, 16, cancellationToken, (rowStart, rowEnd) =>
        {
        for (int y = rowStart; y < rowEnd; y++)
        {
            Span<float> source = input.DangerousGetRowSpan(y), row = temporary.DangerousGetRowSpan(y);
            for (int x = 0; x < width; x++)
            {
                if (length == 5)
                {
                    float a = source[x] * normalized[2];
                    a = MathF.FusedMultiplyAdd(source[Mirror(x - 1, width)] + source[Mirror(x + 1, width)], normalized[1], a);
                    row[x] = MathF.FusedMultiplyAdd(source[Mirror(x - 2, width)] + source[Mirror(x + 2, width)], normalized[0], a);
                }
                else if (x < radius || x + radius >= width)
                {
                    float sum = 0, weights = 0;
                    for (int j = Math.Max(-radius, -x); j <= Math.Min(radius, width - 1 - x); j++)
                    { weights += kernel[j + radius]; sum += source[x + j] * kernel[j + radius]; }
                    row[x] = sum * (1 / weights);
                }
                else
                {
                    float s0 = 0, s1 = 0, s2 = 0, s3 = 0;
                    for (int j = 0; j < radius; j++)
                    {
                        float value = (source[x + j - radius] + source[x + radius - j]) * normalized[j];
                        switch (j & 3) { case 0: s0 += value; break; case 1: s1 += value; break; case 2: s2 += value; break; default: s3 += value; break; }
                    }
                    float sum = source[x] * normalized[radius] + s0 + s1 + s2;
                    row[x] = radius == 3 ? sum : sum + s3;
                }
            }
        }
        });

        // Four accumulators retain the reference's exact addition order.
        // Bounded row tiles avoid per-sample Buffer2D lookups and image-sized
        // scratch allocations when accumulating the vertical convolution.
        const int tileWidth = 128;
        JxlParallel.ForEncoderRows(height, 16, cancellationToken, (rowStart, rowEnd) =>
        {
        Span<float> sums = stackalloc float[4 * tileWidth];
        for (int y = rowStart; y < rowEnd; y++)
        {
            Span<float> row = output.DangerousGetRowSpan(y), center = temporary.DangerousGetRowSpan(y);
            if (length == 5)
            {
                Span<float> above1 = temporary.DangerousGetRowSpan(Mirror(y - 1, height)), below1 = temporary.DangerousGetRowSpan(Mirror(y + 1, height));
                Span<float> above2 = temporary.DangerousGetRowSpan(Mirror(y - 2, height)), below2 = temporary.DangerousGetRowSpan(Mirror(y + 2, height));
                for (int x = 0; x < width; x++)
                {
                    float a = center[x] * normalized[2];
                    a = MathF.FusedMultiplyAdd(above1[x] + below1[x], normalized[1], a);
                    row[x] = MathF.FusedMultiplyAdd(above2[x] + below2[x], normalized[0], a);
                }
            }
            else if (y < radius || y + radius >= height)
            {
                row.Clear(); float weights = 0;
                for (int j = Math.Max(-radius, -y); j <= Math.Min(radius, height - 1 - y); j++)
                {
                    float k = kernel[j + radius]; weights += k;
                    Span<float> source = temporary.DangerousGetRowSpan(y + j);
                    for (int x = 0; x < width; x++) { row[x] += source[x] * k; }
                }
                float inverseWeights = 1 / weights;
                for (int x = 0; x < width; x++) { row[x] *= inverseWeights; }
            }
            else
            {
                for (int left = 0; left < width; left += tileWidth)
                {
                    int count = Math.Min(tileWidth, width - left); sums.Clear();
                    for (int j = 0; j < radius; j++)
                    {
                        Span<float> above = temporary.DangerousGetRowSpan(y + j - radius).Slice(left, count);
                        Span<float> below = temporary.DangerousGetRowSpan(y + radius - j).Slice(left, count);
                        Span<float> accumulator = sums.Slice((j & 3) * tileWidth, count);
                        float k = normalized[j];
                        for (int x = 0; x < count; x++) { accumulator[x] += (above[x] + below[x]) * k; }
                    }
                    for (int x = 0; x < count; x++)
                    {
                        float sum = center[left + x] * normalized[radius] + sums[x] + sums[tileWidth + x] + sums[2 * tileWidth + x];
                        row[left + x] = radius == 3 ? sum : sum + sums[3 * tileWidth + x];
                    }
                }
            }
        }
        });

        static int Mirror(int index, int size) => index < 0 ? -index - 1 : index >= size ? size * 2 - 1 - index : index;
    }

    private static float Remove(float value, float limit) => value > limit ? value - limit : value < -limit ? value + limit : 0;
    private static float Amplify(float value, float limit) => value > limit ? value + limit : value < -limit ? value - limit : value + value;
    private static float Clamp(float value, float limit) => value >= limit ? MathF.FusedMultiplyAdd(value - limit, .724216145665f, limit) :
        value < -limit ? MathF.FusedMultiplyAdd(value + limit, .724216145665f, -limit) : value;

    private static (float, float, float) Absorb(float r, float g, float b) =>
        (MathF.FusedMultiplyAdd(.29956550340058319f, r, MathF.FusedMultiplyAdd(.63373087833825936f, g, MathF.FusedMultiplyAdd(.077705617820981968f, b, 1.7557483643287353f))),
         MathF.FusedMultiplyAdd(.22158691104574774f, r, MathF.FusedMultiplyAdd(.69391388044116142f, g, MathF.FusedMultiplyAdd(.0987313588422f, b, 1.7557483643287353f))),
         MathF.FusedMultiplyAdd(.02f, r, MathF.FusedMultiplyAdd(.02f, g, MathF.FusedMultiplyAdd(.20480129041026129f, b, 12.226454707163354f))));

    private static float Gamma(float value)
    {
        float biased = Math.Max(0, value) + 9.9710635769299145f;
        int bits = BitConverter.SingleToInt32Bits(biased), exponent = (bits - 0x3f2aaaab) >> 23;
        float mantissa = BitConverter.Int32BitsToSingle(bits - (exponent << 23)) - 1;
        float p = MathF.FusedMultiplyAdd(MathF.FusedMultiplyAdd(.74245873327820566f, mantissa, 1.4287160470083755f), mantissa, -1.8503833400518310E-06f);
        float q = MathF.FusedMultiplyAdd(MathF.FusedMultiplyAdd(.17409343003366853f, mantissa, 1.0096718572241148f), mantissa, .99032814277590719f);
        return MathF.FusedMultiplyAdd(19.245013259874995f * .6931471805599453f, p / q + exponent, -23.16046239805755f);
    }
}
