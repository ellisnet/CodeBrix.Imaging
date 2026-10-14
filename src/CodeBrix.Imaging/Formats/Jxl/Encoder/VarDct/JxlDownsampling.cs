// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from libjxl lib/jxl/enc_heuristics.cc (DownsampleImage2_Sharper,
// DownsampleImage2_Iterative, CreateMask and AntiUpsample) and image_ops.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Color;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;

internal static class JxlDownsampling
{
    // The 12x12 sharper kernel is symmetric in both axes; retain one quadrant.
    private static ReadOnlySpan<float> SharperKernel =>
    [
        -.000314256996835f, -.000314256996835f, -.000897597057705f, -.000562751488849f, -.000176807273646f, .001864627368902f,
        -.000314256996835f, -.001527942804748f, -.000121760530512f, .000191123989093f, .010193185932466f, .058637519197110f,
        -.000897597057705f, -.000121760530512f, .000946363683751f, .007113577630288f, .000437956841058f, -.000372823835211f,
        -.000562751488849f, .000191123989093f, .007113577630288f, .044592622228814f, .000222278879007f, -.162864473015945f,
        -.000176807273646f, .010193185932466f, .000437956841058f, .000222278879007f, -.000913092543974f, -.017071696107902f,
        .001864627368902f, .058637519197110f, -.000372823835211f, -.162864473015945f, -.017071696107902f, .414660099370354f,
    ];
    private static ReadOnlySpan<float> UpsamplingKernel =>
    [
        -.01716200f, -.03452303f, -.04022174f, -.02921014f, -.00624645f,
        -.03452303f, .14111091f, .28896755f, .00278718f, -.01610267f,
        -.04022174f, .28896755f, .56661550f, .03777607f, -.01986694f,
        -.02921014f, .00278718f, .03777607f, -.03144731f, -.01185068f,
        -.00624645f, -.01610267f, -.01986694f, -.01185068f, -.00213539f,
    ];

    internal static void Apply(JxlFloatFrame frame, int factor, int effort, CancellationToken cancellationToken, bool sharpen = true)
    {
        if (factor is not (2 or 4 or 8)) { throw new ArgumentOutOfRangeException(nameof(factor)); }
        for (int c = 0; c < frame.ColorChannels; c++)
        {
            Buffer2D<float> source = frame.Planes[c];
            Buffer2D<float> output = sharpen ? Downsample(source, factor, effort, frame.Allocator, cancellationToken)
                : Box(source, factor, frame.Allocator, cancellationToken);
            frame.Planes[c] = output; source.Dispose();
        }
    }

