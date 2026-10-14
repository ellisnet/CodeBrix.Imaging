// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlProgressiveEncoderTests
{
    [Theory]
    [InlineData(1, 1, 1)] [InlineData(19, 13, 7)] [InlineData(263, 259, 8)] [InlineData(2057, 9, 7)]
    public void dc_and_full_progression_emit_an_lf_frame_and_preserve_alpha(int width, int height, int effort)
    {
        using Image<Rgba64> source = Make(width, height);
        foreach (JxlProgressiveMode mode in new[] { JxlProgressiveMode.Dc, JxlProgressiveMode.Full })
        {
            using MemoryStream encoded = Encode(source, new() { Progressive = mode, Distance = .5f, Effort = effort });
            using JxlRawImage raw = new JxlDecoder { Coalesce = false, TargetColor = JxlTargetColor.Original }.DecodeRaw(Configuration.Default, encoded, TestContext.Current.CancellationToken);
            Assert.Equal(2, raw.Frames.Count); Assert.Equal(1, raw.Frames[0].Metadata.LowFrequencyLevel);
            Assert.Equal((width + 7) / 8, raw.Frames[0].Width); Assert.Equal((height + 7) / 8, raw.Frames[0].Height);
            encoded.Position = 0; using Image<Rgba64> actual = Image.Load<Rgba64>(encoded);
            for (int y = 0; y < height; y++) { for (int x = 0; x < width; x++) { Assert.Equal(source[x, y].A, actual[x, y].A); } }
        }
    }

    [Theory]
    [InlineData(JxlProgressiveMode.Dc)] [InlineData(JxlProgressiveMode.Ac)] [InlineData(JxlProgressiveMode.Full)]
    public void lossless_squeeze_progression_preserves_every_sample(JxlProgressiveMode mode)
    {
        using Image<Rgba64> source = Make(263, 259); using MemoryStream encoded = Encode(source, new() { Lossless = true, Progressive = mode });
        using Image<Rgba64> actual = Image.Load<Rgba64>(encoded);
        for (int y = 0; y < source.Height; y++) { Assert.True(source.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y).SequenceEqual(actual.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y))); }
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void explicit_raw_lf_chains_use_each_preceding_level(int levels)
    {
        using JxlRawImage source = Chain(levels); using MemoryStream encoded = new();
        new JxlEncoder { Effort = 1, Distance = .1f }.EncodeRaw(source, encoded, cancellationToken: TestContext.Current.CancellationToken); encoded.Position = 0;
        using JxlRawImage decoded = new JxlDecoder { Coalesce = false }.DecodeRaw(Configuration.Default, encoded, TestContext.Current.CancellationToken);
        Assert.Equal(levels + 1, decoded.Frames.Count);
        for (int i = 0; i <= levels; i++)
        {
            JxlRawFrame frame = decoded.Frames[i]; Assert.Equal(levels - i, frame.Metadata.LowFrequencyLevel);
            for (int y = 0; y < frame.Height; y++) { foreach (float value in frame.Planes[1].GetFloatRowSpan(y)) { Assert.Equal(.2f, value, .003f); } }
        }
    }

    [Theory]
    [InlineData(false, JxlProgressiveMode.Dc)] [InlineData(false, JxlProgressiveMode.Full)]
    [InlineData(true, JxlProgressiveMode.Dc)] [InlineData(true, JxlProgressiveMode.Full)]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_agrees_with_full_and_dc_progression(bool lossless, JxlProgressiveMode mode)
    {
        JxlExternalResources.RequireEnabled(); using Image<Rgba64> source = Make(263, 19);
        using MemoryStream encoded = Encode(source, new() { Lossless = lossless, Progressive = mode, Effort = 7 });
        await VerifyNative(encoded, lossless ? 0 : 8);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_agrees_with_explicit_lf_chains(int levels)
    {
        JxlExternalResources.RequireEnabled(); using JxlRawImage source = Chain(levels); using MemoryStream encoded = new();
        new JxlEncoder { Effort = 1, Distance = .1f }.EncodeRaw(source, encoded, cancellationToken: TestContext.Current.CancellationToken); encoded.Position = 0;
        await VerifyNative(encoded, 8);
    }

    [Fact]
    public void resampling_with_dc_progression_is_rejected_before_output()
    {
        using Image<Rgb24> source = new(1, 1); using MemoryStream output = new();
        Assert.Throws<ArgumentException>(() => new JxlEncoder { Resampling = 2, Progressive = JxlProgressiveMode.Dc }.Encode(source, output)); Assert.Equal(0, output.Length);
    }

    private static JxlRawImage Chain(int levels)
    {
        const int width = 513, height = 17; List<JxlRawFrame> frames = [];
        for (int level = levels; level >= 0; level--)
        {
            int divisor = 1 << (level * 3), w = (width + divisor - 1) / divisor, h = (height + divisor - 1) / divisor;
            JxlRawPlane[] planes = new JxlRawPlane[3];
            for (int c = 0; c < 3; c++) { float[] values = new float[w * h]; if (level == levels && c != 0) { Array.Fill(values, .2f); } planes[c] = JxlRawPlane.FromFloats(w, h, values, c); }
            JxlRawFrame frame = new(planes, JxlRawColorSpace.Xyb, new() { LowFrequencyLevel = level });
            frame.Encoding.UseLowFrequencyFrame = level != levels; frames.Add(frame);
        }
        return new(width, height, frames);
    }

    private static Image<Rgba64> Make(int width, int height)
    {
        Image<Rgba64> image = new(width, height);
        for (int y = 0; y < height; y++) { for (int x = 0; x < width; x++) { image[x, y] = new((ushort)(10000 + x * 113), (ushort)(15000 + y * 179), (ushort)(20000 + (x + y) * 61), (ushort)((x + y) * 239)); } }
        return image;
    }
    private static MemoryStream Encode(Image<Rgba64> source, JxlEncoder encoder)
    { MemoryStream output = new(); try { encoder.Encode(source, output); output.Position = 0; return output; } catch { output.Dispose(); throw; } }

    private static async Task VerifyNative(MemoryStream encoded, int tolerance)
    {
        Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken));
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-progressive-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.jxl"), output = Path.Combine(directory, "output.png");
            await File.WriteAllBytesAsync(input, encoded.ToArray(), TestContext.Current.CancellationToken);
            await JxlNativeReference.RunAsync("djxl", [input, output, "--bits_per_sample=16", "--num_threads=1"], TestContext.Current.CancellationToken);
            using Image<Rgba64> managed = Image.Load<Rgba64>(encoded), native = Image.Load<Rgba64>(output); int peak = 0;
            for (int y = 0; y < managed.Height; y++) { for (int x = 0; x < managed.Width; x++)
            { Rgba64 a = managed[x, y], b = native[x, y]; peak = Math.Max(peak, Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Max(Math.Abs(a.B - b.B), Math.Abs(a.A - b.A))))); } }
            Assert.True(peak <= tolerance, $"Native pixel peak difference {peak}");
        }
        finally { Directory.Delete(directory, true); }
    }
}
