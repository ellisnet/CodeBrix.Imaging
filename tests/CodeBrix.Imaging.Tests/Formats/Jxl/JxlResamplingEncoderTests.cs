// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlResamplingEncoderTests
{
    [Theory]
    [InlineData(1, 1, 2, 7)] [InlineData(1, 1, 2, 10)] [InlineData(7, 13, 2, 10)]
    [InlineData(19, 13, 4, 7)] [InlineData(263, 9, 8, 10)]
    public void downsampling_preserves_constant_positive_and_negative_planes(int width, int height, int factor, int effort)
    {
        var allocator = Configuration.Default.MemoryAllocator;
        using Buffer2D<float> source = allocator.Allocate2D<float>(width, height);
        foreach (float value in new[] { -3.5f, 0, .2f, 10f })
        {
            for (int y = 0; y < height; y++) { source.DangerousGetRowSpan(y).Fill(value); }
            using Buffer2D<float> down = JxlDownsampling.Downsample(source, factor, effort, allocator, TestContext.Current.CancellationToken);
            Assert.Equal((width + factor - 1) / factor, down.Width); Assert.Equal((height + factor - 1) / factor, down.Height);
            for (int y = 0; y < down.Height; y++)
            { foreach (float actual in down.DangerousGetRowSpan(y)) { Assert.Equal(value, actual, .00002f); } }
        }
    }

    [Theory]
    [InlineData(2)] [InlineData(4)] [InlineData(8)]
    public void box_sampling_averages_only_existing_pixels_at_odd_edges(int factor)
    {
        var allocator = Configuration.Default.MemoryAllocator;
        using Buffer2D<float> source = allocator.Allocate2D<float>(19, 13);
        for (int y = 0; y < 13; y++)
        { Span<float> row = source.DangerousGetRowSpan(y); for (int x = 0; x < 19; x++) { row[x] = x + 19 * y; } }
        using Buffer2D<float> down = JxlDownsampling.Box(source, factor, allocator, TestContext.Current.CancellationToken);
        for (int y = 0; y < down.Height; y++)
        {
            for (int x = 0; x < down.Width; x++)
            {
                float centerX = (x * factor + Math.Min(19, (x + 1) * factor) - 1) / 2f;
                float centerY = (y * factor + Math.Min(13, (y + 1) * factor) - 1) / 2f;
                Assert.Equal(centerX + 19 * centerY, down.DangerousGetRowSpan(y)[x]);
            }
        }
    }

    [Theory]
    [InlineData(2, 7, 1, 1, 1f)] [InlineData(2, 10, 7, 13, 1f)]
    [InlineData(2, 10, 263, 259, 12f)] [InlineData(4, 9, 263, 19, 20f)]
    [InlineData(8, 8, 263, 259, 3f)] [InlineData(2, 7, 4099, 9, 3f)]
    public void resampled_streams_keep_canvas_size_progression_and_constant_alpha(int factor, int effort, int width, int height, float distance)
    {
        using Image<Rgba64> source = Make(width, height, false); using MemoryStream encoded = new();
        new JxlEncoder { Resampling = factor, Effort = effort, Distance = distance, Progressive = JxlProgressiveMode.Ac, UseContainer = false }.Encode(source, encoded);
        encoded.Position = 0; JxlBitReader reader = new(encoded, TestContext.Current.CancellationToken);
        JxlImageHeader image = JxlImageHeader.Read(reader, 1 << 24, 1 << 20);
        JxlFrameHeader frame = JxlFrameHeader.Read(reader, image, 1 << 24, 1 << 20);
        Assert.Equal((uint)factor, frame.Upsampling); Assert.Equal((uint)factor, Assert.Single(frame.ExtraUpsampling));
        Assert.Equal(3U, frame.PassCount);
        encoded.Position = 0; using Image<Rgba64> decoded = Image.Load<Rgba64>(encoded);
        Assert.Equal(width, decoded.Width); Assert.Equal(height, decoded.Height);
        for (int y = 0; y < height; y++)
        { for (int x = 0; x < width; x++) { Assert.Equal(32123, decoded[x, y].A); } }
    }

    [Theory]
    [InlineData(2)] [InlineData(4)] [InlineData(8)]
    public void floating_and_half_alpha_are_averaged_as_samples_instead_of_integer_bit_patterns(int factor)
    {
        Verify<HalfVector4>(); Verify<RgbaVector>();
        void Verify<TPixel>() where TPixel : unmanaged, IPixel<TPixel>
        {
            using Image<TPixel> source = new(19, 13);
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                { TPixel pixel = default; pixel.FromVector4(new(.2f, .3f, .4f, .25f)); source[x, y] = pixel; }
            }
            using MemoryStream stream = new(); new JxlEncoder { Resampling = factor }.Encode(source, stream); stream.Position = 0;
            using JxlRawImage decoded = new JxlDecoder { Coalesce = false }.DecodeRaw(Configuration.Default, stream, TestContext.Current.CancellationToken);
            JxlRawPlane alpha = Assert.Single(decoded.Frames).Planes[3]; Assert.Equal(JxlSampleType.FloatingPoint, alpha.SampleType);
            for (int y = 0; y < alpha.Height; y++) { foreach (float value in alpha.GetFloatRowSpan(y)) { Assert.Equal(.25f, value); } }
        }
    }

    [Theory]
    [InlineData(2, 7, 1f)] [InlineData(2, 10, 1f)] [InlineData(2, 9, 12f)]
    [InlineData(4, 7, 3f)] [InlineData(4, 10, 20f)] [InlineData(8, 8, 3f)]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_agrees_with_resampled_colors_and_alpha(int factor, int effort, float distance)
    {
        JxlExternalResources.RequireEnabled();
        Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken));
        using Image<Rgba64> source = Make(263, 19, true); using MemoryStream encoded = new();
        new JxlEncoder { Resampling = factor, Effort = effort, Distance = distance, Progressive = JxlProgressiveMode.Ac }.Encode(source, encoded);
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-resampling-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.jxl"), output = Path.Combine(directory, "output.png");
            await File.WriteAllBytesAsync(input, encoded.ToArray(), TestContext.Current.CancellationToken);
            await JxlNativeReference.RunAsync("djxl", [input, output, "--bits_per_sample=16", "--num_threads=1"], TestContext.Current.CancellationToken);
            encoded.Position = 0; using Image<Rgba64> managed = Image.Load<Rgba64>(encoded), native = Image.Load<Rgba64>(output);
            int peak = 0, alphaPeak = 0;
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    Rgba64 a = managed[x, y], b = native[x, y];
                    peak = Math.Max(peak, Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B))));
                    alphaPeak = Math.Max(alphaPeak, Math.Abs(a.A - b.A));
                }
            }
            Assert.True(peak <= 8 && alphaPeak <= 1, $"Factor {factor}, effort {effort}: RGB peak {peak}, alpha peak {alphaPeak}");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static Image<Rgba64> Make(int width, int height, bool varyingAlpha)
    {
        Image<Rgba64> result = new(width, height);
        for (int y = 0; y < height; y++)
        { for (int x = 0; x < width; x++) { result[x, y] = new((ushort)(12000 + x * 117), (ushort)(17000 + y * 291), (ushort)(24000 + (x + y) * 57), varyingAlpha ? (ushort)((x + y) * 233) : (ushort)32123); } }
        return result;
    }
}
