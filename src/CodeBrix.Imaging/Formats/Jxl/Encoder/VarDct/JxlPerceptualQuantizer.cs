// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from FindBestQuantization, AdjustQuantField and TileDistMap in
// libjxl lib/jxl/enc_adaptive_quantization.cc,
// with fixed color correlation as in lib/jxl/enc_heuristics.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Color;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Butteraugli;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Formats.Jxl.VarDct;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;

internal static class JxlPerceptualQuantizer
{
    internal static JxlVarDctPreparation Prepare(JxlImageHeader image, JxlFrameHeader frame, JxlFloatFrame xyb,
        JxlFloatFrame linearReference, JxlAdaptiveQuantization adaptive, float distance, int effort, float intensity,
        MemoryAllocator allocator, CancellationToken cancellationToken, JxlTransformType? transform = null, int decodingSpeed = 0, JxlDequantMatrices matrices = null, JxlLossyPatchEncoder patches = null)
    {
        Buffer2D<float> field = adaptive.Quantization;
        using Buffer2D<JxlVarBlock> blocks = allocator.Allocate2D<JxlVarBlock>(field.Width, field.Height);
        using Buffer2D<int> correlationX = allocator.Allocate2D<int>((field.Width + 7) / 8, (field.Height + 7) / 8);
        using Buffer2D<int> correlationB = allocator.Allocate2D<int>(correlationX.Width, correlationX.Height);
        using (JxlVarDctPreparation initial = new(image, frame, xyb, distance, allocator, cancellationToken, transform, effort, adaptive, decodingSpeed: decodingSpeed, matrices: matrices))
        {
            for (int y = 0; y < field.Height; y++) { initial.Blocks.DangerousGetRowSpan(y).CopyTo(blocks.DangerousGetRowSpan(y)); }
            for (int y = 0; y < correlationX.Height; y++)
            {
                initial.XFromY.Row(y).CopyTo(correlationX.DangerousGetRowSpan(y));
                initial.BFromY.Row(y).CopyTo(correlationB.DangerousGetRowSpan(y));
            }
        }
        Normalize(field, blocks, distance, cancellationToken);
        using Buffer2D<float> original = allocator.Allocate2D<float>(field.Width, field.Height);
        float minimum = float.PositiveInfinity, maximum = 0;
        for (int y = 0; y < field.Height; y++)
        {
            Span<float> row = field.DangerousGetRowSpan(y); row.CopyTo(original.DangerousGetRowSpan(y));
            foreach (float value in row) { minimum = Math.Min(minimum, value); maximum = Math.Max(maximum, value); }
        }
        float deviation = MathF.Sqrt(250 / (maximum / minimum)), asymmetry = Math.Min(2, deviation);
        float lower = minimum / (asymmetry * deviation), upper = maximum * (deviation / asymmetry);
        int iterations = effort >= 9 ? 4 : 2;
        using JxlButteraugli comparator = new(linearReference, intensity, cancellationToken);
        using Buffer2D<float> difference = allocator.Allocate2D<float>((int)frame.Width, (int)frame.Height);
        JxlColorTransform inverse = new(image, false, linearOutput: true);
        JxlVarDctPreparation prepared = null;
        try
        {
            for (int iteration = 0; iteration <= iterations; iteration++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                prepared = new(image, frame, xyb, distance, allocator, cancellationToken, transform, effort, adaptive, blocks, decodingSpeed, matrices,
                    correlationX, correlationB);
                using (JxlFloatFrame decoded = JxlVarDctRoundtrip.Reconstruct(image, frame, prepared, allocator, cancellationToken))
                {
                    patches?.Apply(decoded, false, cancellationToken);
                    inverse.Apply(decoded, cancellationToken);
                    comparator.Compare(decoded, difference, cancellationToken);
                }
                if (iteration == iterations) { JxlVarDctPreparation result = prepared; prepared = null; return result; }
                Adjust(field, original, blocks, difference, prepared.GlobalScale, distance, lower, upper, iteration, (int)frame.Upsampling, cancellationToken);
                prepared.Dispose(); prepared = null;
            }
            throw new InvalidOperationException("The quantization search did not produce a frame.");
        }
        finally { prepared?.Dispose(); }
    }