    internal static Buffer2D<float> Downsample(Buffer2D<float> source, int factor, int effort, MemoryAllocator allocator, CancellationToken cancellationToken)
    {
        if (factor is not (2 or 4 or 8)) { throw new ArgumentOutOfRangeException(nameof(factor)); }
        Buffer2D<float> result = Box(source, factor, allocator, cancellationToken);
        if (factor != 2) { return result; }
        try
        {
            using Buffer2D<float> mask = allocator.Allocate2D<float>(result.Width, result.Height);
            MakeMask(result, mask, cancellationToken);
            for (int y = 0; y < result.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Span<float> row = result.DangerousGetRowSpan(y), masks = mask.DangerousGetRowSpan(y);
                for (int x = 0; x < result.Width; x++)
                {
                    float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity, sum = 0;
                    for (int ky = 0; ky < 12; ky++)
                    {
                        Span<float> input = source.DangerousGetRowSpan(Math.Clamp(y * 2 + ky - 5, 0, source.Height - 1));
                        for (int kx = 0; kx < 12; kx++)
                        {
                            float value = input[Math.Clamp(x * 2 + kx - 5, 0, source.Width - 1)];
                            if (ky is 5 or 6 && kx is 5 or 6) { minimum = Math.Min(minimum, value); maximum = Math.Max(maximum, value); }
                            sum += value * SharperKernel[Math.Min(ky, 11 - ky) * 6 + Math.Min(kx, 11 - kx)];
                        }
                    }
                    row[x] = Math.Clamp(sum, minimum - masks[x], maximum + masks[x]);
                }
            }
            if (effort >= 10) { Refine(source, result, mask, allocator, cancellationToken); }
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    internal static Buffer2D<float> Box(Buffer2D<float> source, int factor, MemoryAllocator allocator, CancellationToken cancellationToken)
    {
        if (factor is not (2 or 4 or 8)) { throw new ArgumentOutOfRangeException(nameof(factor)); }
        Buffer2D<float> output = allocator.Allocate2D<float>((source.Width + factor - 1) / factor, (source.Height + factor - 1) / factor);
        try
        {
            for (int y = 0; y < output.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested(); Span<float> row = output.DangerousGetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    float sum = 0; int count = 0;
                    for (int dy = 0; dy < factor && y * factor + dy < source.Height; dy++)
                    {
                        Span<float> input = source.DangerousGetRowSpan(y * factor + dy);
                        for (int dx = 0; dx < factor && x * factor + dx < input.Length; dx++) { sum += input[x * factor + dx]; count++; }
                    }
                    row[x] = sum / count;
                }
            }
            return output;
        }
        catch { output.Dispose(); throw; }
    }

    internal static JxlModularPlane[] ExtraChannels(JxlModularPlane[] source, JxlImageHeader image, int factor,
        JxlModularWorkspace workspace, CancellationToken cancellationToken)
    {
        JxlModularPlane[] result = new JxlModularPlane[source.Length];
        for (int c = 0; c < source.Length; c++)
        {
            JxlModularPlane input = source[c]; JxlBitDepth depth = image.ExtraChannels[c].BitDepth;
            JxlModularPlane output = workspace.Allocate((input.Width + factor - 1) / factor, (input.Height + factor - 1) / factor,
                input.HorizontalShift, input.VerticalShift);
            output.OriginalWidth = (input.OriginalWidth + factor - 1) / factor; output.OriginalHeight = (input.OriginalHeight + factor - 1) / factor;
            result[c] = output;
            for (int y = 0; y < output.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested(); Span<int> row = output.Row(y);
                for (int x = 0; x < row.Length; x++)
                {
                    double sum = 0; int count = 0;
                    for (int iy = y * factor; iy < Math.Min(input.Height, (y + 1) * factor); iy++)
                    {
                        Span<int> samples = input.Row(iy);
                        for (int ix = x * factor; ix < Math.Min(input.Width, (x + 1) * factor); ix++)
                        { sum += depth.ExponentBits == 0 ? samples[ix] : JxlSampleConverter.ToFloat(samples[ix], depth); count++; }
                    }
                    row[x] = depth.ExponentBits == 0 ? (int)Math.Round(sum / count) : depth.ExponentBits == 5
                        ? HalfTypeHelper.Pack((float)(sum / count)) : BitConverter.SingleToInt32Bits((float)(sum / count));
                }
            }
        }
        return result;
    }

    private static void MakeMask(Buffer2D<float> source, Buffer2D<float> mask, CancellationToken cancellationToken)
    {
        Span<float> differences = stackalloc float[4];
        for (int y = 0; y < source.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Span<float> row = source.DangerousGetRowSpan(y), above = source.DangerousGetRowSpan(Math.Max(0, y - 1));
            Span<float> below = source.DangerousGetRowSpan(Math.Min(source.Height - 1, y + 1)), output = mask.DangerousGetRowSpan(y);
            for (int x = 0; x < row.Length; x++)
            {
                differences[0] = Math.Abs(row[x] - row[Math.Max(0, x - 1)]); differences[1] = Math.Abs(row[x] - row[Math.Min(row.Length - 1, x + 1)]);
                differences[2] = Math.Abs(row[x] - above[x]); differences[3] = Math.Abs(row[x] - below[x]); differences.Sort(); output[x] = differences[1];
            }
        }
    }

    private static void Refine(Buffer2D<float> source, Buffer2D<float> down, Buffer2D<float> mask, MemoryAllocator allocator, CancellationToken cancellationToken)
    {
        using Buffer2D<float> initial = allocator.Allocate2D<float>(down.Width, down.Height);
        using Buffer2D<float> correction = allocator.Allocate2D<float>(source.Width, source.Height);
        using Buffer2D<float> gradient = allocator.Allocate2D<float>(down.Width, down.Height);
        using Buffer2D<float> weights = allocator.Allocate2D<float>(down.Width, down.Height);
        for (int y = 0; y < down.Height; y++) { down.DangerousGetRowSpan(y).CopyTo(initial.DangerousGetRowSpan(y)); }
        AntiUpsample(source, weights, true, cancellationToken);
        for (int iteration = 0; iteration < 3; iteration++)
        {
            for (int y = 0; y < source.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Span<float> row = correction.DangerousGetRowSpan(y), original = source.DangerousGetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    float sum = 0, minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
                    for (int ky = 0; ky < 5; ky++)
                    {
                        Span<float> input = down.DangerousGetRowSpan(Math.Clamp(y / 2 + ky - 2, 0, down.Height - 1));
                        for (int kx = 0; kx < 5; kx++)
                        {
                            float value = input[Math.Clamp(x / 2 + kx - 2, 0, down.Width - 1)];
                            sum += value * Kernel(x, y, kx, ky); minimum = Math.Min(minimum, value); maximum = Math.Max(maximum, value);
                        }
                    }
                    row[x] = original[x] - Math.Clamp(sum, minimum, maximum);
                }
            }
            AntiUpsample(correction, gradient, false, cancellationToken);
            for (int y = 0; y < down.Height; y++)
            {
                Span<float> row = down.DangerousGetRowSpan(y), d = gradient.DangerousGetRowSpan(y), w = weights.DangerousGetRowSpan(y);
                for (int x = 0; x < row.Length; x++) { row[x] += d[x] / w[x]; }
            }
        }
        for (int y = 0; y < down.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested(); Span<float> row = down.DangerousGetRowSpan(y), masks = mask.DangerousGetRowSpan(y);
            for (int x = 0; x < row.Length; x++)
            {
                float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
                for (int iy = Math.Max(0, y - 1); iy <= Math.Min(down.Height - 1, y + 1); iy++)
                {
                    Span<float> input = initial.DangerousGetRowSpan(iy);
                    for (int ix = Math.Max(0, x - 1); ix <= Math.Min(down.Width - 1, x + 1); ix++)
                    { minimum = Math.Min(minimum, input[ix]); maximum = Math.Max(maximum, input[ix]); }
                }
                row[x] = Math.Clamp(row[x], minimum - 2 * masks[x], maximum + 2 * masks[x]);
            }
        }
    }

    private static void AntiUpsample(Buffer2D<float> source, Buffer2D<float> destination, bool uniform, CancellationToken cancellationToken)
    {
        for (int y = 0; y < destination.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested(); Span<float> row = destination.DangerousGetRowSpan(y);
            for (int x = 0; x < row.Length; x++)
            {
                float sum = 0;
                for (int iy = Math.Max(0, y * 2 - 4); iy < Math.Min(source.Height, y * 2 + 6); iy++)
                {
                    Span<float> input = source.DangerousGetRowSpan(iy);
                    for (int ix = Math.Max(0, x * 2 - 4); ix < Math.Min(source.Width, x * 2 + 6); ix++)
                    { sum = (float)(sum + (double)Kernel(ix, iy, x - ix / 2 + 2, y - iy / 2 + 2) * (uniform ? 1 : input[ix])); }
                }
                row[x] = sum;
            }
        }
    }

    private static float Kernel(int x, int y, int kx, int ky) => UpsamplingKernel[((y & 1) == 0 ? ky : 4 - ky) * 5 + ((x & 1) == 0 ? kx : 4 - kx)];
}
