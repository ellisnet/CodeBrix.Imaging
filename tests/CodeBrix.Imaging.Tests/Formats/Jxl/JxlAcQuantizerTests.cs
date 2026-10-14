// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.VarDct;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlAcQuantizerTests
{
    [Fact]
    public void isolated_faint_high_frequency_detail_survives_refinement()
    {
        float[] coefficients = new float[192];
        float weight = JxlDequantMatrices.Default.Get(JxlTransformType.Dct8, 1)[63];
        coefficients[64 + 63] = .49f * weight / 5;
        Span<float> fastThresholds = stackalloc float[4], refinedThresholds = stackalloc float[4];
        int fast = JxlAcQuantizer.Refine(coefficients, JxlTransformType.Dct8, new(), 65536, 5, 4, fastThresholds);
        int refined = JxlAcQuantizer.Refine(coefficients, JxlTransformType.Dct8, new(), 65536, 5, 7, refinedThresholds);
        Assert.Equal(0, Quantize(fast, fastThresholds[3]));
        int detail = Quantize(refined, refinedThresholds[3]);
        Assert.NotEqual(0, detail);
        double original = coefficients[64 + 63];
        Assert.True(Math.Abs(original - detail * weight / refined) < Math.Abs(original));

        int Quantize(int q, float threshold)
        { float value = coefficients[64 + 63] * q / weight; return Math.Abs(value) < threshold ? 0 : (int)Math.Round(value); }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)] [InlineData(12)] [InlineData(13)]
    [InlineData(14)] [InlineData(15)] [InlineData(16)] [InlineData(17)] [InlineData(18)] [InlineData(19)] [InlineData(20)]
    [InlineData(21)] [InlineData(22)] [InlineData(23)] [InlineData(24)] [InlineData(25)] [InlineData(26)]
    public void every_transform_keeps_quantization_and_dead_zones_bounded(int index)
    {
        JxlTransformType type = (JxlTransformType)index; (int bw, int bh) = type.Blocks();
        float[] values = new float[3 * bw * bh * 64];
        for (int i = 0; i < values.Length; i++) { values[i] = .002f * MathF.Sin(i * .77f); }
        Span<float> thresholds = stackalloc float[4];
        foreach (int original in new[] { 1, 5, 255, 256 })
        {
            int quant = JxlAcQuantizer.Refine(values, type, new JxlFrameHeader(), 8192, original, 7, thresholds);
            Assert.InRange(quant, 1, 256);
            foreach (float value in thresholds) { Assert.True(float.IsFinite(value) && value > 0 && value < 2); }
        }
    }
}
