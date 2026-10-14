// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlRawFeatureEncoderTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void patches_emit_every_blending_mode_with_independent_extra_channels(int mode)
    {
        using JxlRawImage source = Patches(mode); using MemoryStream bytes = Encode(source, true, true);
        using Image<Rgba32> actual = Image.Load<Rgba32>(bytes);
        Assert.Equal(new Rgba32(32, 48, 64, 128), actual[0, 0]);
        if (mode == 0) { Assert.Equal(actual[0, 0], actual[10, 8]); }
        else { Assert.NotEqual(actual[0, 0], actual[10, 8]); }
        if (mode == 1) { Assert.Equal(new Rgba32(80, 100, 120, 192), actual[10, 8]); }
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void quantized_splines_and_explicit_noise_change_supplied_residuals(bool modular, bool noise)
    {
        using JxlRawImage source = Spline(noise); using MemoryStream encoded = Encode(source, false, modular);
        using Image<Rgba64> decorated = Image.Load<Rgba64>(encoded);
        source.Frames[0].Encoding.Splines = []; source.Frames[0].Encoding.NoiseLut = null;
        using MemoryStream plain = Encode(source, false, modular); using Image<Rgba64> baseline = Image.Load<Rgba64>(plain);
        int different = 0;
        for (int y = 0; y < 31; y++) { for (int x = 0; x < 47; x++) { if (decorated[x, y] != baseline[x, y]) { different++; } } }
        Assert.True(different > 20);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_agrees_with_all_patch_blending_modes(int mode)
    {
        JxlExternalResources.RequireEnabled();
        using JxlRawImage source = Patches(mode); using MemoryStream encoded = Encode(source, true, true);
        await VerifyNative(encoded, 1);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_agrees_with_caller_supplied_splines_and_noise(bool modular, bool noise)
    {
        JxlExternalResources.RequireEnabled();
        using JxlRawImage source = Spline(noise); using MemoryStream encoded = Encode(source, false, modular);
        await VerifyNative(encoded, 12);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_agrees_with_patches_from_xyb_reference_frames(bool modular)
    {
        JxlExternalResources.RequireEnabled();
        using JxlRawImage source = Patches(1); using MemoryStream encoded = Encode(source, false, modular);
        await VerifyNative(encoded, 12);
    }

    [Fact]
    public void invalid_features_are_rejected_before_writing_output()
    {
        using JxlRawImage source = Spline(false); using MemoryStream output = new();
        source.Frames[0].Encoding.Splines[0].ControlPoints = [new(1, 1), new(1, 1)];
        Assert.Throws<ArgumentException>(() => new JxlEncoder().EncodeRaw(source, output, cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(0, output.Length);
        source.Frames[0].Encoding.Splines = []; source.Frames[0].Encoding.NoiseLut = new float[7];
        Assert.Throws<ArgumentException>(() => new JxlEncoder().EncodeRaw(source, output, cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(0, output.Length);
        source.Frames[0].Encoding.NoiseLut = null;
        source.Frames[0].Encoding.Patches = [new() { Reference = 3, Source = new(0, 0, 2, 2), Targets = [new() { Position = new(2, 2) }] }];
        Assert.Throws<ArgumentException>(() => new JxlEncoder().EncodeRaw(source, output, cancellationToken: TestContext.Current.CancellationToken)); Assert.Equal(0, output.Length);
    }

    private static JxlRawImage Patches(int mode)
    {
        JxlRawFrame reference = Rgba([80, 100, 120, 192], new() { IsReferenceOnly = true, SaveAsReference = 1, SaveBeforeColorTransform = true });
        JxlRawFrame residual = Rgba([32, 48, 64, 128], new());
        residual.Encoding.Patches = [new() { Reference = 1, Source = new(2, 1, 7, 5), Targets =
            [new() { Position = new(10, 8), Blending = new((JxlPatchBlendMode)mode, 0, true), ExtraChannelBlending = [new((JxlPatchBlendMode)mode, 0, true)] },
             new() { Position = new(3, 20), Blending = new((JxlPatchBlendMode)mode, 0, true), ExtraChannelBlending = [new((JxlPatchBlendMode)mode, 0, true)] }] }];
        return new(47, 31, [reference, residual]);
    }

    private static JxlRawFrame Rgba(int[] color, JxlFrameMetadata metadata)
    {
        JxlRawPlane[] planes = new JxlRawPlane[4];
        for (int c = 0; c < 4; c++)
        { int[] samples = new int[47 * 31]; Array.Fill(samples, color[c]); planes[c] = JxlRawPlane.FromIntegers(47, 31, samples, 8, c == 3 ? 0 : c, c == 3 ? new() : null); }
        return new(planes, metadata: metadata);
    }

    private static JxlRawImage Spline(bool noise)
    {
        JxlRawPlane[] planes = new JxlRawPlane[3];
        for (int c = 0; c < 3; c++) { float[] samples = new float[47 * 31]; Array.Fill(samples, c == 0 ? 0 : .2f); planes[c] = JxlRawPlane.FromFloats(47, 31, samples, c); }
        JxlRawFrame frame = new(planes, JxlRawColorSpace.Xyb);
        JxlSpline spline = new() { ControlPoints = [new(6, 7), new(20, 15), new(32, 21)] };
        spline.X[0] = 1; spline.Y[0] = 1; spline.Width[0] = 3;
        JxlSpline second = new() { ControlPoints = [new(30, 5), new(20, 10), new(10, 18)] };
        second.B[0] = 1; second.Width[0] = 2;
        frame.Encoding.Splines = [spline, second]; frame.Encoding.SplineQuantizationAdjustment = 2;
        if (noise) { frame.Encoding.NoiseLut = [.01f, .02f, .03f, .04f, .05f, .06f, .07f, .08f]; }
        return new(47, 31, [frame]);
    }

    private static MemoryStream Encode(JxlRawImage source, bool lossless, bool modular)
    {
        MemoryStream result = new();
        try { new JxlEncoder { Lossless = lossless, Modular = modular, Distance = .1f, Effort = 7, Progressive = JxlProgressiveMode.Ac }.EncodeRaw(source, result); result.Position = 0; return result; }
        catch { result.Dispose(); throw; }
    }

    private static async Task VerifyNative(MemoryStream encoded, int tolerance)
    {
        Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken));
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-raw-feature-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.jxl"), output = Path.Combine(directory, "output.png");
            await File.WriteAllBytesAsync(input, encoded.ToArray(), TestContext.Current.CancellationToken);
            await JxlNativeReference.RunAsync("djxl", [input, output, "--bits_per_sample=16", "--num_threads=1"], TestContext.Current.CancellationToken);
            using Image<Rgba64> managed = Image.Load<Rgba64>(encoded), native = Image.Load<Rgba64>(output); int peak = 0;
            for (int y = 0; y < managed.Height; y++)
            {
                for (int x = 0; x < managed.Width; x++)
                {
                    Rgba64 a = managed[x, y], b = native[x, y];
                    peak = Math.Max(peak, Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Max(Math.Abs(a.B - b.B), Math.Abs(a.A - b.A)))));
                }
            }
            Assert.True(peak <= tolerance, $"Native pixel peak difference {peak}");
        }
        finally { Directory.Delete(directory, true); }
    }
}
