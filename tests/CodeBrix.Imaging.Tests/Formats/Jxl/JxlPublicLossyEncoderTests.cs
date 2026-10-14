// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Color;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Metadata.Profiles.Icc;
using CodeBrix.Imaging.Metadata.Profiles.Xmp;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlPublicLossyEncoderTests
{
    [Theory]
    [InlineData(19, 13)] [InlineData(263, 259)] [InlineData(2057, 9)]
    public void progressive_ac_preserves_the_final_reconstruction_exactly(int width, int height)
    {
        using Image<Rgba64> source = new(width, height);
        for (int y = 0; y < height; y++)
        { for (int x = 0; x < width; x++) { source[x, y] = new((ushort)(x * 173), (ushort)(y * 233), (ushort)((x + y) * 117), (ushort)((x + y) * 319)); } }
        using MemoryStream baseline = new(), progressive = new();
        new JxlEncoder { Distance = .5f, Effort = 7 }.Encode(source, baseline);
        new JxlEncoder { Distance = .5f, Effort = 7, Progressive = JxlProgressiveMode.Ac }.Encode(source, progressive);
        baseline.Position = progressive.Position = 0;
        using Image<Rgba64> a = Image.Load<Rgba64>(baseline), b = Image.Load<Rgba64>(progressive);
        for (int y = 0; y < height; y++)
        { Assert.True(a.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y).SequenceEqual(b.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y))); }
    }

    [Theory]
    [InlineData(1, false)] [InlineData(7, false)] [InlineData(10, false)]
    [InlineData(1, true)] [InlineData(7, true)] [InlineData(10, true)]
    [InlineData(1, false, true)] [InlineData(7, false, true)] [InlineData(10, false, true)]
    [InlineData(1, true, true)] [InlineData(7, true, true)] [InlineData(10, true, true)]
    public void public_lossy_encoder_accepts_all_pixel_types_and_preserves_alpha(int effort, bool progressive, bool modular = false)
    {
        Verify<L8>(); Verify<L16>(); Verify<La16>(); Verify<La32>(); Verify<Rgb24>(); Verify<Bgr24>(); Verify<Rgb48>(); Verify<Rgba64>();
        Verify<Rgba32>(); Verify<Bgra32>(); Verify<Argb32>(); Verify<Abgr32>(); Verify<Byte4>(); Verify<A8>(); Verify<Bgr565>();
        Verify<Bgra4444>(); Verify<Bgra5551>(); Verify<Rgba1010102>(); Verify<Rg32>(); Verify<Short2>(); Verify<Short4>();
        Verify<NormalizedByte2>(); Verify<NormalizedByte4>(); Verify<NormalizedShort2>(); Verify<NormalizedShort4>();
        Verify<HalfSingle>(); Verify<HalfVector2>(); Verify<HalfVector4>(); Verify<RgbaVector>();

        void Verify<TPixel>() where TPixel : unmanaged, IPixel<TPixel>
        {
            using Image<TPixel> source = new(19, 13); bool half = typeof(TPixel) == typeof(HalfSingle) || typeof(TPixel) == typeof(HalfVector2) || typeof(TPixel) == typeof(HalfVector4);
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    TPixel pixel = default; Vector4 value = new(.1f + x * .03f, .1f + y * .05f, .2f + (x + y) * .02f, ((x + y) % 7) / 6f);
                    if (half) { pixel.FromVector4(value); } else { pixel.FromScaledVector4(value); }
                    source[x, y] = pixel;
                }
            }
            using MemoryStream encoded = new(); new JxlEncoder { Modular = modular, Distance = .01f, Effort = effort, Progressive = progressive ? JxlProgressiveMode.Ac : JxlProgressiveMode.None }.Encode(source, encoded);
            encoded.Position = 0; using Image<RgbaVector> actual = new JxlDecoder().Decode<RgbaVector>(Configuration.Default, encoded, TestContext.Current.CancellationToken);
            double squared = 0;
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    Vector4 expected = half ? source[x, y].ToVector4() : source[x, y].ToScaledVector4();
                    Vector4 error = expected - actual[x, y].ToVector4(); squared += error.X * error.X + error.Y * error.Y + error.Z * error.Z;
                    Assert.Equal(BitConverter.SingleToInt32Bits(expected.W), BitConverter.SingleToInt32Bits(actual[x, y].A));
                }
            }
            double rms = Math.Sqrt(squared / (source.Width * source.Height * 3));
            Assert.True(rms <= .035, $"{typeof(TPixel).Name}, effort {effort}: RGB RMS {rms}");
        }
    }

    [Fact]
    public void quality_overrides_distance_and_lossless_overrides_quality()
    {
        using Image<Rgb24> source = new(37, 31);
        for (int y = 0; y < source.Height; y++)
        { for (int x = 0; x < source.Width; x++) { source[x, y] = new((byte)(x * 7), (byte)(y * 8), (byte)((x + y) * 3)); } }
        byte[] high = Encode(new() { Distance = .01f }), low = Encode(new() { Distance = 3 });
        Assert.Equal(high, Encode(new() { Distance = float.Epsilon }));
        Assert.Equal(high, Encode(new() { Distance = .05f }));
        Assert.True(Error(high) < Error(low)); Assert.True(high.Length > low.Length);
        Assert.Equal(Encode(new() { Distance = 1 }), Encode(new() { Quality = 90, Distance = 25 }));
        byte[] exact = Encode(new() { Lossless = true, Quality = 0 });
        Assert.Equal(exact, Encode(new() { Quality = 100 }));
        Assert.Equal(0, Error(exact));

        byte[] Encode(JxlEncoder encoder) { using MemoryStream output = new(); encoder.Encode(source, output); return output.ToArray(); }
        double Error(byte[] bytes)
        {
            using MemoryStream input = new(bytes); using Image<Rgb24> image = Image.Load<Rgb24>(input); double squared = 0;
            for (int y = 0; y < source.Height; y++)
            { for (int x = 0; x < source.Width; x++) { squared += Vector4.DistanceSquared(source[x, y].ToVector4(), image[x, y].ToVector4()); } }
            return squared;
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void icc_rgb_gray_and_hdr_sources_keep_their_color_encoding_and_animation(int kind)
    {
        JxlColorEncoding color = Color(kind); byte[] icc = JxlIccSynthesis.Create(color);
        if (kind == 2) { Verify<L16>(); } else { Verify<Rgb48>(); }

        void Verify<TPixel>() where TPixel : unmanaged, IPixel<TPixel>
        {
            using Image<TPixel> source = new(17, 11); TPixel pixel = default;
            pixel.FromScaledVector4(new(.35f, .55f, .7f, 1)); source.Frames.CreateFrame(pixel);
            pixel.FromScaledVector4(new(.25f, .45f, .6f, 1));
            for (int y = 0; y < source.Height; y++) { source.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y).Fill(pixel); }
            source.Metadata.IccProfile = new IccProfile(icc); source.Metadata.GetJxlMetadata().IntensityTarget = kind >= 4 ? 1000 : 255;
            source.Metadata.XmpProfile = new XmpProfile("<x:xmpmeta>lossy animation</x:xmpmeta>"u8.ToArray());
            source.Frames[0].Metadata.GetJxlMetadata().Duration = 7; source.Frames[1].Metadata.GetJxlMetadata().Duration = 13;
            using MemoryStream output = new(); new JxlEncoder { Distance = .01f }.Encode(source, output); output.Position = 0;
            using Image<RgbaVector> actual = new JxlDecoder { TargetColor = JxlTargetColor.Original }.Decode<RgbaVector>(Configuration.Default, output, TestContext.Current.CancellationToken);
            Assert.Equal(icc, actual.Metadata.IccProfile.ToByteArray()); Assert.Equal(2, actual.Frames.Count);
            Assert.Equal(source.Metadata.XmpProfile.Data, actual.Metadata.XmpProfile.Data);
            for (int f = 0; f < 2; f++)
            {
                Assert.Equal(source.Frames[f].Metadata.GetJxlMetadata().Duration, actual.Frames[f].Metadata.GetJxlMetadata().Duration);
                Assert.True(Vector4.Distance(source.Frames[f][0, 0].ToScaledVector4(), actual.Frames[f][0, 0].ToVector4()) <= .004,
                    $"ICC kind {kind}, frame {f}: {source.Frames[f][0, 0].ToScaledVector4()} → {actual.Frames[f][0, 0].ToVector4()}");
            }
        }
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(2, false)] [InlineData(3, false)] [InlineData(4, false)] [InlineData(5, false)]
    [InlineData(0, true)] [InlineData(1, true)] [InlineData(2, true)] [InlineData(3, true)] [InlineData(4, true)] [InlineData(5, true)]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_agrees_with_public_rgb_gray_and_hdr_icc_output(int kind, bool progressive)
    {
        JxlExternalResources.RequireEnabled();
        await VerifyNative(kind, progressive);
    }

    [Fact]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_agrees_with_simulated_photon_noise()
    {
        JxlExternalResources.RequireEnabled();
        await VerifyNative(0, true, 1_000_000);
    }

    [Theory]
    [InlineData(.7f)] [InlineData(1)] [InlineData(1.5f)] [InlineData(3)] [InlineData(4)] [InlineData(25)]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_agrees_with_restoration_filtering(float distance)
    {
        JxlExternalResources.RequireEnabled();
        await VerifyNative(0, true, distance: distance);
    }

    [Theory]
    [InlineData(0, 8)] [InlineData(1, 9)] [InlineData(2, 10)]
    [InlineData(3, 8)] [InlineData(4, 9)] [InlineData(5, 10)]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_agrees_after_perceptual_search_for_rgb_gray_and_hdr(int kind, int effort)
    {
        JxlExternalResources.RequireEnabled();
        await VerifyNative(kind, true, distance: 1, effort: effort);
    }

    [Fact]
    public void photon_noise_is_deterministic_and_keeps_raw_integer_alpha_exact()
    {
        using Image<Rgba64> source = new(65, 17, new Rgba64(24000, 33000, 45000, 0));
        for (int y = 0; y < source.Height; y++)
        { for (int x = 0; x < source.Width; x++) { source[x, y] = new(24000, 33000, 45000, (ushort)((x + y) * 331)); } }
        using MemoryStream baseline = new(), noisy = new(), repeated = new();
        new JxlEncoder { Distance = .5f }.Encode(source, baseline);
        JxlEncoder encoder = new() { Distance = .5f, PhotonNoiseIso = 1_000_000, Progressive = JxlProgressiveMode.Ac };
        encoder.Encode(source, noisy); encoder.Encode(source, repeated); Assert.Equal(noisy.ToArray(), repeated.ToArray());
        baseline.Position = noisy.Position = 0;
        using Image<Rgba64> cleanImage = Image.Load<Rgba64>(baseline), noisyImage = Image.Load<Rgba64>(noisy);
        bool changed = false;
        for (int y = 0; y < source.Height; y++)
        { for (int x = 0; x < source.Width; x++) { changed |= cleanImage[x, y] != noisyImage[x, y]; Assert.Equal(source[x, y].A, noisyImage[x, y].A); } }
        Assert.True(changed); noisy.Position = 0;
        using JxlRawImage raw = new JxlDecoder { Coalesce = false }.DecodeRaw(Configuration.Default, noisy, TestContext.Current.CancellationToken);
        for (int y = 0; y < source.Height; y++)
        { for (int x = 0; x < source.Width; x++) { Assert.Equal(source[x, y].A, raw.Frames[0].Planes[3].GetIntegerRowSpan(y)[x]); } }
    }

    [Theory]
    [InlineData(0, 1)] [InlineData(1, 7)] [InlineData(2, 10)]
    [InlineData(3, 1)] [InlineData(4, 7)] [InlineData(5, 10)]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_agrees_with_lossy_modular_gray_rgb_and_hdr(int kind, int effort)
    {
        JxlExternalResources.RequireEnabled();
        await VerifyNative(kind, true, effort: effort, modular: true);
    }

    private static async Task VerifyNative(int kind, bool progressive, float photonNoiseIso = 0, float distance = .5f, int effort = 7, bool modular = false)
    {
        JxlExternalResources.RequireEnabled();
        Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken));
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-public-lossy-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            if (kind == 2) { await Verify<L16>(); } else { await Verify<Rgba64>(); }
        }
        finally { Directory.Delete(directory, true); }

        async Task Verify<TPixel>() where TPixel : unmanaged, IPixel<TPixel>
        {
            using Image<TPixel> source = new(259, 17);
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    TPixel pixel = default; pixel.FromScaledVector4(new(.2f + x / 500f, .3f + y / 50f, .4f, ((x + y) % 7) / 6f)); source[x, y] = pixel;
                }
            }
            source.Metadata.IccProfile = new IccProfile(JxlIccSynthesis.Create(Color(kind)));
            source.Metadata.GetJxlMetadata().IntensityTarget = kind >= 4 ? 1000 : 255;
            string input = Path.Combine(directory, "input.jxl"), output = Path.Combine(directory, "output.png");
            using MemoryStream bytes = new(); new JxlEncoder { Modular = modular, Distance = distance, Effort = effort, Progressive = progressive ? JxlProgressiveMode.Ac : JxlProgressiveMode.None, PhotonNoiseIso = photonNoiseIso }.Encode(source, bytes);
            await File.WriteAllBytesAsync(input, bytes.ToArray(), TestContext.Current.CancellationToken);
            await JxlNativeReference.RunAsync("djxl", [input, output, "--bits_per_sample=16", "--num_threads=1", "--color_space=RGB_D65_SRG_Rel_SRG"], TestContext.Current.CancellationToken);
            using Image<Rgba64> native = Image.Load<Rgba64>(output); bytes.Position = 0;
            using Image<Rgba64> managed = new JxlDecoder { TargetColor = JxlTargetColor.Srgb }.Decode<Rgba64>(Configuration.Default, bytes, TestContext.Current.CancellationToken);
            int peak = 0;
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    Rgba64 a = native[x, y], b = managed[x, y];
                    peak = Math.Max(peak, Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B))));
                    Assert.Equal(a.A, b.A);
                }
            }
            Assert.True(peak <= 8, $"ICC kind {kind}: 16-bit decoder disagreement {peak}");
        }
    }

    private static JxlColorEncoding Color(int kind) => kind switch
    {
        1 => new() { Primaries = 11 }, 2 => new() { ColorSpace = 1 }, 3 => new() { TransferFunction = 8 },
        4 => new() { Primaries = 9, TransferFunction = 16 }, 5 => new() { Primaries = 9, TransferFunction = 18 }, _ => new(),
    };
}
