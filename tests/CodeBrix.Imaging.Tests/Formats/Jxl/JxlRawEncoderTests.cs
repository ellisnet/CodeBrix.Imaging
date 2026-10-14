// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlRawEncoderTests
{
    [Theory]
    [InlineData(1, false)] [InlineData(7, false)] [InlineData(10, true)]
    public void every_extra_channel_role_and_31_bit_color_samples_survive_lossless_encoding(int effort, bool progressive)
    {
        const int width = 263, height = 19;
        List<JxlRawPlane> planes = [];
        for (int c = 0; c < 3; c++) { planes.Add(Integer(width, height, c, 31)); }
        foreach (JxlExtraChannelType type in Enum.GetValues<JxlExtraChannelType>())
        {
            int[] values = new int[width * height]; for (int i = 0; i < values.Length; i++) { values[i] = (i * 271) & 65535; }
            planes.Add(JxlRawPlane.FromIntegers(width, height, values, 16, extraChannel: new() { Type = type, Name = type + " Ω", AlphaAssociated = type == JxlExtraChannelType.Alpha, SpotColor = new(.25f, .5f, .75f, 1), ColorFilterArrayChannel = 274 }));
        }
        using JxlRawImage source = new(width, height, [new(planes)]);
        using MemoryStream encoded = Encode(source, new() { Lossless = true, Effort = effort, Progressive = progressive ? JxlProgressiveMode.Ac : JxlProgressiveMode.None });
        using JxlRawImage actual = Decode(encoded);
        Assert.Equal(planes.Count, actual.Frames[0].Planes.Count);
        for (int c = 0; c < planes.Count; c++)
        {
            JxlRawPlane a = planes[c], b = actual.Frames[0].Planes[c];
            Assert.Equal(a.ExtraChannelType, b.ExtraChannelType); Assert.Equal(a.Name, b.Name);
            Assert.Equal(a.AlphaAssociated, b.AlphaAssociated);
            if (a.ExtraChannelType == JxlExtraChannelType.SpotColor) { Assert.Equal(a.SpotColor, b.SpotColor); }
            if (a.ExtraChannelType == JxlExtraChannelType.ColorFilterArray) { Assert.Equal(a.ColorFilterArrayChannel, b.ColorFilterArrayChannel); }
            for (int y = 0; y < height; y++) { Assert.True(a.GetIntegerRowSpan(y).SequenceEqual(b.GetIntegerRowSpan(y))); }
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void lossless_float_planes_preserve_signed_zero_infinities_and_nan_payloads(bool progressive)
    {
        int[] bits = [0, int.MinValue, 0x7F800000, unchecked((int)0xFF800000), 0x7FC12345, unchecked((int)0xFFC54321), 1, int.MinValue + 1, 0x3F123456];
        float[] values = Array.ConvertAll(bits, BitConverter.Int32BitsToSingle);
        JxlRawPlane[] planes = new JxlRawPlane[4];
        for (int c = 0; c < 4; c++) { planes[c] = JxlRawPlane.FromFloats(9, 1, values, c == 3 ? 0 : c, c == 3 ? new() : null); }
        using JxlRawImage source = new(9, 1, [new(planes)]);
        using MemoryStream encoded = Encode(source, new() { Lossless = true, Progressive = progressive ? JxlProgressiveMode.Ac : JxlProgressiveMode.None });
        using JxlRawImage actual = Decode(encoded);
        foreach (JxlRawPlane plane in actual.Frames[0].Planes)
        { for (int x = 0; x < 9; x++) { Assert.Equal(bits[x], BitConverter.SingleToInt32Bits(plane.GetFloatRowSpan(0)[x])); } }
    }

    [Theory]
    [InlineData(1)] [InlineData(3)] [InlineData(6)]
    [InlineData(1, false, false)] [InlineData(3, false, false)] [InlineData(6, false, false)]
    [InlineData(1, false, true)] [InlineData(3, false, true)] [InlineData(6, false, true)]
    public void extra_channel_sampling_shifts_keep_full_canvas_and_constant_values(int shift, bool lossless = true, bool modular = false)
    {
        const int width = 2057, height = 19; int scale = 1 << shift, w = (width + scale - 1) / scale, h = (height + scale - 1) / scale;
        int[] values = new int[w * h]; Array.Fill(values, 12345);
        using JxlRawImage source = new(width, height, [new([Integer(width, height, 0, 16),
            JxlRawPlane.FromIntegers(w, h, values, 16, extraChannel: new() { Type = JxlExtraChannelType.Depth, DimensionShift = shift })], JxlRawColorSpace.Grayscale)]);
        using MemoryStream encoded = Encode(source, new() { Lossless = lossless, Modular = lossless || modular, Effort = 1, Progressive = JxlProgressiveMode.Ac }); using JxlRawImage decoded = Decode(encoded);
        JxlRawPlane depth = decoded.Frames[0].Planes[^1]; Assert.Equal(width, depth.Width); Assert.Equal(height, depth.Height); Assert.Equal(0, depth.DimensionShift);
        for (int y = 0; y < height; y++) { foreach (float sample in depth.GetFloatRowSpan(y)) { Assert.Equal(12345f / 65535, sample, .000001f); } }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void raw_lossy_encoding_keeps_non_color_channels_exact(bool modular)
    {
        const int width = 31, height = 19; List<JxlRawPlane> planes = [];
        for (int c = 0; c < 3; c++) { planes.Add(Integer(width, height, c, 16)); }
        int[] samples = new int[width * height]; for (int i = 0; i < samples.Length; i++) { samples[i] = int.MaxValue - i * 173; }
        planes.Add(JxlRawPlane.FromIntegers(width, height, samples, 31, extraChannel: new() { Type = JxlExtraChannelType.Thermal }));
        using JxlRawImage source = new(width, height, [new(planes)]);
        using MemoryStream encoded = Encode(source, new() { Modular = modular, Effort = 8, Progressive = JxlProgressiveMode.Ac });
        using JxlRawImage decoded = Decode(encoded);
        JxlRawPlane extra = decoded.Frames[0].Planes[3];
        for (int y = 0; y < height; y++)
        {
            Assert.True(samples.AsSpan(y * width, width).SequenceEqual(extra.GetIntegerRowSpan(y)));
        }
    }

    [Fact]
    public void saved_reference_and_cropped_add_layer_render_the_expected_canvas()
    {
        using JxlRawImage source = Layers(); using MemoryStream encoded = Encode(source, new() { Lossless = true });
        using Image<Rgb24> actual = Image.Load<Rgb24>(encoded);
        Assert.Equal(17, actual.Width); Assert.Equal(13, actual.Height);
        for (int y = 0; y < 13; y++)
        { for (int x = 0; x < 17; x++) { bool inside = x >= 3 && x < 10 && y >= 2 && y < 7; Assert.Equal(new Rgb24((byte)(inside ? 70 : 50), (byte)(inside ? 90 : 60), (byte)(inside ? 110 : 70)), actual[x, y]); } }
        encoded.Position = 0; using JxlRawImage layers = Decode(encoded);
        Assert.Equal(2, layers.Frames.Count); Assert.True(layers.Frames[0].Metadata.IsReferenceOnly);
        Assert.Equal(2, layers.Frames[0].Metadata.SaveAsReference); Assert.Equal(3, layers.Frames[1].Metadata.CropLeft);
    }

    [Fact]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_agrees_with_raw_layer_blending()
    {
        JxlExternalResources.RequireEnabled();
        using JxlRawImage source = Layers(); using MemoryStream encoded = Encode(source, new() { Lossless = true });
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-raw-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.jxl"), output = Path.Combine(directory, "output.png");
            await File.WriteAllBytesAsync(input, encoded.ToArray(), TestContext.Current.CancellationToken);
            await JxlNativeReference.RunAsync("djxl", [input, output, "--num_threads=1"], TestContext.Current.CancellationToken);
            using Image<Rgb24> managed = Image.Load<Rgb24>(encoded), native = Image.Load<Rgb24>(output);
            for (int y = 0; y < managed.Height; y++) { Assert.True(managed.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y).SequenceEqual(native.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y))); }
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void cancellation_and_invalid_channel_descriptions_leave_output_empty()
    {
        using JxlRawImage source = new(3, 2, [new([Integer(3, 2, 0, 8)], JxlRawColorSpace.Grayscale)]); using MemoryStream output = new();
        using CancellationTokenSource cancellation = new(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new JxlEncoder().EncodeRaw(source, output, cancellationToken: cancellation.Token));
        Assert.Equal(0, output.Length);
        Assert.Throws<ArgumentException>(() => JxlRawPlane.FromIntegers(1, 1, [0], 8, extraChannel: new() { DimensionShift = 7 }));
        Assert.Throws<ArgumentException>(() => JxlRawPlane.FromFloats(2, 1, [0]));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void unrepresentable_reference_metadata_is_rejected_before_output(int kind)
    {
        JxlRawFrame first = Solid(kind == 1 ? 7 : 17, 13, 50, new()
        {
            IsReferenceOnly = kind == 0, SaveAsReference = 1,
            CropLeft = kind == 0 ? 2 : 0, SaveBeforeColorTransform = kind != 0,
        });
        using JxlRawImage source = new(17, 13, kind == 2 ? [first] : [first, Solid(17, 13, 70, new())]);
        using MemoryStream output = new();
        Assert.Throws<ArgumentException>(() => new JxlEncoder().EncodeRaw(source, output, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(0, output.Length);
    }

    [Theory]
    [InlineData(true, true)] [InlineData(false, false)] [InlineData(false, true)]
    public void level_ten_accepts_256_extra_channels(bool lossless, bool modular)
    {
        List<JxlRawPlane> planes = [Integer(3, 2, 0, 8), Integer(3, 2, 1, 8), Integer(3, 2, 2, 8)];
        for (int c = 0; c < 256; c++) { planes.Add(JxlRawPlane.FromIntegers(3, 2, [c, c, c, c, c, c], 8, extraChannel: new() { Type = JxlExtraChannelType.Optional })); }
        using JxlRawImage source = new(3, 2, [new(planes)]);
        using MemoryStream stream = Encode(source, new() { Lossless = lossless, Modular = modular, Effort = 1, Progressive = JxlProgressiveMode.Ac });
        using JxlRawImage decoded = Decode(stream); Assert.Equal(259, decoded.Frames[0].Planes.Count);
        for (int c = 0; c < 256; c++) { Assert.Equal(c, decoded.Frames[0].Planes[3 + c].GetIntegerRowSpan(1)[2]); }
    }

    private static JxlRawImage Layers()
    {
        JxlRawFrame background = Solid(17, 13, 50, new() { IsReferenceOnly = true, SaveAsReference = 2 });
        JxlRawFrame layer = Solid(7, 5, 20, new() { CropLeft = 3, CropTop = 2, Blending = new(JxlBlendMode.Add, 0, false, 2) });
        return new(17, 13, [background, layer]);
    }

    private static JxlRawFrame Solid(int width, int height, int value, JxlFrameMetadata metadata)
    {
        JxlRawPlane[] planes = new JxlRawPlane[3];
        for (int c = 0; c < 3; c++) { int[] samples = new int[width * height]; Array.Fill(samples, value + c * 10); planes[c] = JxlRawPlane.FromIntegers(width, height, samples, 8, c); }
        return new(planes, metadata: metadata);
    }

    private static JxlRawPlane Integer(int width, int height, int channel, int bits)
    {
        int[] samples = new int[width * height]; long mask = (1L << bits) - 1;
        for (int i = 0; i < samples.Length; i++) { samples[i] = (int)((i * 98765431L + channel * 1234567L) & mask); }
        return JxlRawPlane.FromIntegers(width, height, samples, bits, channel);
    }

    private static MemoryStream Encode(JxlRawImage image, JxlEncoder encoder)
    { MemoryStream output = new(); try { encoder.EncodeRaw(image, output); output.Position = 0; return output; } catch { output.Dispose(); throw; } }
    private static JxlRawImage Decode(Stream stream) => new JxlDecoder { Coalesce = false, TargetColor = JxlTargetColor.Original }.DecodeRaw(Configuration.Default, stream, TestContext.Current.CancellationToken);
}
