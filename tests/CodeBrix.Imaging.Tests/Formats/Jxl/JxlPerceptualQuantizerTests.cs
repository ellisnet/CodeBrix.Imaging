// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Numerics;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Color;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Butteraugli;
using CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Memory;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlPerceptualQuantizerTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(.5f, 8, 1f)] [InlineData(1f, 8, 1f)] [InlineData(1f, 9, 1f)] [InlineData(3f, 10, 1f)]
    [InlineData(.5f, 8, .25f)] [InlineData(1f, 9, .25f)]
    public void perceptual_search_reduces_size_within_target_or_improves_a_coarse_initial_field(float distance, int effort, float initialScale)
    {
        const int width = 129, height = 97;
        var allocator = Configuration.Default.MemoryAllocator;
        var cancellation = TestContext.Current.CancellationToken;
        using JxlFloatFrame linear = JxlButteraugliKernels.Create(width, height, 3, allocator);
        using JxlFloatFrame xyb = JxlButteraugliKernels.Create(width, height, 3, allocator);
        JxlXybEncoder forward = new(255);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float detail = x > width / 2 ? .13f * MathF.Sin(x * .47f + y * .13f) : 0;
                Vector3 value = new(.2f + x * .002f + detail, .3f + y * .002f + detail * .7f, .4f + .1f * MathF.Cos(x * .12f + y * .07f));
                Vector3 encoded = forward.FromLinearSrgb(value);
                for (int c = 0; c < 3; c++)
                { linear.Planes[c].DangerousGetRowSpan(y)[x] = value[c]; xyb.Planes[c].DangerousGetRowSpan(y)[x] = encoded[c]; }
            }
        }
        JxlImageHeader image = new() { Size = new(width, height) };
        JxlFrameHeader frame = new()
        {
            Width = width, Height = height,
            Restoration = new() { GaborEnabled = distance > .5f, EpfIterations = distance >= 1.5f ? 2U : distance >= .7f ? 1U : 0U },
        };
        float adaptiveDistance = frame.Restoration.GaborEnabled ? distance : distance * .62f;
        using JxlAdaptiveQuantization adaptive = new(xyb, adaptiveDistance, cancellation);
        if (initialScale != 1)
        {
            for (int y = 0; y < adaptive.Quantization.Height; y++)
            { Span<float> row = adaptive.Quantization.DangerousGetRowSpan(y); for (int x = 0; x < row.Length; x++) { row[x] *= initialScale; } }
        }
        if (frame.Restoration.GaborEnabled) { JxlInverseGabor.Apply(xyb, cancellation); }
        using JxlButteraugli metric = new(linear, 80, cancellation);
        using Buffer2D<float> difference = allocator.Allocate2D<float>(width, height);
        using JxlVarDctPreparation initial = new(image, frame, xyb, distance, allocator, cancellation, effort: effort, adaptive: adaptive);
        using JxlVarDctPreparation optimized = JxlPerceptualQuantizer.Prepare(image, frame, xyb, linear, adaptive, distance, effort, 80, allocator, cancellation);
        float before = Score(initial), after = Score(optimized);
        int initialBytes = Size(initial), optimizedBytes = Size(optimized);
        output.WriteLine($"Effort {effort}, distance {distance}, initial scale {initialScale}: initial {before} ({initialBytes} bytes), optimized {after} ({optimizedBytes} bytes)");
        Assert.True(float.IsFinite(after), $"Target {distance}; initial {before}; optimized {after}");
        if (initialScale == 1)
        {
            // A smaller stream with better quality can move farther below the
            // target. The target bounds distortion; it is not a required error.
            Assert.True(after <= distance && optimizedBytes < initialBytes, $"Target {distance}, score {after}, size {initialBytes} → {optimizedBytes}");
        }
        else
        {
            Assert.True(before > distance && after < before && Math.Abs(after - distance) < Math.Abs(before - distance),
                $"Target {distance}; initial {before}; optimized {after}");
        }
        // Pinned libjxl enc_heuristics.cc fixes the selected-transform CfL map
        // before FindBestQuantizer. Refinement changes only the quantization.
        for (int y = 0; y < initial.XFromY.Height; y++)
        {
            Assert.True(initial.XFromY.Row(y).SequenceEqual(optimized.XFromY.Row(y)), "Perceptual refinement changed X correlation.");
            Assert.True(initial.BFromY.Row(y).SequenceEqual(optimized.BFromY.Row(y)), "Perceptual refinement changed B correlation.");
        }
        for (int y = 0; y < initial.Blocks.Height; y++)
        {
            for (int x = 0; x < initial.Blocks.Width; x++)
            {
                var a = initial.Blocks.DangerousGetRowSpan(y)[x]; var b = optimized.Blocks.DangerousGetRowSpan(y)[x];
                Assert.Equal(a.Transform, b.Transform); Assert.Equal(a.Multiplier < 0, b.Multiplier < 0);
                if (b.Multiplier > 0) { Assert.InRange(b.Multiplier, 1, 256); }
            }
        }

        float Score(JxlVarDctPreparation prepared)
        {
            using JxlFloatFrame decoded = JxlVarDctRoundtrip.Reconstruct(image, frame, prepared, allocator, cancellation);
            new JxlColorTransform(image, false, linearOutput: true).Apply(decoded, cancellation);
            return metric.Compare(decoded, difference, cancellation);
        }

        int Size(JxlVarDctPreparation prepared)
        {
            using MemoryStream stream = new(); JxlBitWriter writer = new(stream); image.Write(writer);
            JxlVarDctEncoder.WritePreparedFrame(writer, image, frame, prepared, [], distance, effort, allocator, cancellation);
            writer.ZeroPadToByte(); return checked((int)stream.Length);
        }
    }
}
