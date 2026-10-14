// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlEncoderIntegrationTests
{
    [Theory]
    [InlineData(7, JxlProgressiveMode.None, 0)]
    [InlineData(8, JxlProgressiveMode.Ac, 2)]
    [InlineData(9, JxlProgressiveMode.Full, 0)]
    [InlineData(10, JxlProgressiveMode.Dc, 4)]
    public void raw_features_matrices_preview_and_progression_preserve_extra_channels(int effort, JxlProgressiveMode mode, int speed)
    {
        using JxlRawImage source = Source();
        using JxlRawFrame preview = Frame(7, 3, false);
        source.Encoding.Preview = preview;
        using MemoryStream stream = Encode(source, effort, mode, speed);
        using Image<Rgba64> decoded = Image.Load<Rgba64>(stream);
        Assert.Equal(65, decoded.Width); Assert.Equal(35, decoded.Height);
        JxlRawPlane alpha = source.Frames[^1].Planes[3];
        for (int y = 0; y < decoded.Height; y++)
        {
            for (int x = 0; x < decoded.Width; x++) { Assert.Equal(alpha.GetIntegerRowSpan(y)[x], decoded[x, y].A); }
        }
        // Explicit frame features remain meaningful with the other controls.
        source.Frames[^1].Encoding.Patches = [];
        source.Frames[^1].Encoding.Splines = [];
        source.Frames[^1].Encoding.NoiseLut = new float[8];
        using MemoryStream plain = Encode(source, effort, mode, speed);
        using Image<Rgba64> baseline = Image.Load<Rgba64>(plain);
        int different = 0;
        for (int y = 0; y < decoded.Height; y++) { for (int x = 0; x < decoded.Width; x++) { if (decoded[x, y] != baseline[x, y]) { different++; } } }
        Assert.True(different > 100);
    }

    [Theory]
    [InlineData(8, JxlProgressiveMode.Ac, 2)]
    [InlineData(9, JxlProgressiveMode.Full, 0)]
    [InlineData(10, JxlProgressiveMode.Dc, 4)]
    [InlineData(8, JxlProgressiveMode.Ac, 2, false)]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_agrees_with_combined_raw_features(int effort, JxlProgressiveMode mode, int speed, bool hasPreview = true)
    {
        JxlExternalResources.RequireEnabled();
        using JxlRawImage source = Source(); using JxlRawFrame preview = Frame(7, 3, false);
        source.Encoding.Preview = hasPreview ? preview : null;
        using MemoryStream encoded = Encode(source, effort, mode, speed);
        Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken));
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-combined-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.jxl"), output = Path.Combine(directory, "output.png");
            await File.WriteAllBytesAsync(input, encoded.ToArray(), TestContext.Current.CancellationToken);
            await JxlNativeReference.RunAsync("djxl", [input, output, "--bits_per_sample=16", "--num_threads=1"], TestContext.Current.CancellationToken);
            using Image<Rgba64> managed = Image.Load<Rgba64>(encoded), native = Image.Load<Rgba64>(output);
            int peak = 0;
            for (int y = 0; y < managed.Height; y++)
            {
                for (int x = 0; x < managed.Width; x++)
                {
                    Rgba64 a = managed[x, y], b = native[x, y]; Assert.Equal(a.A, b.A);
                    peak = Math.Max(peak, Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B))));
                }
            }
            Assert.True(peak <= 12, $"Combined-feature native pixel peak difference {peak}");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static JxlRawImage Source()
    {
        JxlRawFrame reference = Frame(65, 35, true), residual = Frame(65, 35, false);
        reference.Metadata.IsReferenceOnly = true;
        reference.Metadata.SaveAsReference = 1;
        reference.Metadata.SaveBeforeColorTransform = true;
        residual.Encoding.Patches = [new()
        {
            Reference = 1, Source = new(2, 1, 9, 7), Targets =
            [new() { Position = new(10, 8), Blending = new(JxlPatchBlendMode.Add) },
             new() { Position = new(45, 24), Blending = new(JxlPatchBlendMode.Add) }],
        }];
        JxlSpline spline = new() { ControlPoints = [new(6, 7), new(20, 15), new(32, 21)] };
        spline.Y[0] = 1; spline.Width[0] = 3;
        residual.Encoding.Splines = [spline];
        residual.Encoding.NoiseLut = [.01f, .02f, .03f, .04f, .05f, .06f, .07f, .08f];
        JxlQuantizationMatrix matrix = JxlQuantizationMatrix.CreateDefault(JxlDctTransform.Dct16x8);
        foreach (float[] values in new[] { matrix.X, matrix.Y, matrix.B })
        { for (int i = 0; i < values.Length; i++) { values[i] *= 1.3f; } }
        residual.Encoding.Transform = matrix.Transform;
        residual.Encoding.QuantizationMatrices = [matrix];
        return new(65, 35, [reference, residual]);
    }

    private static JxlRawFrame Frame(int width, int height, bool reference)
    {
        JxlRawPlane[] planes = new JxlRawPlane[4];
        for (int c = 0; c < 3; c++)
        {
            float[] values = new float[width * height];
            for (int i = 0; i < values.Length; i++) { values[i] = c == 0 ? .001f : reference ? .05f : .2f + (i % 23) * .0001f; }
            planes[c] = JxlRawPlane.FromFloats(width, height, values, c);
        }
        int[] alpha = new int[width * height];
        for (int i = 0; i < alpha.Length; i++) { alpha[i] = 10000 + i * 19 % 50000; }
        planes[3] = JxlRawPlane.FromIntegers(width, height, alpha, 16, extraChannel: new());
        return new(planes, JxlRawColorSpace.Xyb);
    }

    private static MemoryStream Encode(JxlRawImage image, int effort, JxlProgressiveMode mode, int speed)
    {
        MemoryStream output = new();
        try
        {
            new JxlEncoder { Distance = .7f, Effort = effort, Progressive = mode, FasterDecoding = speed, PhotonNoiseIso = 3200, NoiseModeling = true }
                .EncodeRaw(image, output, cancellationToken: TestContext.Current.CancellationToken);
            output.Position = 0; return output;
        }
        catch { output.Dispose(); throw; }
    }
}
