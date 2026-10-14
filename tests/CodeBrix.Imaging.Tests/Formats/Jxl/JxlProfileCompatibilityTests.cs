// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jpeg;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Color;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlProfileCompatibilityTests
{
    [Theory]
    [InlineData(0, true)] [InlineData(1, true)] [InlineData(2, true)]
    [InlineData(3, true)] [InlineData(4, true)] [InlineData(5, true)]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(2, false)]
    [InlineData(3, false)] [InlineData(4, false)] [InlineData(5, false)]
    public void gray_profiles_accept_neutral_rgb_buffers_and_all_frames(int kind, bool lossless)
    {
        switch (kind)
        {
            case 0: Verify<Rgb24>(); break;
            case 1: Verify<Rgba32>(); break;
            case 2: Verify<Rgb48>(); break;
            case 3: Verify<Rgba64>(); break;
            case 4: Verify<RgbaVector>(); break;
            default: Verify<HalfVector4>(); break;
        }
        void Verify<TPixel>() where TPixel : unmanaged, IPixel<TPixel>
        {
            using Image<TPixel> source = new(19, 3); source.Frames.CreateFrame();
            byte[] profile = GrayProfile(); source.Metadata.IccProfile = new(profile);
            for (int f = 0; f < 2; f++)
            for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
            {
                float v = (x + y + f) / 24f; TPixel pixel = default;
                if (kind == 5) { pixel.FromVector4(new(v, v, v, .75f)); }
                else { pixel.FromScaledVector4(new(v, v, v, .75f)); }
                source.Frames[f][x, y] = pixel;
            }
            using MemoryStream encoded = new();
            new JxlEncoder { Lossless = lossless, Distance = .001f, Effort = 2 }.Encode(source, encoded);
            encoded.Position = 0;
            using Image<TPixel> actual = new JxlDecoder { TargetColor = JxlTargetColor.Original }
                .Decode<TPixel>(Configuration.Default, encoded, TestContext.Current.CancellationToken);
            Assert.Equal(profile, actual.Metadata.IccProfile.ToByteArray()); Assert.Equal(2, actual.Frames.Count);
            for (int f = 0; f < 2; f++)
            for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
            {
                if (lossless) { Assert.Equal(source.Frames[f][x, y], actual.Frames[f][x, y]); }
                else { Assert.InRange(Vector4.Distance(source.Frames[f][x, y].ToVector4(), actual.Frames[f][x, y].ToVector4()), 0, .009f); }
            }
            encoded.Position = 0;
            using JxlRawImage raw = new JxlDecoder { TargetColor = JxlTargetColor.Original }.DecodeRaw(Configuration.Default, encoded, TestContext.Current.CancellationToken);
            Assert.Equal(JxlRawColorSpace.Grayscale, raw.Frames[0].ColorSpace);
            Assert.Equal(profile, source.Metadata.IccProfile.ToByteArray());
        }
    }

    [Fact]
    public void loaded_gray_jpeg_can_be_saved_as_jxl()
    {
        using Image<L8> source = new(11, 9, new L8(103)); source.Metadata.IccProfile = new(GrayProfile());
        using MemoryStream original = new();
        new JpegEncoder { ColorType = JpegColorType.Luminance }.Encode(source, original);
        original.Position = 0; using Image<Rgba32> loaded = Image.Load<Rgba32>(original);
        using MemoryStream encoded = new(); new JxlEncoder { Lossless = true, Effort = 1 }.Encode(loaded, encoded);
        encoded.Position = 0; using Image<Rgba32> decoded = Image.Load<Rgba32>(encoded);
        Assert.Equal(loaded.Metadata.IccProfile.ToByteArray(), decoded.Metadata.IccProfile.ToByteArray());
        for (int y = 0; y < loaded.Height; y++)
        for (int x = 0; x < loaded.Width; x++) { Assert.Equal(loaded[x, y], decoded[x, y]); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void colored_samples_in_later_frames_reject_before_output(bool lossless)
    {
        using Image<Rgba32> source = new(1027, 2, new Rgba32(64, 64, 64)); source.Frames.CreateFrame();
        source.Metadata.IccProfile = new(GrayProfile()); source.Frames[1][1026, 1] = new(255, 0, 0);
        using MemoryStream encoded = new();
        Assert.Throws<ArgumentException>(() => new JxlEncoder { Lossless = lossless }.Encode(source, encoded));
        Assert.Equal(0, encoded.Length);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void float_gray_profile_checks_signed_zero_bits(bool half)
    {
        using MemoryStream output = new();
        if (half)
        {
            using Image<HalfVector4> source = new(1, 1, new HalfVector4 { PackedValue = 0x3c00000000008000UL });
            source.Metadata.IccProfile = new(GrayProfile());
            Assert.Throws<ArgumentException>(() => new JxlEncoder { Lossless = true }.Encode(source, output));
        }
        else
        {
            using Image<RgbaVector> source = new(1, 1, new RgbaVector(-0f, 0, 0, 1)); source.Metadata.IccProfile = new(GrayProfile());
            Assert.Throws<ArgumentException>(() => new JxlEncoder { Lossless = true }.Encode(source, output));
        }
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void gray_half_samples_preserve_nan_payloads_and_signed_zero()
    {
        ushort[] samples = [0, 0x8000, 0x7c00, 0xfc00, 0x7c01, 0x7e51, 0xfe73, 0x3c00];
        using Image<HalfVector4> source = new(samples.Length, 1); source.Metadata.IccProfile = new(GrayProfile());
        for (int x = 0; x < samples.Length; x++)
        {
            ulong value = samples[x]; source[x, 0] = new() { PackedValue = value | value << 16 | value << 32 | 0x3c00000000000000UL };
        }
        using MemoryStream stream = new(); new JxlEncoder { Lossless = true, Effort = 2 }.Encode(source, stream);
        stream.Position = 0; using Image<HalfVector4> actual = Image.Load<HalfVector4>(stream);
        for (int x = 0; x < samples.Length; x++) { Assert.Equal(source[x, 0].PackedValue, actual[x, 0].PackedValue); }
    }

    [Fact]
    public void explicit_transparent_color_clearing_allows_hidden_non_gray_samples()
    {
        using Image<Rgba32> source = new(1, 1, new Rgba32(255, 0, 100, 0)); source.Metadata.IccProfile = new(GrayProfile());
        using MemoryStream stream = new();
        Assert.Throws<ArgumentException>(() => new JxlEncoder { Lossless = true }.Encode(source, stream));
        new JxlEncoder { Lossless = true, TransparentColorMode = JxlTransparentColorMode.Clear }.Encode(source, stream);
        stream.Position = 0; using Image<Rgba32> actual = Image.Load<Rgba32>(stream); Assert.Equal(default, actual[0, 0]);
        Assert.Equal(new Rgba32(255, 0, 100, 0), source[0, 0]);
    }

    [Fact]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_preserves_gray_profile_in_rgba_input()
    {
        JxlExternalResources.RequireEnabled();
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-gray-profile-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            using Image<Rgba64> source = new(31, 9);
            byte[] profile = GrayProfile(); source.Metadata.IccProfile = new(profile);
            for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
            { ushort value = (ushort)((x * 1933 + y * 827) & 65535); source[x, y] = new(value, value, value, (ushort)(x * 2000)); }
            string input = Path.Combine(directory, "gray.jxl"), output = Path.Combine(directory, "gray.png"), icc = Path.Combine(directory, "gray.icc");
            await using (FileStream stream = File.Create(input))
            { await new JxlEncoder { Lossless = true, Effort = 2 }.EncodeAsync(source, stream, TestContext.Current.CancellationToken); }
            await JxlNativeReference.RunAsync("djxl", [input, output, "--bits_per_sample=16", "--orig_icc_out=" + icc, "--num_threads=1"], TestContext.Current.CancellationToken);
            Assert.Equal(profile, await File.ReadAllBytesAsync(icc, TestContext.Current.CancellationToken));
            using Image<Rgba64> actual = Image.Load<Rgba64>(output);
            for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++) { Assert.Equal(source[x, y], actual[x, y]); }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static byte[] GrayProfile() => JxlIccSynthesis.Create(new() { ColorSpace = 1 });
}
