// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Container;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Butteraugli;
using CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlNoiseEstimatorTests
{
    [Theory]
    [InlineData(1, 1)] [InlineData(7, 32)] [InlineData(8, 8)] [InlineData(129, 65)]
    public void small_and_uniform_inputs_do_not_acquire_noise(int width, int height)
    {
        using JxlFloatFrame source = JxlButteraugliKernels.Create(width, height, 3, Configuration.Default.MemoryAllocator);
        for (int c = 0; c < 3; c++) { for (int y = 0; y < height; y++) { source.Planes[c].DangerousGetRowSpan(y).Fill(.5f); } }
        Assert.Null(JxlNoiseEstimator.Estimate(source, 2, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void estimated_noise_is_bounded_quantized_deterministic_and_does_not_edit_input()
    {
        using JxlFloatFrame source = JxlButteraugliKernels.Create(129, 65, 3, Configuration.Default.MemoryAllocator);
        Random random = new(931127);
        for (int y = 0; y < 65; y++) { for (int x = 0; x < 129; x++)
        { source.Planes[0].DangerousGetRowSpan(y)[x] = .02f; source.Planes[1].DangerousGetRowSpan(y)[x] = .6f + (float)(random.NextDouble() - .5) * .016f; } }
        float before = source.Planes[1].DangerousGetRowSpan(32)[32];
        float[] noise = JxlNoiseEstimator.Estimate(source, 2, TestContext.Current.CancellationToken);
        Assert.NotNull(noise); Assert.Equal(8, noise.Length); Assert.Contains(noise, value => value > 0);
        foreach (float value in noise) { Assert.InRange(value, 0, 1023f / 1024); Assert.Equal(MathF.Round(value * 1024), value * 1024); }
        Assert.Equal(noise, JxlNoiseEstimator.Estimate(source, 2, TestContext.Current.CancellationToken));
        Assert.Equal(before, source.Planes[1].DangerousGetRowSpan(32)[32]);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void noise_estimation_rejects_incompatible_coding_before_output(bool lossless)
    {
        using Image<Rgb24> source = new(1, 1); using MemoryStream stream = new();
        Assert.Throws<ArgumentException>(() => new JxlEncoder { NoiseModeling = true, Lossless = lossless, Modular = !lossless }.Encode(source, stream));
        Assert.Equal(0, stream.Length);
    }

    [Theory]
    [InlineData(JxlProgressiveMode.None)] [InlineData(JxlProgressiveMode.Ac)] [InlineData(JxlProgressiveMode.Full)]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_agrees_with_automatically_estimated_noise(JxlProgressiveMode progressive)
    {
        JxlExternalResources.RequireEnabled();
        using Image<Rgb24> source = new(129, 65); Random random = new(815511);
        for (int y = 0; y < source.Height; y++) { for (int x = 0; x < source.Width; x++)
        { byte sample = (byte)random.Next(96, 104); source[x, y] = new(sample, sample, sample); } }
        using MemoryStream encoded = new();
        new JxlEncoder { NoiseModeling = true, Effort = 7, Distance = 2, Progressive = progressive }.Encode(source, encoded);
        if (progressive != JxlProgressiveMode.Full)
        {
            encoded.Position = 0;
            using JxlCodestreamStream codestream = new(encoded, 1 << 20, false, TestContext.Current.CancellationToken);
            var reader = new CodeBrix.Imaging.Formats.Jxl.Bitstream.JxlBitReader(codestream, TestContext.Current.CancellationToken);
            JxlImageHeader header = JxlImageHeader.Read(reader, 1 << 20, 1 << 20);
            JxlFrameHeader frame = JxlFrameHeader.Read(reader, header, 1 << 20, 1 << 20);
            Assert.True((frame.Flags & 1) != 0, "The synthetic noisy input must exercise the estimated-noise path.");
        }
        Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken));
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-noise-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.jxl"), output = Path.Combine(directory, "output.png");
            await File.WriteAllBytesAsync(input, encoded.ToArray(), TestContext.Current.CancellationToken);
            await JxlNativeReference.RunAsync("djxl", [input, output, "--bits_per_sample=16", "--num_threads=1"], TestContext.Current.CancellationToken);
            encoded.Position = 0; using Image<Rgba64> managed = Image.Load<Rgba64>(encoded), native = Image.Load<Rgba64>(output);
            int peak = 0;
            for (int y = 0; y < managed.Height; y++) { for (int x = 0; x < managed.Width; x++)
            { Rgba64 a = managed[x, y], b = native[x, y]; peak = Math.Max(peak, Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)))); } }
            Assert.True(peak <= 8, $"Estimated-noise native pixel peak difference {peak}");
        }
        finally { Directory.Delete(directory, true); }
    }
}
