// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Modular;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Formats.Jxl.VarDct;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlRawQuantizationTests
{
    public static IEnumerable<object[]> Transforms()
    { foreach (JxlDctTransform transform in Enum.GetValues<JxlDctTransform>()) { yield return [transform]; } }

    [Theory]
    [MemberData(nameof(Transforms))]
    public void custom_matrices_preserve_the_values_used_by_the_quantizer(JxlDctTransform transform)
    {
        JxlQuantizationMatrix input = Matrix(transform);
        JxlDequantMatrices expected = JxlDequantMatrices.Create([input]);
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream);
        expected.Write(writer, Configuration.Default.MemoryAllocator, (bits, planes) =>
            JxlModularEncoder.WriteStreamCore(bits, planes, 0, null, null, true, 1, Configuration.Default.MemoryAllocator, TestContext.Current.CancellationToken));
        writer.WriteBits(21, 0x135792); writer.ZeroPadToByte(); stream.Position = 0;
        JxlBitReader reader = new(stream, TestContext.Current.CancellationToken);
        using JxlModularWorkspace workspace = new(Configuration.Default.MemoryAllocator, 262144);
        JxlDequantMatrices actual = JxlDequantMatrices.Read(reader, 8, 1, null, workspace);
        foreach (JxlDctTransform type in Enum.GetValues<JxlDctTransform>())
        { for (int c = 0; c < 3; c++) { Assert.Equal(expected.Get((JxlTransformType)type, c), actual.Get((JxlTransformType)type, c)); } }
        Assert.Equal(0x135792U, reader.ReadBits(21));
    }

    [Theory]
    [MemberData(nameof(Transforms))]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_agrees_with_explicit_transforms_and_matrices(JxlDctTransform transform)
    {
        JxlExternalResources.RequireEnabled();
        (int bw, int bh) = ((JxlTransformType)transform).Blocks();
        using JxlRawImage source = Source(bw * 8 + 3, bh * 8 + 5);
        source.Frames[0].Encoding.Transform = transform;
        source.Frames[0].Encoding.QuantizationMatrices = [Matrix(transform)];
        using MemoryStream encoded = new();
        new JxlEncoder { Effort = 1, Distance = .3f }.EncodeRaw(source, encoded, cancellationToken: TestContext.Current.CancellationToken);
        await VerifyNative(encoded);
    }

    [Theory]
    [InlineData(7)] [InlineData(8)]
    [Trait("Category", "JxlNativeReference")]
    public async Task automatic_and_perceptual_search_use_custom_quantization(int effort)
    {
        JxlExternalResources.RequireEnabled(); using JxlRawImage source = Source(65, 35);
        source.Frames[0].Encoding.QuantizationMatrices = [Matrix(JxlDctTransform.Dct8), Matrix(JxlDctTransform.Dct16), Matrix(JxlDctTransform.Afv0)];
        using MemoryStream encoded = new();
        new JxlEncoder { Effort = effort, Distance = .7f, Progressive = JxlProgressiveMode.Full }.EncodeRaw(source, encoded, cancellationToken: TestContext.Current.CancellationToken);
        await VerifyNative(encoded);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void malformed_or_inapplicable_matrices_fail_before_any_output(int kind)
    {
        using JxlRawImage source = Source(8, 8); JxlRawFrameEncoding coding = source.Frames[0].Encoding;
        JxlQuantizationMatrix matrix = Matrix(JxlDctTransform.Dct8); coding.QuantizationMatrices = [matrix];
        switch (kind)
        {
            case 0: matrix.X[0] = float.NaN; break;
            case 1: matrix.Y = new float[65]; break;
            case 2: matrix.B[0] = 0; break;
            case 3: matrix.X[0] = float.Epsilon; matrix.X[1] = 99_000_000; break;
            case 4: coding.QuantizationMatrices = [Matrix(JxlDctTransform.Afv0), Matrix(JxlDctTransform.Afv3)]; break;
            case 5: coding.Transform = (JxlDctTransform)99; break;
        }
        using MemoryStream output = new();
        JxlEncoder encoder = new() { Lossless = kind == 6, Modular = kind == 7 };
        Assert.ThrowsAny<ArgumentException>(() => encoder.EncodeRaw(source, output, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(0, output.Length);
    }

    private static JxlQuantizationMatrix Matrix(JxlDctTransform transform)
    {
        JxlQuantizationMatrix result = JxlQuantizationMatrix.CreateDefault(transform);
        foreach (float[] values in new[] { result.X, result.Y, result.B })
        { for (int i = 0; i < values.Length; i++) { values[i] *= 1.3f + (i % 7) * .07f; } }
        return result;
    }

    private static JxlRawImage Source(int width, int height)
    {
        JxlRawPlane[] planes = new JxlRawPlane[3];
        for (int c = 0; c < 3; c++)
        {
            int[] samples = new int[width * height];
            for (int y = 0; y < height; y++)
            { for (int x = 0; x < width; x++) { samples[y * width + x] = 40 + c * 31 + (x * 3 + y * 5) % 97; } }
            planes[c] = JxlRawPlane.FromIntegers(width, height, samples, 8, c);
        }
        return new(width, height, [new(planes)]);
    }

    private static async Task VerifyNative(MemoryStream encoded)
    {
        Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken));
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-quant-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.jxl"), output = Path.Combine(directory, "output.png");
            await File.WriteAllBytesAsync(input, encoded.ToArray(), TestContext.Current.CancellationToken);
            await JxlNativeReference.RunAsync("djxl", [input, output, "--bits_per_sample=16", "--num_threads=1"], TestContext.Current.CancellationToken);
            encoded.Position = 0; using Image<Rgba64> managed = Image.Load<Rgba64>(encoded), native = Image.Load<Rgba64>(output);
            int peak = 0;
            for (int y = 0; y < managed.Height; y++) { for (int x = 0; x < managed.Width; x++)
            { Rgba64 a = managed[x, y], b = native[x, y]; peak = Math.Max(peak, Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)))); } }
            Assert.True(peak <= 8, $"Custom matrix native pixel peak difference {peak}");
        }
        finally { Directory.Delete(directory, true); }
    }
}
