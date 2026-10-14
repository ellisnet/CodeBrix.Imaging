// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Scalar convolution retained from the managed adaptation of libjxl
// butteraugli/butteraugli.cc and enc_convolve_separable5.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Butteraugli;
using CodeBrix.Imaging.Memory;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlButteraugliBlurTests(ITestOutputHelper output)
{
    public static IEnumerable<TheoryDataRow<int, int, float>> Shapes()
    {
        foreach ((int width, int height) in new[] { (2, 2), (3, 17), (7, 13), (33, 31), (127, 129), (128, 129), (129, 127), (257, 193), (512, 384) })
        {
            foreach (float sigma in new[] { 1.2f, 7.15593339443f, 3.22489901262f, 1.56416327805f, 2.7f })
            { yield return new(width, height, sigma); }
        }
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void row_tiled_blur_retains_every_scalar_result_bit(int width, int height, float sigma)
    {
        var allocator = Configuration.Default.MemoryAllocator;
        using Buffer2D<float> input = allocator.Allocate2D<float>(width, height);
        using Buffer2D<float> actual = allocator.Allocate2D<float>(width, height);
        using Buffer2D<float> expected = allocator.Allocate2D<float>(width, height);
        using Buffer2D<float> temporary = allocator.Allocate2D<float>(width, height);
        for (int y = 0; y < height; y++)
        {
            Span<float> row = input.DangerousGetRowSpan(y);
            for (int x = 0; x < width; x++)
            {
                float value = ((x * 37 + y * 191 + x * y * 17) % 257 - 128) * .0078125f;
                row[x] = (x + y) % 13 == 0 ? value * 1024 : value;
            }
        }

        long start = Stopwatch.GetTimestamp();
        ScalarBlur(input, expected, temporary, sigma);
        TimeSpan scalarTime = Stopwatch.GetElapsedTime(start);
        start = Stopwatch.GetTimestamp();
        JxlButteraugliKernels.Blur(input, actual, temporary, sigma, TestContext.Current.CancellationToken);
        TimeSpan tiledTime = Stopwatch.GetElapsedTime(start);
        output.WriteLine($"{width}x{height}, sigma {sigma}: scalar {scalarTime.TotalMilliseconds:F3} ms, row tiles {tiledTime.TotalMilliseconds:F3} ms");
        for (int y = 0; y < height; y++)
        {
            ReadOnlySpan<float> a = actual.DangerousGetRowSpan(y), e = expected.DangerousGetRowSpan(y);
            for (int x = 0; x < width; x++)
            { Assert.Equal(BitConverter.SingleToInt32Bits(e[x]), BitConverter.SingleToInt32Bits(a[x])); }
        }
    }

    private static void ScalarBlur(Buffer2D<float> input, Buffer2D<float> output, Buffer2D<float> temporary, float sigma)
    {
        // Previous scalar implementation, deliberately kept in its original
        // pixel/tap order as a numerical regression oracle for the optimization.
        int radius = Math.Max(1, (int)(2.25f * Math.Abs(sigma))), length = radius * 2 + 1;
        float[] kernel = new float[length]; double scale = -1 / (2.0 * sigma * sigma); float weight = 0;
        for (int i = -radius; i <= radius; i++) { kernel[i + radius] = (float)Math.Exp(scale * i * i); weight += kernel[i + radius]; }
        float inverse = 1 / weight; float[] normalized = new float[radius + 1];
        for (int i = 0; i <= radius; i++) { normalized[i] = kernel[i] * inverse; }
        for (int direction = 0; direction < 2; direction++)
        {
            Buffer2D<float> source = direction == 0 ? input : temporary, destination = direction == 0 ? temporary : output;
            for (int y = 0; y < input.Height; y++)
            {
                TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
                Span<float> row = destination.DangerousGetRowSpan(y);
                for (int x = 0; x < input.Width; x++)
                {
                    int position = direction == 0 ? x : y, size = direction == 0 ? input.Width : input.Height;
                    if (length == 5)
                    {
                        float a = Read(0) * normalized[2];
                        a = MathF.FusedMultiplyAdd(Read(-1) + Read(1), normalized[1], a);
                        row[x] = MathF.FusedMultiplyAdd(Read(-2) + Read(2), normalized[0], a);
                    }
                    else if (position < radius || position + radius >= size)
                    {
                        float sum = 0, weights = 0;
                        for (int j = Math.Max(-radius, -position); j <= Math.Min(radius, size - 1 - position); j++)
                        { weights += kernel[j + radius]; sum += Read(j) * kernel[j + radius]; }
                        row[x] = sum * (1 / weights);
                    }
                    else
                    {
                        float s0 = 0, s1 = 0, s2 = 0, s3 = 0;
                        for (int j = 0; j < radius; j++)
                        {
                            float value = (Read(j - radius) + Read(radius - j)) * normalized[j];
                            switch (j & 3) { case 0: s0 += value; break; case 1: s1 += value; break; case 2: s2 += value; break; default: s3 += value; break; }
                        }
                        float sum = Read(0) * normalized[radius] + s0 + s1 + s2;
                        row[x] = radius == 3 ? sum : sum + s3;
                    }

                    float Read(int offset)
                    {
                        int index = position + offset;
                        if (length == 5) { index = index < 0 ? -index - 1 : index >= size ? size * 2 - 1 - index : index; }
                        return source.DangerousGetRowSpan(direction == 0 ? y : index)[direction == 0 ? index : x];
                    }
                }
            }
        }
    }
}
