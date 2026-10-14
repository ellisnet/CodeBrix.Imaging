// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-render/src/filter/epf.rs,
// filter/impls/generic/{gabor,epf}.rs and util.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Rendering;

internal static class JxlRestoration
{
    internal static void Apply(JxlFloatFrame frame, JxlRestorationFilter filter, CancellationToken cancellationToken, Buffer2D<float> sigma = null,
        int maxDegreeOfParallelism = 1)
    {
        bool differentGrayWeights = frame.ColorChannels == 1 && filter.GaborEnabled &&
            (filter.GaborWeights[0] != filter.GaborWeights[2] || filter.GaborWeights[0] != filter.GaborWeights[4] ||
             filter.GaborWeights[1] != filter.GaborWeights[3] || filter.GaborWeights[1] != filter.GaborWeights[5]);
        if (differentGrayWeights)
        {
            using JxlFloatFrame rgb = new(frame.Allocator, 3, 3);
            for (int c = 0; c < 3; c++)
            {
                rgb.Planes[c] = frame.Allocator.Allocate2D<float>(frame.Planes[0].Width, frame.Planes[0].Height);
                for (int y = 0; y < frame.Planes[0].Height; y++) { frame.Planes[0].DangerousGetRowSpan(y).CopyTo(rgb.Planes[c].DangerousGetRowSpan(y)); }
            }
            Apply(rgb, filter, cancellationToken, sigma, maxDegreeOfParallelism);
            (frame.Planes[0], rgb.Planes[0]) = (rgb.Planes[0], frame.Planes[0]);
            return;
        }
        if (!filter.GaborEnabled && filter.EpfIterations == 0) { return; }
        using JxlFloatFrame scratch = frame.CreateColorScratch();
        if (filter.GaborEnabled)
        {
            Gabor(frame, scratch, filter, cancellationToken);
            frame.SwapColors(scratch);
        }
        if (filter.EpfIterations == 0) { return; }
        if (filter.EpfIterations == 3) { Epf(frame, scratch, filter, sigma, 0, maxDegreeOfParallelism, cancellationToken); frame.SwapColors(scratch); }
        Epf(frame, scratch, filter, sigma, 1, maxDegreeOfParallelism, cancellationToken); frame.SwapColors(scratch);
        if (filter.EpfIterations >= 2) { Epf(frame, scratch, filter, sigma, 2, maxDegreeOfParallelism, cancellationToken); frame.SwapColors(scratch); }
    }

    private static void Gabor(JxlFloatFrame input, JxlFloatFrame output, JxlRestorationFilter filter, CancellationToken cancellationToken)
    {
        for (int c = 0; c < input.ColorChannels; c++)
        {
            Buffer2D<float> plane = input.Planes[c];
            float side = filter.GaborWeights[c * 2], diagonal = filter.GaborWeights[(c * 2) + 1];
            float normalization = 1 / (1 + (4 * side) + (4 * diagonal));
            for (int y = 0; y < plane.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Span<float> top = plane.DangerousGetRowSpan(Math.Max(0, y - 1));
                Span<float> row = plane.DangerousGetRowSpan(y);
                Span<float> bottom = plane.DangerousGetRowSpan(Math.Min(plane.Height - 1, y + 1));
                Span<float> target = output.Planes[c].DangerousGetRowSpan(y);
                for (int x = 0; x < plane.Width; x++)
                {
                    int left = Math.Max(0, x - 1), right = Math.Min(plane.Width - 1, x + 1);
                    float sides = top[x] + row[left] + row[right] + bottom[x];
                    float diagonals = top[left] + top[right] + bottom[left] + bottom[right];
                    target[x] = (row[x] + (sides * side) + (diagonals * diagonal)) * normalization;
                }
            }
        }
    }

