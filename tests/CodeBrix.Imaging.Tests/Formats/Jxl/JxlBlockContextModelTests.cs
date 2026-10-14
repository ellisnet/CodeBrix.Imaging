// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;
using CodeBrix.Imaging.Formats.Jxl.VarDct;
using CodeBrix.Imaging.Memory;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlBlockContextModelTests
{
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void decoder_speed_contexts_share_transform_orders_and_quantizers(int speed)
    {
        using Buffer2D<JxlVarBlock> blocks = Configuration.Default.MemoryAllocator.Allocate2D<JxlVarBlock>(4, 4);
        JxlBlockContextModel model = new(blocks, 1, 7, TestContext.Current.CancellationToken, speed);
        Assert.Equal(2, model.Count); Assert.Empty(model.QuantThresholds); VerifyHeaderAndLookup(model);
        for (int order = 0; order < 13; order++)
        { Assert.Equal(0, model.Map[order]); Assert.Equal(1, model.Map[order + 13]); Assert.Equal(1, model.Map[order + 26]); }
    }

    [Theory]
    [InlineData(4, 4, 1f, 7)] [InlineData(128, 128, .5f, 3)]
    public void small_images_and_low_efforts_use_the_single_bit_default(int width, int height, float distance, int effort)
    {
        using Buffer2D<JxlVarBlock> blocks = Configuration.Default.MemoryAllocator.Allocate2D<JxlVarBlock>(width, height);
        for (int y = 0; y < height; y++) { blocks.DangerousGetRowSpan(y).Fill(new(JxlTransformType.Dct8, 1)); }
        JxlBlockContextModel model = new(blocks, distance, effort, TestContext.Current.CancellationToken);
        Assert.Empty(model.QuantThresholds); Assert.Equal(15, model.Count);
        VerifyHeaderAndLookup(model, 1);
    }

    [Theory]
    [InlineData(1)] [InlineData(128)] [InlineData(256)]
    public void constant_quantizers_have_bounded_contiguous_context_maps(int quantizer)
    {
        using Buffer2D<JxlVarBlock> blocks = Configuration.Default.MemoryAllocator.Allocate2D<JxlVarBlock>(128, 128);
        for (int y = 0; y < blocks.Height; y++) { blocks.DangerousGetRowSpan(y).Fill(new(JxlTransformType.Dct8, quantizer)); }
        JxlBlockContextModel model = new(blocks, 1, 7, TestContext.Current.CancellationToken);
        VerifyHeaderAndLookup(model);
    }

    [Fact]
    public void quantile_split_counts_every_cell_covered_by_a_large_transform()
    {
        // Arrange: high-quantizer DCT64 blocks cover 3/4 of the image, although
        // their anchor count is much smaller than the low-quantizer DCT8 count.
        using Buffer2D<JxlVarBlock> blocks = Configuration.Default.MemoryAllocator.Allocate2D<JxlVarBlock>(128, 128);
        for (int y = 0; y < 128; y++)
        {
            for (int x = 0; x < 128; x++)
            {
                blocks.DangerousGetRowSpan(y)[x] = y >= 96 ? new(JxlTransformType.Dct8, 10) :
                    new(JxlTransformType.Dct64, x % 8 == 0 && y % 8 == 0 ? 100 : -1);
            }
        }

        // Act.
        JxlBlockContextModel model = new(blocks, 1, 7, TestContext.Current.CancellationToken);

        // Assert: a missing area weight would leave the cumulative count below
        // half the image and fail to choose any quantizer split at all.
        Assert.Equal(new uint[] { 99 }, model.QuantThresholds);
        VerifyHeaderAndLookup(model);
    }

    private static void VerifyHeaderAndLookup(JxlBlockContextModel model, long? expectedBits = null)
    {
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream);
        model.Write(writer); long bits = writer.Position;
        if (expectedBits.HasValue) { Assert.Equal(expectedBits.Value, bits); }
        writer.WriteBits(17, 0x17593); writer.ZeroPadToByte(); stream.Position = 0;
        JxlBitReader reader = new(stream, TestContext.Current.CancellationToken);
        JxlHfBlockContext decoded = JxlHfBlockContext.Read(reader, Configuration.Default.MemoryAllocator);
        Assert.Equal(bits, reader.Position); Assert.Equal(0x17593U, reader.ReadBits(17));
        Assert.Equal(model.QuantThresholds, decoded.QuantThresholds); Assert.Equal(model.Map, decoded.Map);
        Assert.Equal(model.Count, decoded.ClusterCount); Assert.InRange(decoded.ClusterCount, 1, 16);
        for (int i = 0; i < decoded.ClusterCount; i++) { Assert.Contains((byte)i, decoded.Map); }
        for (int channel = 0; channel < 3; channel++)
        {
            for (int transform = 0; transform < 27; transform++)
            {
                for (int q = 1; q <= 256; q++)
                {
                    JxlVarBlock block = new((JxlTransformType)transform, q);
                    int segment = 0; foreach (uint threshold in decoded.QuantThresholds) { if (q > threshold) { segment++; } }
                    int expected = decoded.Map[(channel * 13 + block.Transform.OrderIndex()) * (decoded.QuantThresholds.Length + 1) + segment];
                    Assert.Equal(expected, model.Context(channel, block));
                }
            }
        }
    }
}
