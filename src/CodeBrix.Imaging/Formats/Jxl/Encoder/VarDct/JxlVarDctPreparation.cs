// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Initial quantization and scale selection adapted from libjxl lib/jxl/
// enc_heuristics.cc, enc_adaptive_quantization.cc, quantizer.cc and enc_group.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// LF extraction inverts the attributed jxl-oxide managed reconstruction.
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Buffers;
using System.Numerics;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Formats.Jxl.VarDct;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;

internal sealed class JxlVarDctPreparation : IDisposable
{
    private readonly JxlModularWorkspace workspace;
    internal readonly JxlModularPlane[] Coefficients = new JxlModularPlane[3];
    internal readonly JxlModularPlane[] LowFrequency = new JxlModularPlane[3];
    internal readonly Buffer2D<JxlVarBlock> Blocks;
    internal readonly JxlModularPlane XFromY, BFromY;
    internal readonly uint GlobalScale, QuantLf;
    internal readonly JxlDequantMatrices Matrices;

    internal JxlVarDctPreparation(JxlImageHeader image, JxlFrameHeader frame, JxlFloatFrame xyb, float distance,
        MemoryAllocator allocator, CancellationToken cancellationToken, JxlTransformType? transform = null, int effort = 3,
        JxlAdaptiveQuantization adaptive = null, Buffer2D<JxlVarBlock> fixedBlocks = null, int decodingSpeed = 0, JxlDequantMatrices matrices = null,
        Buffer2D<int> fixedXFromY = null, Buffer2D<int> fixedBFromY = null)
    {
        this.Matrices = matrices ?? JxlDequantMatrices.Default;
        if (!float.IsFinite(distance) || distance <= 0 || distance > 25) { throw new ArgumentOutOfRangeException(nameof(distance)); }
        if (transform.HasValue && !Enum.IsDefined(transform.Value)) { throw new ArgumentOutOfRangeException(nameof(transform)); }
        Buffer2D<float> quantization = adaptive?.Quantization;
        int width = (int)frame.SampleWidth, height = (int)frame.SampleHeight;
        if (xyb.ColorChannels != 3 || xyb.Planes.Length < 3) { throw new ArgumentException("VarDCT requires three XYB planes.", nameof(xyb)); }
        for (int c = 0; c < 3; c++)
        {
            if (xyb.Planes[c] == null || xyb.Planes[c].Width != width || xyb.Planes[c].Height != height)
            { throw new ArgumentException("XYB plane dimensions must match the frame.", nameof(xyb)); }
        }
        int bw = (width + 7) / 8, bh = (height + 7) / 8;
        if (quantization != null && (quantization.Width != bw || quantization.Height != bh))
        { throw new ArgumentException("Quantization field dimensions must match the frame blocks.", nameof(quantization)); }
        if (fixedBlocks != null && (quantization == null || fixedBlocks.Width != bw || fixedBlocks.Height != bh))
        { throw new ArgumentException("Fixed transforms require a matching quantization field.", nameof(fixedBlocks)); }
        int correlationWidth = (width + 63) / 64, correlationHeight = (height + 63) / 64;
        bool fixedCorrelation = fixedXFromY != null || fixedBFromY != null;
        if (fixedCorrelation && (fixedXFromY == null || fixedBFromY == null ||
            fixedXFromY.Width != correlationWidth || fixedXFromY.Height != correlationHeight ||
            fixedBFromY.Width != correlationWidth || fixedBFromY.Height != correlationHeight))
        { throw new ArgumentException("Fixed correlation requires both maps to match the color tiles.", nameof(fixedXFromY)); }
        float dcDistance = Math.Max(.5f * distance, Math.Min(distance, .3f * MathF.Pow(distance / .3f, .83f)));
        float dcQuant = Math.Min(1.095924047623553f / dcDistance, 50);
        float acQuant = (quantization == null ? .79f : .39f) / distance;
        int scale = fixedBlocks == null ? (int)Math.Clamp(65536f * acQuant / 5, 1, 32768)
            : PerceptualScale(quantization, allocator, cancellationToken);
        scale = Math.Min(scale, Math.Max(1, (int)(dcQuant * 4096 * 1.6f)));
        this.GlobalScale = (uint)scale;
        this.QuantLf = (uint)Math.Clamp((int)((dcQuant * 65536 / scale) + .5f), 1, 65536);
        this.workspace = JxlModularWorkspace.ForEncoding(allocator, Math.Max(262144, (long)bw * bh * 64));
        try
        {
            this.Blocks = allocator.Allocate2D<JxlVarBlock>(bw, bh, AllocationOptions.Clean);
            this.XFromY = this.workspace.Allocate(correlationWidth, correlationHeight);
            this.BFromY = this.workspace.Allocate(correlationWidth, correlationHeight);
            if (fixedCorrelation)
            {
                for (int y = 0; y < correlationHeight; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    fixedXFromY.DangerousGetRowSpan(y).CopyTo(this.XFromY.Row(y));
                    fixedBFromY.DangerousGetRowSpan(y).CopyTo(this.BFromY.Row(y));
                }
            }
            // Slower efforts seed strategy selection with an initial 8x8 fit.
            // The final fit below uses the chosen transforms and quantizers.
            bool refineCorrelation = !transform.HasValue || transform == JxlTransformType.Dct8;
            if (fixedBlocks != null)
            {
                for (int y = 0; y < bh; y++) { fixedBlocks.DangerousGetRowSpan(y).CopyTo(this.Blocks.DangerousGetRowSpan(y)); }
            }
            else if (!fixedCorrelation && refineCorrelation && effort >= 7) { JxlChromaFromLuma.Compute(xyb, this.XFromY, this.BFromY, effort, cancellationToken, matrices: this.Matrices); }
            bool automatic = fixedBlocks != null || (!transform.HasValue && effort >= 5 && adaptive != null);
            if (automatic && fixedBlocks == null)
            {
                using JxlAcStrategy strategy = new(xyb, adaptive, this.XFromY, this.BFromY, this.Blocks, distance, effort, cancellationToken, decodingSpeed, this.Matrices);
                strategy.Select();
            }
            for (int c = 0; c < 3; c++)
            {
                this.Coefficients[c] = this.workspace.Allocate(bw * 8, bh * 8);
                this.LowFrequency[c] = this.workspace.Allocate(bw, bh);
            }
            for (int by = 0; by < bh; by++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int bx = 0; bx < bw; bx++)
                {
                    JxlVarBlock block = this.Blocks.DangerousGetRowSpan(by)[bx];
                    if (block.Multiplier < 0) { continue; }
                    JxlTransformType selected = automatic ? block.Transform : transform ?? JxlTransformType.Dct8;
                    (int dw, int dh) = selected.Blocks();
                    if (bx + dw > bw || by + dh > bh || bx % 32 + dw > 32 || by % 32 + dh > 32) { selected = JxlTransformType.Dct8; }
                    (dw, dh) = selected.Blocks();
                    float quant = acQuant;
                    if (quantization != null)
                    {
                        float maximum = 0, mean = 0;
                        for (int y = 0; y < dh; y++)
                        {
                            foreach (float value in quantization.DangerousGetRowSpan(by + y).Slice(bx, dw))
                            {
                                if (!float.IsFinite(value) || value <= 0) { throw new ArgumentException("Invalid quantization value.", nameof(quantization)); }
                                maximum = Math.Max(maximum, value); mean += value;
                            }
                        }
                        float mixer = Math.Clamp(1 - (distance - 1.54138f) * .56391f, 0, 1);
                        quant = dw * dh < 4 ? maximum : maximum * mixer + (1 - mixer) * (mean / (dw * dh));
                    }
                    int multiplier = (int)Math.Clamp((quant * 65536 / scale) + .5f, 1, 256);
                    for (int y = 0; y < dh; y++)
                    {
                        this.Blocks.DangerousGetRowSpan(by + y).Slice(bx, dw).Fill(new(default, -1));
                    }
                    this.Blocks.DangerousGetRowSpan(by)[bx] = new(selected, multiplier);
                }
            }
            if (!fixedCorrelation && refineCorrelation)
            { JxlChromaFromLuma.Compute(xyb, this.XFromY, this.BFromY, effort, cancellationToken, this.Blocks, this.GlobalScale, this.Matrices); }
            // Transforms never cross 256x256 groups, so each group writes only its own
            // coefficients, LF samples and block entries.
            int groupColumns = (bw + 31) / 32, groupRows = (bh + 31) / 32;
            JxlParallel.ForEncoder(groupColumns * groupRows, cancellationToken, () => new Scratch(allocator), (group, state) =>
            {
            IMemoryOwner<float> scratch = state.Values, lfScratch = state.LowFrequency;
            Span<float> lumaThresholds = stackalloc float[4];
            int groupLeft = group % groupColumns * 32, groupTop = group / groupColumns * 32;
            for (int by = groupTop; by < Math.Min(bh, groupTop + 32); by++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int bx = groupLeft; bx < Math.Min(bw, groupLeft + 32); bx++)
                {
                    JxlVarBlock block = this.Blocks.DangerousGetRowSpan(by)[bx];
                    if (block.Multiplier <= 0) { continue; }
                    JxlTransformType selected = block.Transform;
                    int multiplier = block.Multiplier;
                    (int dw, int dh) = selected.Blocks();
                    int w = dw * 8, h = dh * 8, size = w * h;
                    Span<float> values = scratch.Memory.Span[..(3 * size)], dc = lfScratch.Memory.Span[..(3 * dw * dh)];
                    for (int c = 0; c < 3; c++)
                    {
                        Span<float> channel = values.Slice(c * size, size), low = dc.Slice(c * dw * dh, dw * dh);
                        for (int y = 0; y < h; y++)
                        {
                            Span<float> row = xyb.Planes[c].DangerousGetRowSpan(Math.Min(height - 1, by * 8 + y));
                            for (int x = 0; x < w; x++)
                            {
                                float value = row[Math.Min(width - 1, bx * 8 + x)];
                                if (!float.IsFinite(value)) { throw new ArgumentException("Lossy JPEG XL requires finite samples.", nameof(xyb)); }
                                channel[y * w + x] = value;
                            }
                        }
                        JxlForwardTransform.Apply(channel, selected);
                        int logW = 5 - BitOperations.Log2((uint)dw), logH = 5 - BitOperations.Log2((uint)dh);
                        for (int y = 0; y < dh; y++)
                        {
                            for (int x = 0; x < dw; x++)
                            { low[y * dw + x] = channel[y * w + x] * JxlInverseTransform.LfScales[y << logH] * JxlInverseTransform.LfScales[x << logW]; }
                        }
                        JxlDct.Transform2D(low, dw, dh, true);
                    }
                    multiplier = JxlAcQuantizer.Refine(values, selected, frame, this.GlobalScale, multiplier, effort, lumaThresholds, this.Matrices);
                    this.Blocks.DangerousGetRowSpan(by)[bx] = new(selected, multiplier);
                    // Quantize Y first; chroma prediction uses the reconstructed Y.
                    for (int order = 0; order < 3; order++)
                    {
                        int c = order == 2 ? 2 : 1 - order;
                        Span<float> channel = values.Slice(c * size, size), low = dc.Slice(c * dw * dh, dw * dh);
                        float lfScale = (c == 0 ? 16f : c == 1 ? 128f : 256f) / ((float)this.GlobalScale * this.QuantLf);
                        for (int y = 0; y < dh; y++)
                        {
                            for (int x = 0; x < dw; x++)
                            {
                                int p = y * dw + x;
                                float value = low[p] - (c == 2 ? dc[dw * dh + p] : 0);
                                int quantized = Round(value / lfScale);
                                this.LowFrequency[c].Set(bx + x, by + y, quantized); low[p] = quantized * lfScale;
                            }
                        }
                        float[] matrix = this.Matrices.Get(selected, c);
                        float qmScale = c == 1 ? 1 : MathF.Pow(.8f, (int)(c == 0 ? frame.XQuantScale : frame.BQuantScale) - 2);
                        float acScale = 65536f / (this.GlobalScale * (float)multiplier) * qmScale;
                        bool transpose = selected.Transpose();
                        for (int y = 0; y < h; y++)
                        {
                            Span<int> row = this.Coefficients[c].Row(by * 8 + y);
                            for (int x = 0; x < w; x++)
                            {
                                if (x < dw && y < dh) { continue; }
                                int p = y * w + x;
                                float dequant = matrix[transpose ? x * h + y : p] * acScale;
                                float correlation = c == 1 ? 0 : c == 0 ? this.XFromY.Get((bx * 8 + x) / 64, (by * 8 + y) / 64) / 84f
                                    : 1 + this.BFromY.Get((bx * 8 + x) / 64, (by * 8 + y) / 64) / 84f;
                                float value = (channel[p] - (c == 1 ? 0 : correlation * values[size + p])) / dequant;
                                int quadrant = (y >= h / 2 ? 2 : 0) + (x >= w / 2 ? 1 : 0);
                                float threshold = c == 1 ? lumaThresholds[quadrant] : JxlAcQuantizer.ChromaThreshold(quadrant, dw * dh);
                                int quantized = Math.Abs(value) < threshold ? 0 : Round(value);
                                row[bx * 8 + x] = quantized;
                                float adjusted = Math.Abs((long)quantized) <= 1 ? quantized * image.Opsin[12 + c] : quantized - image.Opsin[15] / quantized;
                                channel[p] = adjusted * dequant;
                            }
                        }
                    }
                }
            }
            });
        }
        catch { this.Dispose(); throw; }
    }

    private sealed class Scratch(MemoryAllocator allocator) : IDisposable
    {
        internal readonly IMemoryOwner<float> Values = allocator.Allocate<float>(3 * 256 * 256);
        internal readonly IMemoryOwner<float> LowFrequency = allocator.Allocate<float>(3 * 32 * 32);
        public void Dispose() { this.Values.Dispose(); this.LowFrequency.Dispose(); }
    }

    private static int PerceptualScale(Buffer2D<float> quantization, MemoryAllocator allocator, CancellationToken cancellationToken)
    {
        int count = checked(quantization.Width * quantization.Height);
        using IMemoryOwner<float> owner = allocator.Allocate<float>(count);
        Span<float> values = owner.Memory.Span[..count];
        for (int y = 0; y < quantization.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            quantization.DangerousGetRowSpan(y).CopyTo(values.Slice(y * quantization.Width, quantization.Width));
        }
        // Sorting uses the same upper median as the reference's nth_element.
        values.Sort(); float median = values[count / 2];
        for (int i = 0; i < count; i++) { values[i] = Math.Abs(values[i] - median); }
        values.Sort(); cancellationToken.ThrowIfCancellationRequested();
        return (int)Math.Clamp(65536f * (median - values[count / 2]) / 5, 1, 32768);
    }

    private static int Round(float value)
    {
        double rounded = Math.Round(value);
        if (!double.IsFinite(rounded) || rounded < int.MinValue || rounded > int.MaxValue)
        { throw new ArgumentException("The sample range exceeds JPEG XL's quantized coefficient precision."); }
        return (int)rounded;
    }

    public void Dispose() { this.Blocks?.Dispose(); this.workspace.Dispose(); }
}
