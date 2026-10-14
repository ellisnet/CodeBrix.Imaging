// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Container;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlRawHeaderEncoderTests
{
    [Fact]
    public void custom_header_fields_are_rounded_to_the_encoded_precision()
    {
        using JxlRawImage source = Image(19, 13); Configure(source, 6, 2);
        source.Encoding.IntrinsicSize = new(200, 100); source.Encoding.MinimumNits = .0123f;
        source.Encoding.RelativeToMaximumDisplay = true; source.Encoding.LinearBelow = .314f;
        using MemoryStream encoded = Encode(source, 2);
        using JxlCodestreamStream codestream = new(encoded, 1 << 20, false, TestContext.Current.CancellationToken);
        JxlImageHeader header = JxlImageHeader.Read(new(codestream, TestContext.Current.CancellationToken), 1 << 20, 1 << 20);
        Assert.Equal(new JxlSizeHeader(200, 100), header.IntrinsicSize); Assert.Equal((float)(Half).0123f, header.MinNits);
        Assert.True(header.RelativeToMaxDisplay); Assert.Equal((float)(Half).314f, header.LinearBelow);
        Assert.True(header.CustomOpsin); Assert.Equal(1U, header.CustomWeightsMask);
        for (int i = 0; i < 16; i++) { Assert.Equal((float)(Half)source.Encoding.OpsinInverse[i], header.Opsin[i]); }
        Assert.Equal(2U, header.Color.Primaries); Assert.Equal(2U, header.Color.WhitePoint); Assert.True(header.Color.HasGamma);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void preview_geometry_is_independent_of_the_main_frame(bool lossless)
    {
        using JxlRawImage source = Image(263, 19); using JxlRawImage preview = Image(7, 3);
        source.Encoding.Preview = preview.Frames[0];
        using MemoryStream encoded = Encode(source, 1, lossless);
        var info = new JxlDecoder().Identify(Configuration.Default, encoded, TestContext.Current.CancellationToken);
        Assert.Equal(263, info.Width); Assert.Equal(19, info.Height); Assert.Equal(1, info.Metadata.GetJxlMetadata().FrameCount);
        encoded.Position = 0; using Image<Rgba64> actual = CodeBrix.Imaging.Image.Load<Rgba64>(encoded);
        Assert.Equal(263, actual.Width); Assert.Equal(19, actual.Height);
    }

    [Theory]
    [InlineData(0, 1)] [InlineData(1, 1)] [InlineData(2, 1)] [InlineData(3, 1)]
    [InlineData(4, 1)] [InlineData(5, 1)] [InlineData(6, 1)]
    [InlineData(6, 2)] [InlineData(6, 4)] [InlineData(6, 8)]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_agrees_with_enumerated_color_custom_opsin_and_upsampling(int kind, int factor)
    {
        JxlExternalResources.RequireEnabled(); using JxlRawImage source = Image(263, 19); Configure(source, kind, factor);
        using MemoryStream encoded = Encode(source, factor); await VerifyNative(encoded, false);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_reads_the_main_image_and_preview(bool lossless)
    {
        JxlExternalResources.RequireEnabled(); using JxlRawImage source = Image(263, 19); using JxlRawImage preview = Image(7, 3);
        source.Encoding.Preview = preview.Frames[0]; using MemoryStream encoded = Encode(source, 1, lossless); await VerifyNative(encoded, true);
    }

    [Fact]
    public void malformed_custom_header_is_rejected_before_output()
    {
        using JxlRawImage source = Image(3, 2); using MemoryStream output = new(); source.Encoding.OpsinInverse = new float[15];
        Assert.Throws<ArgumentException>(() => new JxlEncoder().EncodeRaw(source, output, cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(0, output.Length);
        source.Encoding.OpsinInverse = null; source.Encoding.ColorEncoding = new() { Gamma = float.NaN };
        Assert.Throws<ArgumentOutOfRangeException>(() => new JxlEncoder().EncodeRaw(source, output, cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(0, output.Length);
    }

    private static void Configure(JxlRawImage image, int kind, int factor)
    {
        image.Encoding.ColorEncoding = kind switch
        {
            1 => new() { Primaries = 11 }, 2 => new() { TransferFunction = 8 },
            3 => new() { Primaries = 9, TransferFunction = 16 }, 4 => new() { Primaries = 9, TransferFunction = 18 },
            5 => new() { Gamma = 1f / 2.2f }, 6 => new() { WhitePoint = 2, Primaries = 2, Gamma = 1f / 2.2f }, _ => new(),
        };
        if (kind is 3 or 4) { image.Metadata.GetJxlMetadata().IntensityTarget = 1000; }
        if (kind == 6)
        {
            image.Encoding.OpsinInverse = JxlRawImageEncoding.DefaultOpsinInverse;
            image.Encoding.OpsinInverse[0] *= 1.01f; image.Encoding.OpsinInverse[4] *= .99f;
        }
        JxlImageHeader defaults = new();
        if (factor == 2) { image.Encoding.UpsamplingWeights2 = (float[])defaults.Up2.Clone(); }
        if (factor == 4) { image.Encoding.UpsamplingWeights4 = (float[])defaults.Up4.Clone(); }
        if (factor == 8) { image.Encoding.UpsamplingWeights8 = (float[])defaults.Up8.Clone(); }
    }

    private static JxlRawImage Image(int width, int height)
    {
        JxlRawPlane[] planes = new JxlRawPlane[3];
        for (int c = 0; c < 3; c++)
        {
            float[] samples = new float[width * height];
            for (int y = 0; y < height; y++) { for (int x = 0; x < width; x++) { samples[y * width + x] = .15f + c * .1f + x / 1000f + y / 400f; } }
            planes[c] = JxlRawPlane.FromFloats(width, height, samples, c);
        }
        return new(width, height, [new(planes)]);
    }

    private static MemoryStream Encode(JxlRawImage source, int factor, bool lossless = false)
    {
        MemoryStream output = new();
        try { new JxlEncoder { Lossless = lossless, Resampling = factor, Effort = 7, Distance = .3f }.EncodeRaw(source, output, cancellationToken: TestContext.Current.CancellationToken); output.Position = 0; return output; }
        catch { output.Dispose(); throw; }
    }

    private static async Task VerifyNative(MemoryStream encoded, bool hasPreview)
    {
        Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken));
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-header-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.jxl"), output = Path.Combine(directory, "output.png"), preview = Path.Combine(directory, "preview.npy");
            await File.WriteAllBytesAsync(input, encoded.ToArray(), TestContext.Current.CancellationToken);
            string[] arguments = [input, output, "--bits_per_sample=16", "--num_threads=1", "--color_space=RGB_D65_SRG_Rel_SRG"];
            await JxlNativeReference.RunAsync("djxl", arguments, TestContext.Current.CancellationToken);
            if (hasPreview)
            {
                // The pinned CLI's NPY writer emits preview_bitstream; its PNG writer does not.
                await JxlNativeReference.RunAsync("djxl", [input, Path.Combine(directory, "main.npy"), "--preview_out=" + preview, "--num_threads=1"], TestContext.Current.CancellationToken);
                using JxlNpyImage thumb = JxlNpyImage.Open(preview); Assert.Equal(7, thumb.Width); Assert.Equal(3, thumb.Height);
                foreach (float value in thumb.ReadFrame(0)) { Assert.True(float.IsFinite(value)); }
            }
            using Image<Rgba64> managed = new JxlDecoder { TargetColor = JxlTargetColor.Srgb }.Decode<Rgba64>(Configuration.Default, encoded, TestContext.Current.CancellationToken),
                native = CodeBrix.Imaging.Image.Load<Rgba64>(output); int peak = 0;
            for (int y = 0; y < managed.Height; y++) { for (int x = 0; x < managed.Width; x++)
            { Rgba64 a = managed[x, y], b = native[x, y]; peak = Math.Max(peak, Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)))); } }
            Assert.True(peak <= 12, $"Native pixel peak difference {peak}");
        }
        finally { Directory.Delete(directory, true); }
    }
}
