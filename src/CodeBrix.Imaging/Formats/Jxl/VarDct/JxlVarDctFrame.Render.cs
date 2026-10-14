// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-render/src/vardct/{mod,generic/mod,
// transform_common}.rs, 0.12.6, f8ae722ef2d6b782941c89517d19cfbf605c4a9d
// (Apache-2.0 option).

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Color;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.VarDct;

internal sealed partial class JxlVarDctFrame
{
    internal static JxlFloatFrame Render(JxlImageHeader image, JxlFrameHeader header, JxlLfGlobal global, float[] lfDequant,
        JxlDequantMatrices matrices, JxlModularPlane[][] quant, int[] precision, JxlHfMetadata[] metadata, JxlModularPlane[] coefficients,
        List<JxlModularPlane> extras, MemoryAllocator allocator, CancellationToken cancellationToken, JxlFloatFrame lfReference, int maxDegreeOfParallelism)
    {
        int width = (int)header.SampleWidth, height = (int)header.SampleHeight, lfDimension = (int)header.GroupDimension * 8;
        int lfColumns = (width + lfDimension - 1) / lfDimension;
        int[] hs = new int[3], vs = new int[3]; GetShifts(header, hs, vs);
        bool subsampled = Array.Exists(hs, s => s != 0) || Array.Exists(vs, s => s != 0);
        using JxlFloatFrame lf = new(allocator, 3, 3);
        JxlFloatFrame result = new(allocator, 3, 3 + extras.Count) { ColorTransformed = !image.XybEncoded && !header.YCbCr };
        try
        {
            for (int c = 0; c < 3; c++)
            {
                lf.Planes[c] = allocator.Allocate2D<float>(coefficients[c].Width / 8, coefficients[c].Height / 8);
                result.Planes[c] = allocator.Allocate2D<float>(coefficients[c].Width, coefficients[c].Height, AllocationOptions.Clean);
                result.SamplingShifts[c] = BitOperations.Log2(header.Upsampling);
            }
            if (lfReference != null)
            {
                for (int c = 0; c < 3; c++)
                {
                    if (lfReference.Planes[c].Width != lf.Planes[c].Width || lfReference.Planes[c].Height != lf.Planes[c].Height)
                    { JxlThrowHelper.Invalid("low-frequency reference dimensions do not match the frame."); }
                    for (int y = 0; y < lf.Planes[c].Height; y++)
                    { lfReference.Planes[c].DangerousGetRowSpan(y).CopyTo(lf.Planes[c].DangerousGetRowSpan(y)); }
                }
            }
            for (int group = 0; lfReference == null && group < quant.Length; group++)
            {
                for (int c = 0; c < 3; c++)
                {
                    JxlModularPlane plane = quant[group][c];
                    int left = group % lfColumns * lfDimension / 8 >> hs[c], top = group / lfColumns * lfDimension / 8 >> vs[c];
                    float scale = (float)((double)lfDequant[c] * (1 << (9 - precision[group])) / ((ulong)global.GlobalScale * global.QuantLf));
                    for (int y = 0; y < plane.Height; y++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Span<int> source = plane.Row(y); Span<float> output = lf.Planes[c].DangerousGetRowSpan(top + y).Slice(left, plane.Width);
                        for (int x = 0; x < plane.Width; x++) { output[x] = source[x] * scale; }
                    }
                }
            }
            if (!subsampled && lfReference == null)
            {
                float kx = global.BaseCorrelationX + (((int)global.XFactorLf - 128f) / global.ColorFactor);
                float kb = global.BaseCorrelationB + (((int)global.BFactorLf - 128f) / global.ColorFactor);
                for (int y = 0; y < lf.Planes[0].Height; y++)
                {
                    Span<float> xr = lf.Planes[0].DangerousGetRowSpan(y), yr = lf.Planes[1].DangerousGetRowSpan(y), br = lf.Planes[2].DangerousGetRowSpan(y);
                    for (int x = 0; x < xr.Length; x++) { xr[x] += kx * yr[x]; br[x] += kb * yr[x]; }
                }
            }
            if (lfReference == null && (header.Flags & 128) == 0)
            {
                if (subsampled) { JxlThrowHelper.Invalid("adaptive LF smoothing requires equal channel sampling."); }
                SmoothLf(lf, lfDequant, global, cancellationToken);
            }
            float[] qmScale = [MathF.Pow(.8f, (int)header.XQuantScale - 2), 1, MathF.Pow(.8f, (int)header.BQuantScale - 2)];
            for (int group = 0; group < metadata.Length; group++)
            {
                JxlHfMetadata hf = metadata[group];
                int gx = group % lfColumns * lfDimension, gy = group / lfColumns * lfDimension;
                for (int by = 0; by < hf.Blocks.Height; by++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (int bx = 0; bx < hf.Blocks.Width; bx++)
                    {
                        JxlVarBlock block = hf.Blocks.DangerousGetRowSpan(by)[bx];
                        if (block.Multiplier <= 0) { continue; }
                        (int bw, int bh) = block.Transform.Blocks();
                        int w = bw * 8, h = bh * 8;
                        for (int c = 0; c < 3; c++)
                        {
                            if (!ChannelBlock(hf, bx, by, hs[c], vs[c])) { continue; }
                            int left = (gx >> hs[c]) + ((bx >> hs[c]) * 8), top = (gy >> vs[c]) + ((by >> vs[c]) * 8);
                            if (left > coefficients[c].Width - w || top > coefficients[c].Height - h ||
                                left > result.Planes[c].Width - w || top > result.Planes[c].Height - h)
                            { JxlThrowHelper.Invalid("subsampled VarDCT block exceeds its rendering plane."); }
                            float[] matrix = matrices.Get(block.Transform, c);
                            float multiplier = 65536f / (global.GlobalScale * (float)block.Multiplier) * qmScale[c];
                            bool transpose = block.Transform.Transpose();
                            for (int y = 0; y < h; y++)
                            {
                                Span<int> source = coefficients[c].Row(top + y).Slice(left, w);
                                Span<float> output = result.Planes[c].DangerousGetRowSpan(top + y).Slice(left, w);
                                for (int x = 0; x < w; x++)
                                {
                                    float value = source[x];
                                    value = Math.Abs(value) <= 1 ? value * image.Opsin[12 + c] : value - (image.Opsin[15] / value);
                                    output[x] = value * matrix[transpose ? (x * h) + y : (y * w) + x] * multiplier;
                                }
                            }
                        }
                    }
                }
                if (!subsampled)
                {
                    int w = Math.Min(lfDimension, result.Planes[0].Width - gx), h = Math.Min(lfDimension, result.Planes[0].Height - gy);
                    for (int y = 0; y < h; y++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Span<float> xr = result.Planes[0].DangerousGetRowSpan(gy + y).Slice(gx, w);
                        Span<float> yr = result.Planes[1].DangerousGetRowSpan(gy + y).Slice(gx, w), br = result.Planes[2].DangerousGetRowSpan(gy + y).Slice(gx, w);
                        for (int x = 0; x < w; x++)
                        {
                            float kx = global.BaseCorrelationX + (hf.XFromY.Get(x / 64, y / 64) / (float)global.ColorFactor);
                            float kb = global.BaseCorrelationB + (hf.BFromY.Get(x / 64, y / 64) / (float)global.ColorFactor);
                            xr[x] += kx * yr[x]; br[x] += kb * yr[x];
                        }
                    }
                }
            }
            // The three coefficient channels have independent inverse transforms. Work
            // per channel keeps transform scratch resident and avoids a task per block.
            JxlParallel.For(3, (long)width * height < 65536 ? 1 : maxDegreeOfParallelism, cancellationToken, c =>
            {
                using IMemoryOwner<float> transformMemory = allocator.Allocate<float>(256 * 256);
                using IMemoryOwner<float> dcMemory = allocator.Allocate<float>(32 * 32);
                for (int group = 0; group < metadata.Length; group++)
                {
                    JxlHfMetadata hf = metadata[group];
                    int gx = group % lfColumns * lfDimension, gy = group / lfColumns * lfDimension;
                    for (int by = 0; by < hf.Blocks.Height; by++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        for (int bx = 0; bx < hf.Blocks.Width; bx++)
                        {
                            JxlVarBlock block = hf.Blocks.DangerousGetRowSpan(by)[bx];
                            if (block.Multiplier <= 0 || !ChannelBlock(hf, bx, by, hs[c], vs[c])) { continue; }
                            (int bw, int bh) = block.Transform.Blocks();
                            int w = bw * 8, h = bh * 8;
                            int left = (gx >> hs[c]) + ((bx >> hs[c]) * 8), top = (gy >> vs[c]) + ((by >> vs[c]) * 8);
                            Span<float> values = transformMemory.Memory.Span[..(w * h)], dc = dcMemory.Memory.Span[..(bw * bh)];
                            for (int y = 0; y < h; y++) { result.Planes[c].DangerousGetRowSpan(top + y).Slice(left, w).CopyTo(values.Slice(y * w, w)); }
                            for (int y = 0; y < bh; y++) { lf.Planes[c].DangerousGetRowSpan((top / 8) + y).Slice(left / 8, bw).CopyTo(dc.Slice(y * bw, bw)); }
                            JxlDct.Transform2D(dc, bw, bh, false);
                            int logW = 5 - BitOperations.Log2((uint)bw), logH = 5 - BitOperations.Log2((uint)bh);
                            for (int y = 0; y < bh; y++)
                            {
                                for (int x = 0; x < bw; x++)
                                { values[(y * w) + x] = dc[(y * bw) + x] / (JxlInverseTransform.LfScales[y << logH] * JxlInverseTransform.LfScales[x << logW]); }
                            }
                            JxlInverseTransform.Apply(values, block.Transform);
                            for (int y = 0; y < h; y++) { values.Slice(y * w, w).CopyTo(result.Planes[c].DangerousGetRowSpan(top + y).Slice(left, w)); }
                        }
                    }
                }
            });
            // Discard transform padding before the restoration filters inspect image edges.
            for (int c = 0; c < 3; c++)
            {
                int w = (width + (1 << hs[c]) - 1) >> hs[c], h = (height + (1 << vs[c]) - 1) >> vs[c];
                Buffer2D<float> source = result.Planes[c];
                if (source.Width == w && source.Height == h) { continue; }
                Buffer2D<float> cropped = allocator.Allocate2D<float>(w, h);
                result.Planes[c] = cropped;
                try { for (int y = 0; y < h; y++) { source.DangerousGetRowSpan(y)[..w].CopyTo(cropped.DangerousGetRowSpan(y)); } }
                finally { source.Dispose(); }
            }
            for (int c = 0; c < extras.Count; c++)
            {
                JxlModularPlane plane = extras[c];
                result.Planes[3 + c] = allocator.Allocate2D<float>(plane.Width, plane.Height);
                result.SamplingShifts[3 + c] = BitOperations.Log2(header.ExtraUpsampling[c]) + (int)image.ExtraChannels[c].DimShift;
                for (int y = 0; y < plane.Height; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Span<int> source = plane.Row(y); Span<float> output = result.Planes[3 + c].DangerousGetRowSpan(y);
                    for (int x = 0; x < plane.Width; x++) { output[x] = JxlSampleConverter.ToFloat(source[x], image.ExtraChannels[c].BitDepth); }
                }
            }
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    private static bool ChannelBlock(JxlHfMetadata metadata, int x, int y, int hs, int vs)
    {
        if (hs == 0 && vs == 0) { return true; }
        // Subsampling is defined independently in each 256-pixel coefficient group.
        int lx = x % 32, ly = y % 32, sx = lx >> hs, sy = ly >> vs;
        return (sx << hs) == lx && (sy << vs) == ly && metadata.Blocks.DangerousGetRowSpan((y / 32 * 32) + sy)[(x / 32 * 32) + sx].Multiplier > 0;
    }

    internal static void SmoothLf(JxlFloatFrame frame, float[] dequant, JxlLfGlobal global, CancellationToken cancellationToken)
    {
        int width = frame.Planes[0].Width, height = frame.Planes[0].Height;
        if (width <= 2 || height <= 2) { return; }
        using JxlFloatFrame scratch = frame.CreateColorScratch();
        float[] scale = new float[3];
        for (int c = 0; c < 3; c++)
        {
            scale[c] = (float)(512.0 * dequant[c] / ((ulong)global.GlobalScale * global.QuantLf));
            for (int y = 0; y < height; y++) { frame.Planes[c].DangerousGetRowSpan(y).CopyTo(scratch.Planes[c].DangerousGetRowSpan(y)); }
        }
        Span<float> average = stackalloc float[3];
        for (int y = 1; y < height - 1; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = 1; x < width - 1; x++)
            {
                float gap = .5f;
                for (int c = 0; c < 3; c++)
                {
                    Span<float> previous = scratch.Planes[c].DangerousGetRowSpan(y - 1), row = scratch.Planes[c].DangerousGetRowSpan(y), next = scratch.Planes[c].DangerousGetRowSpan(y + 1);
                    float side = row[x - 1] + row[x + 1] + (previous[x] + next[x]);
                    float diagonal = (previous[x - 1] + next[x - 1]) + (previous[x + 1] + next[x + 1]);
                    average[c] = (row[x] * .052262735f) + (side * .2034514f) + (diagonal * .03348292f);
                    gap = Math.Max(gap, Math.Abs(average[c] - row[x]) / scale[c]);
                }
                float amount = Math.Max(0, 3 - (4 * gap));
                for (int c = 0; c < 3; c++)
                {
                    float value = scratch.Planes[c].DangerousGetRowSpan(y)[x];
                    frame.Planes[c].DangerousGetRowSpan(y)[x] = ((average[c] - value) * amount) + value;
                }
            }
        }
    }
}
