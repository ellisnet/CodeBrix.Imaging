// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Numerics;
using CodeBrix.Imaging.Formats.Jxl.Color;
using CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.VarDct;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlForwardTransformTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)] [InlineData(12)] [InlineData(13)]
    [InlineData(14)] [InlineData(15)] [InlineData(16)] [InlineData(17)] [InlineData(18)] [InlineData(19)] [InlineData(20)]
    [InlineData(21)] [InlineData(22)] [InlineData(23)] [InlineData(24)] [InlineData(25)] [InlineData(26)]
    public void forward_coefficients_match_pinned_native_transform_vectors(int index)
    {
        using FileStream stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "SampleFiles", "JxlSynthetic", "encoder-transform-reference.bin"));
        using BinaryReader reader = new(stream);
        for (int current = 0; current <= index; current++)
        {
            Assert.Equal(current, reader.ReadInt32()); int width = reader.ReadInt32(), height = reader.ReadInt32();
            if (current != index) { stream.Seek(width * height * sizeof(float), SeekOrigin.Current); continue; }
            JxlTransformType type = (JxlTransformType)index; (int bw, int bh) = type.Blocks();
            Assert.Equal(bw * 8, width); Assert.Equal(bh * 8, height);
            float[] values = new float[width * height], expected = new float[values.Length];
            for (int i = 0; i < expected.Length; i++) { expected[i] = reader.ReadSingle(); }
            for (int y = 0; y < height; y++)
            { for (int x = 0; x < width; x++) { values[y * width + x] = ((x * 37 + y * 191 + x * y * 17) % 257 - 128) * .0078125f + ((x * 3 + y * 7) % 11 == 0 ? .35f : 0); } }
            JxlForwardTransform.Apply(values, type); float peak = 0;
            for (int y = 0; y < height; y++)
            { for (int x = 0; x < width; x++) { peak = Math.Max(peak, Math.Abs(values[y * width + x] - expected[type.Transpose() ? x * height + y : y * width + x])); } }
            Assert.True(peak <= .000002f, $"{type}: maximum native coefficient difference {peak}");
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)] [InlineData(12)] [InlineData(13)]
    [InlineData(14)] [InlineData(15)] [InlineData(16)] [InlineData(17)] [InlineData(18)] [InlineData(19)] [InlineData(20)]
    [InlineData(21)] [InlineData(22)] [InlineData(23)] [InlineData(24)] [InlineData(25)] [InlineData(26)]
    public void all_transform_types_preserve_dc_impulses_and_random_blocks(int index)
    {
        JxlTransformType type = (JxlTransformType)index; (int width, int height) = type.Blocks();
        float[] original = new float[width * height * 64], transformed = new float[original.Length];
        Array.Fill(original, .375f); original.CopyTo(transformed, 0); JxlForwardTransform.Apply(transformed, type);
        Assert.InRange(Math.Abs(transformed[0] - .375f), 0, .000002f);
        for (int i = 1; i < transformed.Length; i++) { Assert.InRange(Math.Abs(transformed[i]), 0, .000002f); }
        Random random = new(84659);
        for (int i = 0; i < original.Length; i++) { original[i] = random.NextSingle() * 2 - 1; }
        RoundTrip(); Array.Clear(original); original[original.Length / 3] = 1; RoundTrip();

        void RoundTrip()
        {
            original.CopyTo(transformed, 0); JxlForwardTransform.Apply(transformed, type); JxlInverseTransform.Apply(transformed, type);
            float peak = 0; for (int i = 0; i < original.Length; i++) { peak = Math.Max(peak, Math.Abs(original[i] - transformed[i])); }
            Assert.True(peak <= .00002f, $"{type}: maximum reconstruction error {peak}");
        }
    }

    [Theory]
    [InlineData(80)] [InlineData(203)] [InlineData(255)] [InlineData(1000)] [InlineData(10000)]
    public void forward_xyb_and_decoder_preserve_linear_rgb_at_sdr_and_hdr_intensities(float intensity)
    {
        JxlXybEncoder encoder = new(intensity);
        JxlColorTransform decoder = new(new JxlImageHeader { XybEncoded = true, IntensityTarget = intensity }, false, true);
        Random random = new(58916);
        Verify(Vector3.Zero); Verify(Vector3.One); Verify(Vector3.UnitX); Verify(Vector3.UnitY); Verify(Vector3.UnitZ);
        // Extended-gamut samples can have negative RGB while all absorbances remain positive.
        Verify(new(-.1f, 1, .5f)); Verify(new(.5f, -.05f, 1));
        for (int i = 0; i < 1000; i++) { Verify(new(random.NextSingle() * 16, random.NextSingle() * 16, random.NextSingle() * 16)); }

        void Verify(Vector3 original)
        {
            Vector3 actual = decoder.Convert(encoder.FromLinearSrgb(original));
            float scale = Math.Max(1, original.Length());
            Assert.True(Vector3.Distance(original, actual) <= scale * .00002f, $"Intensity {intensity}: {original} → {actual}");
        }
    }
}
