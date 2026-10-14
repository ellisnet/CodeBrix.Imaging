// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Formats.Jxl.VarDct;
using CodeBrix.Imaging.Memory;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlAcStrategyTests
{
    [Theory]
    [InlineData(1, 1, 5, .5f)] [InlineData(7, 13, 6, 1f)]
    [InlineData(63, 65, 7, 3f)] [InlineData(129, 137, 10, .5f)]
    [InlineData(263, 259, 7, 1f)] [InlineData(2057, 9, 5, 3f)]
    [InlineData(9, 2057, 6, .5f)]
    public void automatic_selection_covers_each_block_once_without_crossing_color_tiles(int width, int height, int effort, float distance)
    {
        using JxlFloatFrame source = Make(width, height, true);
        using JxlAdaptiveQuantization field = new(source, distance, TestContext.Current.CancellationToken);
        using JxlVarDctPreparation prepared = Prepare(source, field, effort, distance);
        VerifyCoverage(prepared);
    }

    [Theory]
    [InlineData(5)] [InlineData(7)] [InlineData(10)]
    public void smooth_regions_use_large_blocks_while_texture_retains_smaller_transforms(int effort)
    {
        using JxlFloatFrame source = Make(128, 64, true);
        using JxlAdaptiveQuantization field = new(source, 1, TestContext.Current.CancellationToken);
        using JxlVarDctPreparation prepared = Prepare(source, field, effort, 1);
        HashSet<JxlTransformType> types = VerifyCoverage(prepared);
        Assert.Contains(JxlTransformType.Dct64, types);
        Assert.True(types.Count > 1, "The texture should not use the same transform as the smooth tile.");
    }

    [Fact]
    public void low_effort_keeps_eight_by_eight_transforms()
    {
        using JxlFloatFrame source = Make(129, 65, false);
        using JxlVarDctPreparation prepared = Prepare(source, null, 4, 1);
        Assert.Equal(JxlTransformType.Dct8, Assert.Single(VerifyCoverage(prepared)));
    }

    private static JxlVarDctPreparation Prepare(JxlFloatFrame source, JxlAdaptiveQuantization field, int effort, float distance)
    {
        uint width = (uint)source.Planes[0].Width, height = (uint)source.Planes[0].Height;
        return new(new JxlImageHeader { Size = new(width, height) }, new JxlFrameHeader { Width = width, Height = height },
            source, distance, source.Allocator, TestContext.Current.CancellationToken, effort: effort, adaptive: field);
    }

    private static HashSet<JxlTransformType> VerifyCoverage(JxlVarDctPreparation prepared)
    {
        int width = prepared.Blocks.Width, height = prepared.Blocks.Height;
        int[,] coverage = new int[height, width]; HashSet<JxlTransformType> types = [];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                JxlVarBlock block = prepared.Blocks.DangerousGetRowSpan(y)[x];
                Assert.NotEqual(0, block.Multiplier);
                if (block.Multiplier < 0) { continue; }
                Assert.InRange(block.Multiplier, 1, 256); types.Add(block.Transform);
                (int w, int h) = block.Transform.Blocks();
                Assert.True(x + w <= width && y + h <= height);
                Assert.Equal(x / 8, (x + w - 1) / 8); Assert.Equal(y / 8, (y + h - 1) / 8);
                for (int dy = 0; dy < h; dy++) { for (int dx = 0; dx < w; dx++) { coverage[y + dy, x + dx]++; } }
            }
        }
        foreach (int count in coverage) { Assert.Equal(1, count); }
        return types;
    }

    private static JxlFloatFrame Make(int width, int height, bool texture)
    {
        JxlFloatFrame frame = new(Configuration.Default.MemoryAllocator, 3, 3);
        for (int c = 0; c < 3; c++) { frame.Planes[c] = frame.Allocator.Allocate2D<float>(width, height); }
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool textured = texture && (x / 64 + y / 64) % 2 != 0;
                float value = textured ? .35f + .05f * MathF.Sin(x * 1.7f + y * .9f) : .4f;
                frame.Planes[0].DangerousGetRowSpan(y)[x] = textured ? .02f * MathF.Cos(x * .7f - y * 1.1f) : 0;
                frame.Planes[1].DangerousGetRowSpan(y)[x] = value;
                frame.Planes[2].DangerousGetRowSpan(y)[x] = value;
            }
        }
        return frame;
    }
}
