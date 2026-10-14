// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Modular.FastLossless;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlFastLosslessEncoderTests
{
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)] [InlineData(12)]
    [InlineData(13)] [InlineData(14)] [InlineData(15)] [InlineData(16)]
    public void fast_integer_depths_preserve_short_rows_runs_and_extreme_residuals(int depth)
    {
        foreach (int channels in new[] { 1, 2, 3, 4 })
        {
            foreach (int width in new[] { 1, 7, 8, 9, 257 })
            {
                byte[] bytes = Encode(width, 17, depth, channels);
                using JxlTestStream input = new(bytes, 3);
                using JxlRawImage raw = new JxlDecoder().DecodeRaw(Configuration.Default, input, TestContext.Current.CancellationToken);
                Assert.Single(raw.Frames); Assert.Equal(channels, raw.Frames[0].Planes.Count);
                for (int c = 0; c < channels; c++)
                {
                    for (int y = 0; y < 17; y++)
                    {
                        ReadOnlySpan<int> row = raw.Frames[0].Planes[c].GetIntegerRowSpan(y);
                        for (int x = 0; x < width; x++) { Assert.Equal(Sample(x, y, c, depth), row[x]); }
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(4103, 17)] [InlineData(17, 4103)] [InlineData(513, 259)]
    public void imports_are_bounded_to_groups_and_only_sampling_revisits_input(int width, int height)
    {
        List<Rectangle> calls = []; byte[] bytes = Encode(width, height, 16, 4, calls);
        int groups = ((width + 255) / 256) * ((height + 255) / 256);
        Assert.InRange(calls.Count, groups, groups + 1);
        long imported = 0;
        foreach (Rectangle bounds in calls)
        {
            Assert.InRange(bounds.Width, 1, 256); Assert.InRange(bounds.Height, 1, 256);
            imported += bounds.Width * bounds.Height;
        }
        Assert.InRange(imported, (long)width * height, (long)width * height + 65536);
        using MemoryStream input = new(bytes); using Image<Rgba64> decoded = Image.Load<Rgba64>(input);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            { Assert.Equal(new Rgba64((ushort)Sample(x, y, 0, 16), (ushort)Sample(x, y, 1, 16), (ushort)Sample(x, y, 2, 16), (ushort)Sample(x, y, 3, 16)), decoded[x, y]); }
        }
    }

    [Theory]
    [InlineData(8, 1, 1, 17)] [InlineData(8, 2, 9, 31)] [InlineData(8, 3, 259, 7)]
    [InlineData(8, 4, 8, 1)] [InlineData(13, 3, 259, 17)] [InlineData(14, 4, 17, 259)]
    [InlineData(15, 4, 7, 4103)] [InlineData(16, 4, 4103, 7)] [InlineData(16, 1, 257, 19)]
    [Trait("Category", "JxlNativeReference")]
    public async Task pinned_native_decoder_agrees_with_the_fast_lossless_stream(int depth, int channels, int width, int height)
    {
        JxlExternalResources.RequireEnabled();
        Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken));
        byte[] bytes = Encode(width, height, depth, channels);
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-fast-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.jxl"), output = Path.Combine(directory, "output.png");
            await File.WriteAllBytesAsync(input, bytes, TestContext.Current.CancellationToken);
            await JxlNativeReference.RunAsync("djxl", [input, output, "--bits_per_sample=16", "--num_threads=1"], TestContext.Current.CancellationToken);
            using Image<Rgba64> native = Image.Load<Rgba64>(output);
            using MemoryStream stream = new(bytes); using Image<Rgba64> managed = Image.Load<Rgba64>(stream);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Rgba64 a = managed[x, y], b = native[x, y];
                    if (depth is 8 or 16) { Assert.Equal(a, b); }
                    else
                    {
                        // Different float operation orders can round the expanded
                        // 16-bit PNG by one. Recover every original integer sample.
                        Assert.InRange(Math.Abs(a.R - b.R), 0, 1); Assert.InRange(Math.Abs(a.G - b.G), 0, 1);
                        Assert.InRange(Math.Abs(a.B - b.B), 0, 1); Assert.InRange(Math.Abs(a.A - b.A), 0, 1);
                        int maximum = (1 << depth) - 1;
                        Assert.Equal(Sample(x, y, 0, depth), (int)Math.Round(b.R * (double)maximum / 65535));
                        Assert.Equal(Sample(x, y, channels <= 2 ? 0 : 1, depth), (int)Math.Round(b.G * (double)maximum / 65535));
                        Assert.Equal(Sample(x, y, channels <= 2 ? 0 : 2, depth), (int)Math.Round(b.B * (double)maximum / 65535));
                        if (channels is 2 or 4) { Assert.Equal(Sample(x, y, channels - 1, depth), (int)Math.Round(b.A * (double)maximum / 65535)); }
                    }
                }
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static byte[] Encode(int width, int height, int depth, int channels, List<Rectangle> calls = null)
    {
        bool gray = channels <= 2, alpha = channels is 2 or 4;
        JxlBitDepth bits = new((uint)depth, 0);
        JxlImageHeader image = new() { Size = new((uint)width, (uint)height), XybEncoded = false, BitDepth = bits,
            Modular16BitBuffers = depth <= 12, Color = new() { ColorSpace = gray ? 1U : 0 }, ExtraChannels = alpha ? [new() { Type = 0, BitDepth = bits }] : [] };
        JxlFrameHeader frame = new() { Modular = true, Width = (uint)width, Height = (uint)height,
            ExtraUpsampling = alpha ? [1] : [], ExtraBlending = alpha ? [default] : [], Restoration = new() { GaborEnabled = false, EpfIterations = 0 } };
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream); image.Write(writer);
        JxlFastLosslessEncoder.WriteFrame(writer, image, frame, (bounds, planes) =>
        {
            calls?.Add(bounds);
            for (int c = 0; c < planes.Length; c++)
            {
                for (int y = 0; y < bounds.Height; y++)
                { for (int x = 0; x < bounds.Width; x++) { planes[c].Set(x, y, Sample(bounds.Left + x, bounds.Top + y, c, depth)); } }
            }
        }, Configuration.Default.MemoryAllocator, TestContext.Current.CancellationToken);
        writer.ZeroPadToByte(); return stream.ToArray();
    }

    private static int Sample(int x, int y, int c, int depth)
    {
        int maximum = (1 << depth) - 1;
        return y % 17 < 5 ? c == 0 ? 0 : maximum : y % 17 < 10 ? (x + y + c) % 2 * maximum :
            unchecked((x * 73856093) ^ (y * 19349663) ^ (c * 83492791)) & maximum;
    }
}
