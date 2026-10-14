// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Container;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Butteraugli;
using CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlDotEncoderTests
{
    [Fact]
    public void uniform_input_does_not_produce_dot_patches()
    {
        using JxlFloatFrame source = JxlButteraugliKernels.Create(65, 35, 3, Configuration.Default.MemoryAllocator);
        foreach (var plane in source.Planes) { for (int y = 0; y < plane.Height; y++) { plane.DangerousGetRowSpan(y).Fill(.25f); } }
        Assert.Null(JxlLossyPatchEncoder.FindDots(source, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void detected_dot_atlas_restores_the_samples_subtracted_from_the_residual()
    {
        using JxlFloatFrame source = Xyb();
        float[][] original = new float[3][];
        for (int c = 0; c < 3; c++)
        {
            original[c] = new float[65 * 65];
            for (int y = 0; y < 65; y++) { source.Planes[c].DangerousGetRowSpan(y).CopyTo(original[c].AsSpan(y * 65)); }
        }
        JxlLossyPatchEncoder dots = JxlLossyPatchEncoder.FindDots(source, TestContext.Current.CancellationToken);
        Assert.NotNull(dots); dots.Apply(source, true, TestContext.Current.CancellationToken); float difference = 0;
        for (int c = 0; c < 3; c++) { for (int y = 0; y < 65; y++) { for (int x = 0; x < 65; x++)
        { difference = Math.Max(difference, Math.Abs(original[c][y * 65 + x] - source.Planes[c].DangerousGetRowSpan(y)[x])); } } }
        Assert.True(difference > .1f); dots.Apply(source, false, TestContext.Current.CancellationToken);
        for (int c = 0; c < 3; c++) { for (int y = 0; y < 65; y++) { for (int x = 0; x < 65; x++)
        { Assert.InRange(Math.Abs(original[c][y * 65 + x] - source.Planes[c].DangerousGetRowSpan(y)[x]), 0, 1e-6f); } } }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void public_dot_override_controls_reference_frame_emission(bool enabled)
    {
        using Image<Rgb24> source = new(65, 65);
        for (int y = 0; y < 65; y++) { for (int x = 0; x < 65; x++)
        { byte value = (byte)Math.Round(20 + 230 * Peak(x, y)); source[x, y] = new(value, value, value); } }
        using MemoryStream stream = new(); new JxlEncoder { Effort = 7, Distance = 3, Patches = false, Dots = enabled }.Encode(source, stream);
        stream.Position = 0; using JxlCodestreamStream codestream = new(stream, 1 << 20, false, TestContext.Current.CancellationToken);
        JxlBitReader reader = new(codestream, TestContext.Current.CancellationToken); JxlImageHeader header = JxlImageHeader.Read(reader, 1 << 20, 1 << 20);
        Assert.Equal(enabled ? 2U : 0U, JxlFrameHeader.Read(reader, header, 1 << 20, 1 << 20).FrameType);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_agrees_with_detected_dots_and_progression(bool progressive)
    {
        JxlExternalResources.RequireEnabled(); using JxlFloatFrame source = Xyb(); using MemoryStream encoded = new();
        JxlImageHeader header = new() { Size = new(65, 65), XybEncoded = true, BitDepth = new(8, 0), Modular16BitBuffers = false };
        JxlFrameHeader frame = new() { Width = 65, Height = 65 };
        JxlBitWriter writer = new(encoded); header.Write(writer);
        JxlVarDctEncoder.WriteFrame(writer, header, frame, source, [], 3, 7, Configuration.Default.MemoryAllocator, TestContext.Current.CancellationToken,
            progressive: progressive, progressiveDc: progressive, automaticDots: true);
        writer.ZeroPadToByte();
        Assert.True((frame.Flags & 2) != 0, "The source must exercise automatic dot patches.");
        Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken));
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-dots-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.jxl"), output = Path.Combine(directory, "output.png");
            await File.WriteAllBytesAsync(input, encoded.ToArray(), TestContext.Current.CancellationToken);
            await JxlNativeReference.RunAsync("djxl", [input, output, "--bits_per_sample=16", "--num_threads=1"], TestContext.Current.CancellationToken);
            encoded.Position = 0; using Image<Rgba64> managed = Image.Load<Rgba64>(encoded), native = Image.Load<Rgba64>(output);
            int peak = 0;
            for (int y = 0; y < managed.Height; y++) { for (int x = 0; x < managed.Width; x++)
            { Rgba64 a = managed[x, y], b = native[x, y]; peak = Math.Max(peak, Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)))); } }
            Assert.True(peak <= 8, $"Dot-patch native pixel peak difference {peak}");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static JxlFloatFrame Xyb()
    {
        JxlFloatFrame source = JxlButteraugliKernels.Create(65, 65, 3, Configuration.Default.MemoryAllocator);
        for (int y = 0; y < 65; y++) { for (int x = 0; x < 65; x++)
        { source.Planes[0].DangerousGetRowSpan(y)[x] = 0; source.Planes[1].DangerousGetRowSpan(y)[x] = source.Planes[2].DangerousGetRowSpan(y)[x] = .08f + .65f * (float)Peak(x, y); } }
        return source;
    }

    private static double Peak(int x, int y)
    {
        int dx = x - (12 + (int)Math.Round((x - 12) / 20.0) * 20), dy = y - (12 + (int)Math.Round((y - 12) / 20.0) * 20);
        return Math.Exp(-.5 * (dx * dx + dy * dy) / .3);
    }
}
