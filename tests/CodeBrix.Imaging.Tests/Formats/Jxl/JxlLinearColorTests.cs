// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using CodeBrix.Imaging.ColorSpaces.Icc;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Color;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Metadata.Profiles.Icc;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

// The opt-in libjxl float convention: JxlEncoder.ColorEncoding tags samples as linear,
// JxlTargetColor.LinearSrgb decodes to linear. The defaults are unchanged.
public class JxlLinearColorTests
{
    private static readonly float[] Samples = [0f, .0031308f, .18f, .5f, 1f, 1.5f];

    [Fact]
    public void linear_tagged_float_input_round_trips_through_linear_srgb_output()
    {
        using MemoryStream encoded = Encode(new JxlEncoder { Lossless = true, ColorEncoding = JxlColorEncodingInfo.LinearSrgb });
        using Image<RgbaVector> decoded = Decode(encoded, JxlTargetColor.LinearSrgb);
        for (int x = 0; x < Samples.Length; x++)
        {
            RgbaVector pixel = decoded[x, 0];
            Assert.Equal(Samples[x], pixel.R, 5);
            Assert.Equal(Samples[x], pixel.G, 5);
            Assert.Equal(Samples[x], pixel.B, 5);
            Assert.Equal(1f, pixel.A, 5);
        }
    }

    [Fact]
    public void linear_tagged_file_decodes_to_srgb_encoded_values_for_srgb_output()
    {
        using MemoryStream encoded = Encode(new JxlEncoder { Lossless = true, ColorEncoding = JxlColorEncodingInfo.LinearSrgb });
        using Image<RgbaVector> decoded = Decode(encoded, JxlTargetColor.Srgb);
        for (int x = 0; x < Samples.Length; x++) { Assert.Equal(SrgbEncode(Samples[x]), decoded[x, 0].R, 4); }
    }

    [Fact]
    public void default_encoding_still_tags_float_samples_as_srgb()
    {
        using MemoryStream encoded = Encode(new JxlEncoder { Lossless = true });
        using Image<RgbaVector> asIs = Decode(encoded, JxlTargetColor.Auto);
        using Image<RgbaVector> linear = Decode(encoded, JxlTargetColor.LinearSrgb);
        for (int x = 0; x < Samples.Length; x++)
        {
            Assert.Equal(Samples[x], asIs[x, 0].R, 5);
            Assert.Equal(SrgbDecode(Samples[x]), linear[x, 0].R, 4);
        }
    }

    [Fact]
    public void lossy_linear_tagged_input_decodes_close_to_the_linear_source()
    {
        using MemoryStream encoded = Encode(new JxlEncoder { Distance = .5f, ColorEncoding = JxlColorEncodingInfo.LinearSrgb }, 64);
        using Image<RgbaVector> decoded = Decode(encoded, JxlTargetColor.LinearSrgb);
        // Mid-row samples of the six-band image: well inside each band, away from ringing at the edges.
        for (int band = 0; band < Samples.Length - 1; band++)
        {
            float expected = Math.Min(Samples[band], 1f);
            Assert.InRange(decoded[(band * 64) + 32, 32].G, expected - .02f, expected + .02f);
        }
    }

    [Fact]
    public void linear_srgb_output_carries_a_linear_icc_profile_that_survives_a_re_encode()
    {
        using MemoryStream encoded = Encode(new JxlEncoder { Lossless = true, ColorEncoding = JxlColorEncodingInfo.LinearSrgb });
        using Image<RgbaVector> decoded = Decode(encoded, JxlTargetColor.LinearSrgb);
        Assert.NotNull(decoded.Metadata.IccProfile);
        IccColorProfile profile = IccColorProfile.Read(decoded.Metadata.IccProfile.ToByteArray());
        Assert.Equal(3, profile.Channels);

        using MemoryStream again = new();
        new JxlEncoder { Lossless = true }.Encode(decoded, again);
        again.Position = 0;
        using Image<RgbaVector> srgb = Decode(again, JxlTargetColor.Srgb);
        for (int x = 0; x < Samples.Length; x++) { Assert.Equal(SrgbEncode(Samples[x]), srgb[x, 0].R, 3); }
    }

    [Fact]
    public void linear_srgb_output_has_no_icc_profile_when_metadata_is_ignored()
    {
        using MemoryStream encoded = Encode(new JxlEncoder { Lossless = true, ColorEncoding = JxlColorEncodingInfo.LinearSrgb });
        using Image<RgbaVector> decoded = new JxlDecoder { TargetColor = JxlTargetColor.LinearSrgb, IgnoreMetadata = true }
            .Decode<RgbaVector>(Configuration.Default, encoded, TestContext.Current.CancellationToken);
        Assert.Null(decoded.Metadata.IccProfile);
    }

