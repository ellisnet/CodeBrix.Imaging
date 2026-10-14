// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-render/src/features/upsampling.rs,
// filter/ycbcr.rs and image.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

using System;
using System.Numerics;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Rendering;

internal static class JxlUpsampling
{
    internal static void Apply(JxlFloatFrame frame, JxlImageHeader image, JxlFrameHeader header,
        bool extraToColorOnly, CancellationToken cancellationToken)
    {
        int targetShift = extraToColorOnly ? BitOperations.Log2(header.Upsampling) : 0;
        int width = (int)JxlFrameHeader.DivideRoundUp(header.Width, 1U << (targetShift + (3 * (int)header.LfLevel)));
        int height = (int)JxlFrameHeader.DivideRoundUp(header.Height, 1U << (targetShift + (3 * (int)header.LfLevel)));
        for (int c = 0; c < frame.Planes.Length; c++)
        {
            int shift = frame.SamplingShifts[c] - targetShift;
            if (shift < 0) { JxlThrowHelper.Invalid("extra channel has invalid sampling resolution."); }
            while (shift > 0)
            {
                int step = Math.Min(3, shift), factor = 1 << step;
                Buffer2D<float> source = frame.Planes[c];
                int outWidth = shift == step ? width : checked(source.Width * factor);
                int outHeight = shift == step ? height : checked(source.Height * factor);
                float[] weights = step == 1 ? image.Up2 : step == 2 ? image.Up4 : image.Up8;
                Buffer2D<float> output = Upsample(source, frame.Allocator, weights, factor, outWidth, outHeight, cancellationToken);
                frame.Planes[c] = output;
                source.Dispose();
                shift -= step;
                frame.SamplingShifts[c] -= step;
            }
        }
    }

    internal static Buffer2D<float> Upsample(Buffer2D<float> source, MemoryAllocator allocator, float[] weights,
        int factor, int width, int height, CancellationToken cancellationToken)
    {
        int half = factor / 2;
        float[] kernels = new float[half * half * 25];
        int index = 0;
        for (int y = 0; y < 5 * half; y++)
        {
            for (int x = y; x < 5 * half; x++)
            {
                float weight = weights[index++];
                kernels[(((y / 5 * half) + (x / 5)) * 25) + ((y % 5) * 5) + (x % 5)] = weight;
                kernels[(((x / 5 * half) + (y / 5)) * 25) + ((x % 5) * 5) + (y % 5)] = weight;
            }
        }
        Buffer2D<float> output = allocator.Allocate2D<float>(width, height);
        try
        {
            for (int y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Span<float> target = output.DangerousGetRowSpan(y);
                int phaseY = y % factor, matrixY = Math.Min(phaseY, factor - phaseY - 1);
                for (int x = 0; x < width; x++)
                {
                    int phaseX = x % factor, matrixX = Math.Min(phaseX, factor - phaseX - 1);
                    int kernel = ((matrixY * half) + matrixX) * 25;
                    float sum = 0, min = float.PositiveInfinity, max = float.NegativeInfinity;
                    for (int iy = 0; iy < 5; iy++)
                    {
                        Span<float> row = source.DangerousGetRowSpan(JxlRestoration.Mirror((y / factor) + iy - 2, source.Height));
                        int ky = phaseY >= half ? 4 - iy : iy;
                        for (int ix = 0; ix < 5; ix++)
                        {
                            int kx = phaseX >= half ? 4 - ix : ix;
                            float sample = row[JxlRestoration.Mirror((x / factor) + ix - 2, source.Width)];
                            sum += kernels[kernel + (ky * 5) + kx] * sample;
                            if (!float.IsNaN(sample)) { min = Math.Min(min, sample); max = Math.Max(max, sample); }
                        }
                    }
                    target[x] = float.IsFinite(min) ? Math.Clamp(sum, min, max) : float.NaN;
                }
            }
            return output;
        }
        catch { output.Dispose(); throw; }
    }

    internal static void ApplyJpeg(JxlFloatFrame frame, JxlFrameHeader header, CancellationToken cancellationToken)
    {
        bool horizontal = Array.Exists(header.JpegUpsampling, x => x is 1 or 2);
        bool vertical = Array.Exists(header.JpegUpsampling, x => x is 1 or 3);
        int width = (int)header.SampleWidth, height = (int)header.SampleHeight;
        for (int c = 0; c < 3; c++)
        {
            bool hx = horizontal && header.JpegUpsampling[c] is 0 or 3;
            bool vy = vertical && header.JpegUpsampling[c] is 0 or 2;
            Buffer2D<float> source = frame.Planes[c];
            using Buffer2D<float> intermediate = frame.Allocator.Allocate2D<float>(width, source.Height);
            for (int y = 0; y < source.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Span<float> input = source.DangerousGetRowSpan(y), output = intermediate.DangerousGetRowSpan(y);
                for (int x = 0; x < width; x++)
                {
                    int center = hx ? x / 2 : x;
                    int neighbor = Math.Clamp(center + (x % 2 == 0 ? -1 : 1), 0, input.Length - 1);
                    output[x] = hx ? (0.75f * input[center]) + (0.25f * input[neighbor]) : input[x];
                }
            }
            Buffer2D<float> result = frame.Allocator.Allocate2D<float>(width, height);
            try
            {
                for (int y = 0; y < height; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int center = vy ? y / 2 : y;
                    int neighbor = Math.Clamp(center + (y % 2 == 0 ? -1 : 1), 0, source.Height - 1);
                    Span<float> row = intermediate.DangerousGetRowSpan(center), adjacent = intermediate.DangerousGetRowSpan(neighbor);
                    Span<float> output = result.DangerousGetRowSpan(y);
                    for (int x = 0; x < width; x++) { output[x] = vy ? (0.75f * row[x]) + (0.25f * adjacent[x]) : row[x]; }
                }
                frame.Planes[c] = result;
                result = null;
                source.Dispose();
            }
            finally { result?.Dispose(); }
        }
    }
}
