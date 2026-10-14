// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Formats.Jxl.VarDct;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlVarDctEncoderTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)] [InlineData(12)] [InlineData(13)]
    [InlineData(14)] [InlineData(15)] [InlineData(16)] [InlineData(17)] [InlineData(18)] [InlineData(19)] [InlineData(20)]
    [InlineData(21)] [InlineData(22)] [InlineData(23)] [InlineData(24)] [InlineData(25)] [InlineData(26)]
    public void all_transform_types_encode_xyb_with_bounded_quantization_error(int index)
    {
        JxlTransformType transform = (JxlTransformType)index; (int bw, int bh) = transform.Blocks();
        VerifyRaw(Encode(bw * 8 + 3, bh * 8 + 5, transform, true, .01f), bw * 8 + 3, bh * 8 + 5, true);
        VerifyRaw(Encode(bw * 8 + 3, bh * 8 + 5, transform, true, .01f, true), bw * 8 + 3, bh * 8 + 5, true);
    }

    [Theory]
    [InlineData(1, 1, false)] [InlineData(7, 3, true)] [InlineData(263, 259, true)]
    [InlineData(2057, 9, false)] [InlineData(9, 2057, true)]
    public void tiny_images_and_hf_lf_group_boundaries_preserve_alpha(int width, int height, bool alpha)
    {
        VerifyRaw(Encode(width, height, JxlTransformType.Dct8, alpha, .01f), width, height, alpha);
        VerifyRaw(Encode(width, height, JxlTransformType.Dct8, alpha, .01f, true), width, height, alpha);
    }

    [Theory]
    [InlineData(263, 259)] [InlineData(2057, 9)] [InlineData(9, 2057)]
    public void automatic_transforms_preserve_alpha_and_bound_quantization_error(int width, int height)
    {
        VerifyRaw(Encode(width, height, null, true, .01f, true), width, height, true);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)] [InlineData(12)] [InlineData(13)]
    [InlineData(14)] [InlineData(15)] [InlineData(16)] [InlineData(17)] [InlineData(18)] [InlineData(19)] [InlineData(20)]
    [InlineData(21)] [InlineData(22)] [InlineData(23)] [InlineData(24)] [InlineData(25)] [InlineData(26)]
    [InlineData(-1)] [InlineData(-2)] [InlineData(-3)] [InlineData(-4)] [InlineData(-5)] [InlineData(-6)]
    [Trait("Category", "JxlNativeReference")]
    public async Task pinned_native_decoder_agrees_with_every_transform_and_group_boundary(int index)
    {
        JxlExternalResources.RequireEnabled();
        Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken));
        JxlTransformType? transform = index <= -4 ? null : index < 0 ? JxlTransformType.Dct8 : (JxlTransformType)index;
        (int bw, int bh) = (transform ?? JxlTransformType.Dct8).Blocks();
        int width = index is -1 or -4 ? 263 : index is -2 or -5 ? 2057 : index is -3 or -6 ? 9 : bw * 8 + 3;
        int height = index is -1 or -4 ? 259 : index is -2 or -5 ? 9 : index is -3 or -6 ? 2057 : bh * 8 + 5;
        bool alpha = index % 2 != 0;
        byte[] bytes = Encode(width, height, transform, alpha, progressive: true);
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-vardct-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.jxl"), output = Path.Combine(directory, "output.png");
            await File.WriteAllBytesAsync(input, bytes, TestContext.Current.CancellationToken);
            await JxlNativeReference.RunAsync("djxl", [input, output, "--bits_per_sample=16", "--num_threads=1"], TestContext.Current.CancellationToken);
            using Image<Rgba64> native = Image.Load<Rgba64>(output);
            using MemoryStream stream = new(bytes); using Image<Rgba64> managed = Image.Load<Rgba64>(stream);
            int peak = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Rgba64 a = managed[x, y], b = native[x, y];
                    peak = Math.Max(peak, Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B))));
                    Assert.Equal(alpha ? Alpha(x, y) : ushort.MaxValue, b.A); Assert.Equal(a.A, b.A);
                }
            }
            Assert.True(peak <= 8, $"{transform}, {width}x{height}: 16-bit decoder disagreement {peak}");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static byte[] Encode(int width, int height, JxlTransformType? transform, bool alpha, float distance = .1f, bool progressive = false)
    {
        var allocator = Configuration.Default.MemoryAllocator;
        using JxlFloatFrame xyb = new(allocator, 3, 3);
        using JxlModularWorkspace workspace = new(allocator, (long)width * height);
        JxlModularPlane[] extras = alpha ? [workspace.Allocate(width, height)] : [];
        for (int c = 0; c < 3; c++) { xyb.Planes[c] = allocator.Allocate2D<float>(width, height); }
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector3 sample = Sample(x, y);
                xyb.Planes[0].DangerousGetRowSpan(y)[x] = sample.X;
                xyb.Planes[1].DangerousGetRowSpan(y)[x] = sample.Y;
                xyb.Planes[2].DangerousGetRowSpan(y)[x] = sample.Z;
                if (alpha) { extras[0].Set(x, y, Alpha(x, y)); }
            }
        }
        JxlImageHeader image = new() { Size = new((uint)width, (uint)height), BitDepth = new(16, 0), Modular16BitBuffers = false,
            ExtraChannels = alpha ? [new() { Type = 0, BitDepth = new(16, 0) }] : [] };
        JxlFrameHeader frame = new() { Width = (uint)width, Height = (uint)height, ExtraUpsampling = alpha ? [1] : [], ExtraBlending = alpha ? [default] : [] };
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream); image.Write(writer);
        JxlVarDctEncoder.WriteFrame(writer, image, frame, xyb, extras, distance, 7, allocator, TestContext.Current.CancellationToken, transform, progressive);
        writer.ZeroPadToByte(); return stream.ToArray();
    }

    private static Vector3 Sample(int x, int y) => new JxlXybEncoder(255).FromLinearSrgb(new(
        .1f + .7f * ((x * 7 + y * 3) % 67) / 66, .1f + .7f * ((x * 3 + y * 11) % 71) / 70, .1f + .7f * ((x * 5 + y * 7) % 73) / 72));
    private static ushort Alpha(int x, int y) => (ushort)((x * 1777 + y * 337) & 65535);

    private static void VerifyRaw(byte[] bytes, int width, int height, bool alpha)
    {
        using JxlTestStream input = new(bytes, 3);
        using JxlRawImage raw = new JxlDecoder { Coalesce = false }.DecodeRaw(Configuration.Default, input, TestContext.Current.CancellationToken);
        JxlRawFrame frame = Assert.Single(raw.Frames); Assert.Equal(JxlRawColorSpace.Xyb, frame.ColorSpace);
        Assert.Equal(alpha ? 4 : 3, frame.Planes.Count); Assert.Equal(width, frame.Width); Assert.Equal(height, frame.Height);
        float peak = 0; double squared = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Vector3 expected = Sample(x, y);
                Vector3 actual = new(frame.Planes[0].GetFloatRowSpan(y)[x], frame.Planes[1].GetFloatRowSpan(y)[x], frame.Planes[2].GetFloatRowSpan(y)[x]);
                Vector3 error = Vector3.Abs(expected - actual); peak = Math.Max(peak, Math.Max(error.X, Math.Max(error.Y, error.Z))); squared += error.LengthSquared();
                if (alpha) { Assert.Equal(Alpha(x, y), frame.Planes[3].GetIntegerRowSpan(y)[x]); }
            }
        }
        double rms = Math.Sqrt(squared / (width * height * 3));
        Assert.True(peak <= .02f && rms <= .004, $"{width}x{height}: XYB quantization peak {peak}, RMS {rms}");
    }
}
