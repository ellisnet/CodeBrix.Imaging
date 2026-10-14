// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Numerics;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Butteraugli;
using CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Formats.Jxl.VarDct;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlVarDctRoundtripTests
{
    [Theory]
    [InlineData(1, 1, .1f, false)] [InlineData(19, 13, .8f, false)]
    [InlineData(263, 259, 1.5f, false)] [InlineData(2057, 9, 4f, false)] [InlineData(9, 2057, .2f, true)]
    public void search_reconstruction_matches_written_stream_including_filters_and_group_boundaries(int width, int height, float distance, bool disableLfSmoothing)
        => Verify(width, height, null, distance, disableLfSmoothing);

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)] [InlineData(12)] [InlineData(13)]
    [InlineData(14)] [InlineData(15)] [InlineData(16)] [InlineData(17)] [InlineData(18)] [InlineData(19)] [InlineData(20)]
    [InlineData(21)] [InlineData(22)] [InlineData(23)] [InlineData(24)] [InlineData(25)] [InlineData(26)]
    public void search_reconstruction_matches_every_transform(int transform)
    {
        JxlTransformType type = (JxlTransformType)transform;
        (int w, int h) = type.Blocks();
        Verify(w * 8 + 3, h * 8 + 5, type, .1f, false);
    }

    [Theory]
    [InlineData(2)] [InlineData(4)] [InlineData(8)]
    public void search_reconstruction_matches_resampled_pixels(int factor) => Verify(263, 259, null, 3f, false, factor);

    private static void Verify(int width, int height, JxlTransformType? transform, float distance, bool disableLfSmoothing, int factor = 1)
    {
        // Arrange: prepare the exact same coefficients for the search and writer.
        var allocator = Configuration.Default.MemoryAllocator;
        using JxlFloatFrame xyb = JxlButteraugliKernels.Create(width, height, 3, allocator);
        JxlXybEncoder forward = new(255);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector3 value = forward.FromLinearSrgb(new(.1f + .7f * ((x * 7 + y * 3) % 67) / 66,
                    .1f + .7f * ((x * 3 + y * 11) % 71) / 70, .1f + .7f * ((x * 5 + y * 7) % 73) / 72));
                xyb.Planes[0].DangerousGetRowSpan(y)[x] = value.X;
                xyb.Planes[1].DangerousGetRowSpan(y)[x] = value.Y;
                xyb.Planes[2].DangerousGetRowSpan(y)[x] = value.Z;
            }
        }
        JxlImageHeader image = new() { Size = new((uint)width, (uint)height) };
        JxlFrameHeader frame = new()
        {
            Width = (uint)width, Height = (uint)height, Flags = disableLfSmoothing ? 128UL : 0, Upsampling = (uint)factor,
            Restoration = new() { GaborEnabled = distance > .5f, EpfIterations = distance >= 4 ? 3U : distance >= 1.5f ? 2U : distance >= .7f ? 1U : 0U },
        };
        if (factor != 1) { JxlDownsampling.Apply(xyb, factor, 10, TestContext.Current.CancellationToken); }
        using JxlAdaptiveQuantization adaptive = new(xyb, distance, TestContext.Current.CancellationToken);
        if (frame.Restoration.GaborEnabled) { JxlInverseGabor.Apply(xyb, TestContext.Current.CancellationToken); }
        using JxlVarDctPreparation prepared = new(image, frame, xyb, distance, allocator, TestContext.Current.CancellationToken, transform, 7, adaptive);

        // Act.
        using JxlFloatFrame reconstructed = JxlVarDctRoundtrip.Reconstruct(image, frame, prepared, allocator, TestContext.Current.CancellationToken);
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream); image.Write(writer);
        JxlVarDctEncoder.WritePreparedFrame(writer, image, frame, prepared, [], distance, 7, allocator, TestContext.Current.CancellationToken);
        writer.ZeroPadToByte(); stream.Position = 0;
        using JxlRawImage decoded = new JxlDecoder { Coalesce = false }.DecodeRaw(Configuration.Default, stream, TestContext.Current.CancellationToken);

        // Assert: perceptual search must see exactly the decoder's reconstructed pixels.
        JxlRawFrame raw = Assert.Single(decoded.Frames);
        Assert.Equal(JxlRawColorSpace.Xyb, raw.ColorSpace);
        for (int c = 0; c < 3; c++)
        {
            for (int y = 0; y < height; y++)
            { Assert.True(reconstructed.Planes[c].DangerousGetRowSpan(y).SequenceEqual(raw.Planes[c].GetFloatRowSpan(y)), $"{width}x{height}, transform {transform}, channel {c}, row {y}"); }
        }
    }
}