    private static void Normalize(Buffer2D<float> field, Buffer2D<JxlVarBlock> blocks, float distance, CancellationToken cancellationToken)
    {
        float mixer = Math.Clamp(1 - (distance - 1.54138f) * .56391f, 0, 1);
        for (int y = 0; y < field.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = 0; x < field.Width; x++)
            {
                JxlVarBlock block = blocks.DangerousGetRowSpan(y)[x];
                if (block.Multiplier <= 0) { continue; }
                (int dw, int dh) = block.Transform.Blocks(); float maximum = 0, mean = 0;
                for (int iy = 0; iy < dh; iy++)
                {
                    foreach (float value in field.DangerousGetRowSpan(y + iy).Slice(x, dw)) { maximum = Math.Max(maximum, value); mean += value; }
                }
                float quant = dw * dh < 4 ? maximum : maximum * mixer + (1 - mixer) * (mean / (dw * dh));
                for (int iy = 0; iy < dh; iy++) { field.DangerousGetRowSpan(y + iy).Slice(x, dw).Fill(quant); }
            }
        }
    }

    private static void Adjust(Buffer2D<float> field, Buffer2D<float> original, Buffer2D<JxlVarBlock> blocks,
        Buffer2D<float> difference, uint globalScale, float distance, float lower, float upper, int iteration, int upsampling, CancellationToken cancellationToken)
    {
        float inverseScale = 65536f / globalScale, scale = globalScale / 65536f;
        for (int y = 0; y < field.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = 0; x < field.Width; x++)
            {
                JxlVarBlock block = blocks.DangerousGetRowSpan(y)[x];
                if (block.Multiplier <= 0) { continue; }
                (int dw, int dh) = block.Transform.Blocks();
                float ratio = TileDistance(difference, x * 8 * upsampling, y * 8 * upsampling, dw * 8 * upsampling, dh * 8 * upsampling) / distance;
                float quant = field.DangerousGetRowSpan(y)[x];
                if (iteration == 1)
                { quant = Math.Clamp(Math.Max(quant, (float)(.4 * quant + .6 * original.DangerousGetRowSpan(y)[x])), lower, upper); }
                if (ratio <= 1 && iteration < 2) { quant = (float)(quant * Math.Pow(ratio, .2)); }
                else if (ratio > 1)
                {
                    float previous = quant; quant *= ratio;
                    if (MathF.Floor(previous * inverseScale + .5f) == MathF.Floor(quant * inverseScale + .5f)) { quant = previous + scale; }
                }
                quant = Math.Clamp(quant, lower, upper);
                for (int iy = 0; iy < dh; iy++) { field.DangerousGetRowSpan(y + iy).Slice(x, dw).Fill(quant); }
            }
        }
    }

    private static float TileDistance(Buffer2D<float> difference, int left, int top, int width, int height)
    {
        width = Math.Min(width, difference.Width - left); height = Math.Min(height, difference.Height - top);
        float maximum = 0;
        for (int y = top; y < top + height; y++)
        { foreach (float value in difference.DangerousGetRowSpan(y).Slice(left, width)) { maximum = Math.Max(maximum, value); } }
        if (!float.IsFinite(maximum)) { throw new ArgumentException("The sample range exceeds the perceptual metric's finite precision."); }
        if (maximum == 0) { return 0; }
        // Normalize before the sixteenth power to avoid overflow for HDR errors.
        double sum = 0;
        for (int y = top; y < top + height; y++)
        {
            foreach (float value in difference.DangerousGetRowSpan(y).Slice(left, width))
            {
                double power = value / (double)maximum; power *= power; power *= power; power *= power; power *= power; sum += power;
            }
        }
        return (float)(1.2 * maximum * Math.Pow(sum / (width * height), 1.0 / 16));
    }
}