    [Fact]
    public void an_icc_profile_and_a_color_encoding_together_are_refused()
    {
        using Image<RgbaVector> image = CreateImage(1);
        image.Metadata.IccProfile = new IccProfile(JxlIccSynthesis.Create(new JxlColorEncoding()));
        using MemoryStream output = new();
        Assert.Throws<ArgumentException>(() => new JxlEncoder { Lossless = true, ColorEncoding = JxlColorEncodingInfo.LinearSrgb }.Encode(image, output));
    }

    [Fact]
    public void an_invalid_color_encoding_is_refused()
    {
        using Image<RgbaVector> image = CreateImage(1);
        using MemoryStream output = new();
        Assert.Throws<ArgumentException>(() => new JxlEncoder { ColorEncoding = new JxlColorEncodingInfo { TransferFunction = 99 } }.Encode(image, output));
    }

    [Fact]
    public void the_color_encoding_is_captured_when_encoding_starts()
    {
        JxlColorEncodingInfo encoding = JxlColorEncodingInfo.LinearSrgb;
        JxlEncoder encoder = new() { Lossless = true, ColorEncoding = encoding };
        using MemoryStream first = Encode(encoder);
        encoding.TransferFunction = 13;
        using MemoryStream second = Encode(encoder);
        using Image<RgbaVector> linear = Decode(first, JxlTargetColor.Auto);
        using Image<RgbaVector> srgb = Decode(second, JxlTargetColor.LinearSrgb);
        Assert.Equal(.5f, linear[3, 0].R, 5);
        Assert.Equal(SrgbDecode(.5f), srgb[3, 0].R, 4);
    }

    [Fact]
    public void linear_tagged_grayscale_integer_samples_decode_as_linear_light()
    {
        using Image<L16> image = new(4, 1);
        ushort[] values = [0, 11796, 32768, 65535];
        for (int x = 0; x < values.Length; x++) { image[x, 0] = new L16(values[x]); }
        using MemoryStream encoded = new();
        new JxlEncoder { Lossless = true, ColorEncoding = JxlColorEncodingInfo.LinearSrgb }.Encode(image, encoded);
        using Image<RgbaVector> linear = Decode(encoded, JxlTargetColor.LinearSrgb);
        using Image<RgbaVector> srgb = Decode(encoded, JxlTargetColor.Srgb);
        for (int x = 0; x < values.Length; x++)
        {
            float expected = values[x] / 65535f;
            Assert.Equal(expected, linear[x, 0].R, 4);
            Assert.Equal(expected, linear[x, 0].B, 4);
            Assert.Equal(SrgbEncode(expected), srgb[x, 0].R, 3);
        }
    }

    [Fact]
    public void presets_describe_srgb_and_linear_srgb()
    {
        Assert.Equal(13, JxlColorEncodingInfo.Srgb.TransferFunction);
        Assert.Equal(8, JxlColorEncodingInfo.LinearSrgb.TransferFunction);
        Assert.Equal(1, JxlColorEncodingInfo.LinearSrgb.Primaries);
        Assert.Equal(1, JxlColorEncodingInfo.LinearSrgb.WhitePoint);
        Assert.NotSame(JxlColorEncodingInfo.LinearSrgb, JxlColorEncodingInfo.LinearSrgb);
    }

    private static MemoryStream Encode(JxlEncoder encoder, int bandWidth = 1)
    {
        using Image<RgbaVector> image = CreateImage(bandWidth);
        MemoryStream output = new();
        encoder.Encode(image, output);
        output.Position = 0;
        return output;
    }

    private static Image<RgbaVector> Decode(Stream stream, JxlTargetColor target)
    {
        stream.Position = 0;
        return new JxlDecoder { TargetColor = target }.Decode<RgbaVector>(Configuration.Default, stream, TestContext.Current.CancellationToken);
    }

    // One grey band per sample, bandWidth pixels wide and bandWidth (at least one) rows tall.
    private static Image<RgbaVector> CreateImage(int bandWidth)
    {
        Image<RgbaVector> image = new(Samples.Length * bandWidth, bandWidth);
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                float value = Samples[x / bandWidth];
                image[x, y] = new RgbaVector(value, value, value, 1f);
            }
        }

        return image;
    }

    private static float SrgbEncode(float linear) => linear <= .0031308f
        ? linear * 12.92f
        : (1.055f * MathF.Pow(linear, 1 / 2.4f)) - .055f;

    private static float SrgbDecode(float encoded) => encoded <= .04045f
        ? encoded / 12.92f
        : MathF.Pow((encoded + .055f) / 1.055f, 2.4f);
}
