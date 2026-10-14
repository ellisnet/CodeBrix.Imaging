// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Memory;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlAdaptiveQuantizationTests
{
    [Theory]
    [InlineData(1, 1)] [InlineData(7, 13)] [InlineData(129, 65)]
    public void constant_images_have_uniform_finite_precision_including_padded_edges(int width, int height)
    {
        using JxlFloatFrame source = Make(width, height, false);
        using JxlAdaptiveQuantization field = new(source, 1, TestContext.Current.CancellationToken);
        float expected = field.Quantization.DangerousGetRowSpan(0)[0]; Assert.InRange(expected, .01f, 10);
        for (int y = 0; y < field.Quantization.Height; y++)
        { foreach (float value in field.Quantization.DangerousGetRowSpan(y)) { Assert.Equal(expected, value); } }
        Assert.Equal((width + 7) / 8 * 8, field.FineMask.Width);
        Assert.Equal((height + 7) / 8 * 8, field.FineMask.Height);
        for (int y = 0; y < field.FineMask.Height; y++)
        { foreach (float value in field.FineMask.DangerousGetRowSpan(y)) { Assert.InRange(value, 99.99f, 100.01f); } }
    }

    [Fact]
    public void texture_masks_error_while_low_distance_increases_precision()
    {
        using JxlFloatFrame source = Make(129, 65, true);
        using JxlAdaptiveQuantization low = new(source, .5f, TestContext.Current.CancellationToken);
        using JxlAdaptiveQuantization high = new(source, 3, TestContext.Current.CancellationToken);
        Assert.True(low.Mask.DangerousGetRowSpan(4)[3] > low.Mask.DangerousGetRowSpan(4)[12]);
        Assert.True(low.Quantization.DangerousGetRowSpan(4)[3] > low.Quantization.DangerousGetRowSpan(4)[12]);
        Assert.True(low.FineMask.DangerousGetRowSpan(32)[24] > low.FineMask.DangerousGetRowSpan(32)[96]);
        for (int y = 0; y < low.Quantization.Height; y++)
        {
            for (int x = 0; x < low.Quantization.Width; x++)
            { Assert.True(low.Quantization.DangerousGetRowSpan(y)[x] > high.Quantization.DangerousGetRowSpan(y)[x]); }
        }
    }

    private static JxlFloatFrame Make(int width, int height, bool texture)
    {
        JxlFloatFrame frame = new(Configuration.Default.MemoryAllocator, 3, 3);
        for (int c = 0; c < 3; c++) { frame.Planes[c] = frame.Allocator.Allocate2D<float>(width, height); }
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float value = .4f + (texture && x >= 64 ? .05f * (((x + y) % 2) * 2 - 1) : 0);
                frame.Planes[0].DangerousGetRowSpan(y)[x] = 0;
                frame.Planes[1].DangerousGetRowSpan(y)[x] = value;
                frame.Planes[2].DangerousGetRowSpan(y)[x] = value;
            }
        }
        return frame;
    }
}
