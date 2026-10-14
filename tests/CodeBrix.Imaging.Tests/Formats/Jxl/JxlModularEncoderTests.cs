// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Modular;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlModularEncoderTests
{
    [Theory]
    [InlineData(8, 1, true, false, 7)]
    [InlineData(8, 4, true, false, 7)]
    [InlineData(16, 3, true, false, 7)]
    [InlineData(16, 4, true, false, 7)]
    [InlineData(8, 4, false, false, 7)]
    [InlineData(16, 3, false, false, 7)]
    [InlineData(16, 4, false, false, 7)]
    [InlineData(8, 4, true, true, 1)]
    [InlineData(16, 4, true, true, 7)]
    [InlineData(16, 3, false, true, 8)]
    [InlineData(8, 4, false, true, 11)]
    [InlineData(16, 3, false, true, 2)]
    [InlineData(16, 3, false, true, 3)]
    [InlineData(16, 3, false, true, 4)]
    [InlineData(16, 3, false, true, 5)]
    [InlineData(16, 3, false, true, 6)]
    [InlineData(16, 3, false, true, 9)]
    [InlineData(16, 3, false, true, 10)]
    [Trait("Category", "JxlNativeReference")]
    public async Task pinned_djxl_decodes_managed_modular_output_exactly(int bits, int channels, bool usePrefix, bool patterned, int effort)
    {
        JxlExternalResources.RequireEnabled();
        string version = await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken);
        Assert.Contains("ef67fde2", version);
        const int width = 263, height = 17;
        int[][] samples = new int[channels][];
        Random random = new(15783);
        for (int c = 0; c < channels; c++)
        {
            samples[c] = new int[width * height];
            for (int i = 0; i < samples[c].Length; i++)
            { samples[c][i] = patterned ? (((i % 47) * 137) + (c * 31)) & ((1 << bits) - 1) : random.Next(1 << bits); }
        }
        if (channels == 4) { samples[3][0] = 0; }
        byte[] encoded = EncodePlanes(width, height, new((uint)bits, 0), samples, usePrefix, effort);
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-encode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.jxl"), output = Path.Combine(directory, "decoded.png");
            await File.WriteAllBytesAsync(input, encoded, TestContext.Current.CancellationToken);
            await JxlNativeReference.RunAsync("djxl", [input, output, "--bits_per_sample=16", "--num_threads=1"], TestContext.Current.CancellationToken);
            using Image<Rgba64> decoded = Image.Load<Rgba64>(output);
            int scale = bits == 8 ? 257 : 1;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = (y * width) + x; Rgba64 pixel = decoded[x, y];
                    Assert.Equal(samples[0][i] * scale, pixel.R);
                    Assert.Equal(samples[channels == 1 ? 0 : 1][i] * scale, pixel.G);
                    Assert.Equal(samples[channels == 1 ? 0 : 2][i] * scale, pixel.B);
                    Assert.Equal(channels == 4 ? samples[3][i] * scale : ushort.MaxValue, pixel.A);
                }
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(1, 1, 8, 1)]
    [InlineData(17, 13, 8, 4)]
    [InlineData(257, 9, 16, 3)]
    [InlineData(9, 257, 31, 4)]
    [InlineData(511, 263, 8, 4)]
    [InlineData(3, 7, 31, 1)]
    public void modular_writer_preserves_integer_planes_across_groups(int width, int height, int bits, int channels)
    {
        int[][] values = new int[channels][];
        Random random = new(4790);
        for (int c = 0; c < channels; c++)
        {
            values[c] = new int[width * height];
            for (int i = 0; i < values[c].Length; i++) { values[c][i] = (int)random.NextInt64(0, 1L << bits); }
            values[c][0] = 0; values[c][^1] = (int)((1L << bits) - 1);
        }
        byte[] encoded = EncodePlanes(width, height, new((uint)bits, 0), values);
        using JxlTestStream stream = new(encoded, 7);
        using JxlRawImage actual = new JxlDecoder { Coalesce = false }.DecodeRaw(Configuration.Default, stream, TestContext.Current.CancellationToken);
        Assert.Single(actual.Frames);
        for (int c = 0; c < channels; c++)
        {
            JxlRawPlane plane = actual.Frames[0].Planes[c];
            Assert.Equal(JxlSampleType.Integer, plane.SampleType);
            for (int y = 0; y < height; y++) { Assert.True(values[c].AsSpan(y * width, width).SequenceEqual(plane.GetIntegerRowSpan(y))); }
        }
    }

    [Fact]
    public void modular_writer_preserves_binary32_patterns()
    {
        int[] patterns = [0, int.MinValue, 1, unchecked((int)0x80000001), 0x3f800000, 0x7f800000,
            unchecked((int)0xff800000), 0x7fc12345, unchecked((int)0xffc54321), 0x7f7fffff];
        byte[] encoded = EncodePlanes(patterns.Length, 1, new(32, 8), [patterns]);
        using MemoryStream stream = new(encoded, false);
        using JxlRawImage actual = new JxlDecoder { Coalesce = false }.DecodeRaw(Configuration.Default, stream, TestContext.Current.CancellationToken);
        ReadOnlySpan<float> row = actual.Frames[0].Planes[0].GetFloatRowSpan(0);
        for (int i = 0; i < patterns.Length; i++) { Assert.Equal(patterns[i], BitConverter.SingleToInt32Bits(row[i])); }
    }

    internal static byte[] EncodePlanes(int width, int height, JxlBitDepth depth, int[][] samples, bool usePrefix = true, int effort = 7)
    {
        using JxlModularWorkspace workspace = new(Configuration.Default.MemoryAllocator, (long)width * height);
        JxlModularPlane[] planes = new JxlModularPlane[samples.Length];
        for (int c = 0; c < planes.Length; c++)
        {
            planes[c] = workspace.Allocate(width, height);
            for (int y = 0; y < height; y++) { samples[c].AsSpan(y * width, width).CopyTo(planes[c].Row(y)); }
        }
        bool alpha = planes.Length == 4;
        JxlImageHeader image = new() { Size = new((uint)width, (uint)height), XybEncoded = false,
            Color = new() { ColorSpace = planes.Length == 1 ? 1U : 0 }, BitDepth = depth, Modular16BitBuffers = depth.Bits <= 12,
            ExtraChannels = alpha ? [new() { Type = 0, BitDepth = depth }] : [] };
        JxlFrameHeader frame = new() { Modular = true, Width = (uint)width, Height = (uint)height,
            ExtraUpsampling = alpha ? [1] : [], ExtraBlending = alpha ? [default] : [],
            Restoration = new() { GaborEnabled = false, EpfIterations = 0 } };
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream); image.Write(writer);
        JxlModularEncoder.WriteFrame(writer, image, frame, planes, TestContext.Current.CancellationToken, usePrefix, effort);
        return stream.ToArray();
    }
}