    private static void Epf(JxlFloatFrame input, JxlFloatFrame output, JxlRestorationFilter filter, Buffer2D<float> sigma, int step,
        int maxDegreeOfParallelism, CancellationToken cancellationToken)
    {
        int width = input.Planes[0].Width, height = input.Planes[0].Height;
        for (int c = 0; c < input.ColorChannels; c++)
        {
            if (input.Planes[c].Width != width || input.Planes[c].Height != height ||
                output.Planes[c].Width != width || output.Planes[c].Height != height)
            { JxlThrowHelper.Invalid("EPF color planes must have matching dimensions."); }
        }
        if (maxDegreeOfParallelism == 1 || (long)width * height < 65536)
        {
            using Buffer2D<float> work = input.Allocator.Allocate2D<float>(width, 4);
            for (int y = 0; y < height; y++) { EpfRow(input, output, filter, sigma, step, y, work, cancellationToken); }
            return;
        }
        try
        {
            Parallel.For(0, height, new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism, CancellationToken = cancellationToken },
            () => input.Allocator.Allocate2D<float>(width, 4),
            (y, _, work) => { EpfRow(input, output, filter, sigma, step, y, work, cancellationToken); return work; },
                work => work.Dispose());
        }
        catch (AggregateException error) { JxlParallel.Rethrow(error); }
    }

    private static void EpfRow(JxlFloatFrame input, JxlFloatFrame output, JxlRestorationFilter filter, Buffer2D<float> sigma,
        int step, int y, Buffer2D<float> work, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadOnlySpan<(int X, int Y)> kernel = step == 0
            ? [(0, -2), (-1, -1), (0, -1), (1, -1), (-2, 0), (-1, 0), (1, 0), (2, 0), (-1, 1), (0, 1), (1, 1), (0, 2)]
            : [(0, -1), (0, 1), (-1, 0), (1, 0)];
        ReadOnlySpan<(int X, int Y)> distances = step == 0 ? [(0, -1), (1, 0), (0, 0), (-1, 0), (0, 1)] :
            step == 1 ? [(0, -1), (0, 0), (0, 1), (-1, 0), (1, 0)] : [(0, 0)];
        float multiplier = step == 0 ? filter.Pass0Scale : step == 2 ? filter.Pass2Scale : 1;
        int width = input.Planes[0].Width, height = input.Planes[0].Height, colors = input.ColorChannels;
        Span<float> weightSum = work.DangerousGetRowSpan(0), distance = work.DangerousGetRowSpan(1);
        Span<float> accumulated = work.DangerousGetRowSpan(2), negativeInverse = work.DangerousGetRowSpan(3);
        ReadOnlySpan<float> sigmaRow = sigma == null ? default : sigma.DangerousGetRowSpan(y / 8);
        weightSum.Fill(1);
        for (int c = 0; c < colors; c++) { input.Planes[c].DangerousGetRowSpan(y).CopyTo(output.Planes[c].DangerousGetRowSpan(y)); }
        bool active = false;
        for (int x = 0; x < width; x++)
        {
            float strength = sigma == null ? filter.ModularSigma : sigmaRow[x / 8];
            active |= strength >= .3f;
            float scale = multiplier;
            if (((y + 1) & 6) == 0 || (x & 7) is 0 or 7) { scale *= filter.BorderMultiplier; }
            negativeInverse[x] = 6.6f * (0.7071067811865476f - 1) / strength * scale;
        }
        if (!active) { return; }
        foreach ((int kx, int ky) in kernel)
        {
            for (int c = 0; c < colors; c++)
            {
                bool first = true;
                Buffer2D<float> plane = input.Planes[c];
                foreach ((int dx, int dy) in distances)
                {
                    ReadOnlySpan<float> neighbor = plane.DangerousGetRowSpan(Mirror(y + ky + dy, height));
                    ReadOnlySpan<float> center = plane.DangerousGetRowSpan(Mirror(y + dy, height));
                    // Row lookup and vertical mirroring are invariant across the scanline.
                    // Keep the original channel/tap summation order for conformance precision.
                    AddAbsoluteDifference(accumulated, neighbor, center, kx + dx, dx, first);
                    first = false;
                }
                for (int x = 0; x < width; x++)
                {
                    float value = accumulated[x];
                    if (colors == 1) { distance[x] = (filter.ChannelScale[0] * value) + (filter.ChannelScale[1] * value) + (filter.ChannelScale[2] * value); }
                    else { distance[x] = (c == 0 ? 0 : distance[x]) + (filter.ChannelScale[c] * value); }
                }
            }
            for (int x = 0; x < width; x++)
            {
                if (!((sigma == null ? filter.ModularSigma : sigmaRow[x / 8]) >= .3f)) { continue; }
                float weight = Math.Max(0, 1 + (distance[x] * negativeInverse[x]));
                accumulated[x] = weight; weightSum[x] += weight;
            }
            for (int c = 0; c < colors; c++)
            {
                ReadOnlySpan<float> neighbor = input.Planes[c].DangerousGetRowSpan(Mirror(y + ky, height));
                Span<float> target = output.Planes[c].DangerousGetRowSpan(y);
                for (int x = 0; x < width; x++)
                {
                    if (!((sigma == null ? filter.ModularSigma : sigmaRow[x / 8]) >= .3f)) { continue; }
                    target[x] += accumulated[x] * neighbor[Mirror(x + kx, width)];
                }
            }
        }
        for (int c = 0; c < colors; c++)
        {
            Span<float> target = output.Planes[c].DangerousGetRowSpan(y);
            for (int x = 0; x < width; x++) { target[x] /= weightSum[x]; }
        }
    }

    private static void AddAbsoluteDifference(Span<float> sum, ReadOnlySpan<float> neighbor, ReadOnlySpan<float> center,
        int neighborOffset, int centerOffset, bool initialize)
    {
        int width = sum.Length, x = 0;
        int start = Math.Min(width, Math.Max(0, -Math.Min(neighborOffset, centerOffset)));
        int end = Math.Max(start, Math.Min(width, width - Math.Max(neighborOffset, centerOffset)));
        for (; x < start; x++) { sum[x] = (initialize ? 0 : sum[x]) + Math.Abs(neighbor[Mirror(x + neighborOffset, width)] - center[Mirror(x + centerOffset, width)]); }
        ref float a = ref MemoryMarshal.GetReference(neighbor), b = ref MemoryMarshal.GetReference(center), target = ref MemoryMarshal.GetReference(sum);
        if (Vector256.IsHardwareAccelerated)
        {
            for (; x <= end - Vector256<float>.Count; x += Vector256<float>.Count)
            {
                Vector256<float> difference = Vector256.LoadUnsafe(ref a, (nuint)(x + neighborOffset)) - Vector256.LoadUnsafe(ref b, (nuint)(x + centerOffset));
                ((initialize ? Vector256<float>.Zero : Vector256.LoadUnsafe(ref target, (nuint)x)) + Vector256.Abs(difference)).StoreUnsafe(ref target, (nuint)x);
            }
        }
        if (Vector128.IsHardwareAccelerated)
        {
            for (; x <= end - Vector128<float>.Count; x += Vector128<float>.Count)
            {
                Vector128<float> difference = Vector128.LoadUnsafe(ref a, (nuint)(x + neighborOffset)) - Vector128.LoadUnsafe(ref b, (nuint)(x + centerOffset));
                ((initialize ? Vector128<float>.Zero : Vector128.LoadUnsafe(ref target, (nuint)x)) + Vector128.Abs(difference)).StoreUnsafe(ref target, (nuint)x);
            }
        }
        for (; x < end; x++) { sum[x] = (initialize ? 0 : sum[x]) + Math.Abs(neighbor[x + neighborOffset] - center[x + centerOffset]); }
        for (; x < width; x++) { sum[x] = (initialize ? 0 : sum[x]) + Math.Abs(neighbor[Mirror(x + neighborOffset, width)] - center[Mirror(x + centerOffset, width)]); }
    }

    internal static int Mirror(int coordinate, int length)
    {
        while ((uint)coordinate >= length) { coordinate = coordinate < 0 ? -coordinate - 1 : (2 * length) - coordinate - 1; }
        return coordinate;
    }
}
