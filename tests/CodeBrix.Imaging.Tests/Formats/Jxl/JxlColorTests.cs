// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using CodeBrix.Imaging.ColorSpaces.Icc;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Color;
using CodeBrix.Imaging.Formats.Jxl.Container;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlColorTests
{
    [Theory]
    [InlineData("grayscale", false)]
    [InlineData("patches", false)]
    [InlineData("bench_oriented_brg", false)]
    [InlineData("cmyk_layers", false)]
    [InlineData("cmyk_layers", true)]
    [InlineData("pq", false)]
    [InlineData("pq", true)]
    [InlineData("hlg", false)]
    [InlineData("hlg", true)]
    public void icc_conversion_matches_pinned_skcms_samples(string name, bool reverse)
    {
        byte[] icc = ReadOriginalIcc(name);
        IccColorProfile profile = IccColorProfile.Read(icc);
        string root = Path.Combine(Path.GetDirectoryName(JxlHeaderTests.ResourceRoot), "JxlSynthetic", "Color");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")));
        JsonElement hashes = manifest.RootElement.GetProperty("sha256");
        Assert.Equal(hashes.GetProperty(name + ".original.icc").GetString(), Convert.ToHexStringLower(SHA256.HashData(icc)));
        float[] input = ReadSamples(root, name + ".input.f32", hashes);
        float[] expected = ReadSamples(root, name + (reverse ? ".reverse.f32" : ".forward.f32"), hashes);
        Assert.Equal(input.Length, expected.Length);
        if (reverse) { profile.ValidateDestination(); }
        Span<float> result = stackalloc float[4];
        double squared = 0;
        float peak = 0;
        int count = 0;
        for (int i = 0; i < input.Length; i += 4)
        {
            Vector3 xyz = reverse ? IccColorProfile.Srgb.ToXyz(input.AsSpan(i, 4)) : profile.ToXyz(input.AsSpan(i, 4), true);
            if (reverse) { profile.FromXyz(xyz, result); }
            else { IccColorProfile.Srgb.FromXyz(xyz, result); }
            if (profile.HdrTransfer != 0 || (!reverse && profile.ToPcs != null))
            {
                // LUT stages and PQ's nested powers amplify skcms fast-math
                // error. Compare physical PCS coordinates, avoiding cancellation
                // near a gamut boundary. HDR luminances are also checked exactly.
                IccColorProfile outputProfile = reverse ? profile : IccColorProfile.Srgb;
                Vector3 actualXyz = outputProfile.ToXyz(result), expectedXyz = outputProfile.ToXyz(expected.AsSpan(i, 4));
                // This fixture's A2B uses mft2 Lab. skcms treats its FF00
                // endpoint as FFFF. Keep the native CLUT reference, correcting
                // only that known PCS encoding difference (ICC.1:2022 10.10).
                if (name == "cmyk_layers" && !reverse) { expectedXyz = CorrectLegacyLabPcs(expectedXyz); }
                float scale = Math.Max(1, Math.Max(Math.Abs(expectedXyz.X), Math.Max(Math.Abs(expectedXyz.Y), Math.Abs(expectedXyz.Z))));
                Vector3 difference = Vector3.Abs(actualXyz - expectedXyz) / scale;
                peak = Math.Max(peak, Math.Max(difference.X, Math.Max(difference.Y, difference.Z)));
                squared += difference.LengthSquared(); count += 3;
                continue;
            }
            for (int c = 0; c < (reverse && profile.Cmyk ? 4 : 3); c++)
            {
                float actual = result[reverse && profile.Channels == 1 ? 0 : c];
                if (reverse && profile.Cmyk) { actual = 1 - actual; }
                float difference = Math.Abs(actual - expected[i + c]);
                Assert.True(float.IsFinite(difference));
                peak = Math.Max(peak, difference); squared += difference * difference; count++;
            }
        }
        double rms = Math.Sqrt(squared / count);
        // skcms uses approximate power functions; managed transfer functions use MathF.
        double peakLimit = profile.HdrTransfer == 16 ? .01 : reverse ? 5e-4 : 2e-4;
        double rmsLimit = profile.HdrTransfer == 16 ? .0035 : reverse ? 1e-4 : 6e-5;
        Assert.True(peak <= peakLimit && rms <= rmsLimit, $"{name}, reverse={reverse}: peak={peak}, RMS={rms}");
    }

    private static Vector3 CorrectLegacyLabPcs(Vector3 xyz)
    {
        static double LabRoot(double t) => t > .008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116;
        static double LabCube(double t) => t * t * t > .008856 ? t * t * t : (t - 16.0 / 116) / 7.787;
        const double scale = 65535.0 / 65280;
        double x = LabRoot(xyz.X / .9642), y = LabRoot(xyz.Y), z = LabRoot(xyz.Z / .8249);
        double l = Math.Clamp(((116 * y - 16) / 100) * scale, 0, 1);
        double a = Math.Clamp(((500 * (x - y) + 128) / 255) * scale, 0, 1);
        double b = Math.Clamp(((200 * (y - z) + 128) / 255) * scale, 0, 1);
        y = (100 * l + 16) / 116; x = y + (255 * a - 128) / 500; z = y - (255 * b - 128) / 200;
        return new((float)(.9642 * LabCube(x)), (float)LabCube(y), (float)(.8249 * LabCube(z)));
    }

    [Theory]
    [InlineData("lut8", false)] [InlineData("lut8", true)]
    [InlineData("lut16", false)] [InlineData("lut16", true)]
    public void nonlinear_lut_fixtures_follow_icc_pcs_encoding(string name, bool reverse)
    {
        // The pinned skcms fixtures fold XYZ scaling into the matrix, on the
        // wrong side of nonlinear B curves. Retain the same profiles and input
        // corpus, but check their defining polynomial (GenerateCmsFixtures.cs)
        // against ICC.1:2022's PCS encoding. JxlIccLutTests also uses exact,
        // hand-calculated examples with and without each optional stage.
        byte[] icc = ReadOriginalIcc(name);
        string root = Path.Combine(Path.GetDirectoryName(JxlHeaderTests.ResourceRoot), "JxlSynthetic", "Color");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")));
        JsonElement hashes = manifest.RootElement.GetProperty("sha256");
        Assert.Equal(hashes.GetProperty(name + ".original.icc").GetString(), Convert.ToHexStringLower(SHA256.HashData(icc)));
        float[] samples = ReadSamples(root, name + ".input.f32", hashes);
        IccColorProfile profile = IccColorProfile.Read(icc);
        Span<float> actual = stackalloc float[3];
        double peak = 0;
        for (int i = 0; i < samples.Length; i += 4)
        {
            Vector3 input = new(samples[i], samples[i + 1], samples[i + 2]);
            if (reverse) { input = IccColorProfile.Srgb.ToXyz(samples.AsSpan(i, 3)); profile.FromXyz(input, actual); }
            else
            {
                Vector3 xyz = profile.ToXyz(samples.AsSpan(i, 3));
                actual[0] = xyz.X; actual[1] = xyz.Y; actual[2] = xyz.Z;
            }
            double[] expected = LookupPolynomial(input, reverse);
            for (int c = 0; c < 3; c++) { peak = Math.Max(peak, Math.Abs(expected[c] - actual[c])); }
        }
        // The polynomial describes the unquantized CLUT; only its tabulated
        // nodes are rounded to 8/16-bit precision in these fixture profiles.
        Assert.InRange(peak, 0, name == "lut8" ? .003 : .00003);
    }

    private static double[] LookupPolynomial(Vector3 input, bool reverse)
    {
        const double scale = 65535.0 / 32768;
        double r = input.X, g = input.Y, b = input.Z;
        if (reverse) { r /= scale; g /= scale; b /= scale; }
        double exponent = reverse ? 1.25 : 1.5;
        r = Math.Pow(r, exponent); g = Math.Pow(g, exponent); b = Math.Pow(b, exponent);
        if (!reverse) { Grid(ref r, ref g, ref b); }
        if (!reverse) { r = Math.Pow(r, .75); g = Math.Pow(g, .75); b = Math.Pow(b, .75); }
        // Exact signed 15.16 matrix coefficients from the fixture generator.
        double x = (19661 * r + 2621 * g + 655 * b + 655) / 65536;
        double y = (1311 * r + 22282 * g + 1311 * b + 1311) / 65536;
        double z = (655 * r + 2621 * g + 19661 * b + 1966) / 65536;
        if (!reverse) { return [Math.Pow(x, 1.25) * scale, Math.Pow(y, 1.25) * scale, Math.Pow(z, 1.25) * scale]; }
        x = Math.Pow(x, .75); y = Math.Pow(y, .75); z = Math.Pow(z, .75);
        Grid(ref x, ref y, ref z);
        return [Math.Pow(x, 1.5), Math.Pow(y, 1.5), Math.Pow(z, 1.5)];

        static void Grid(ref double red, ref double green, ref double blue)
        {
            double x = .6 * red + .2 * green + .1 * blue * green;
            double y = .1 * red + .7 * blue;
            double z = .4 * green + .3 * blue + .15 * red * blue;
            red = x; green = y; blue = z;
        }
    }

    // skcms fits sampled destination curves to a parametric approximation.
    // Verify exact table inversion independently, including endpoints and each
    // profile's channel matrix, rather than requiring the fitted curve's error.
    [Theory]
    [InlineData("grayscale")]
    [InlineData("patches")]
    [InlineData("bench_oriented_brg")]
    public void sampled_destination_curves_preserve_source_coordinates(string name)
    {
        IccColorProfile profile = IccColorProfile.Read(ReadOriginalIcc(name));
        profile.ValidateDestination();
        Span<float> source = stackalloc float[4], result = stackalloc float[4];
        for (int i = 0; i <= 256; i++)
        {
            source[0] = i / 256f; source[1] = 1 - source[0]; source[2] = .5f;
            profile.FromXyz(profile.ToXyz(source), result);
            for (int c = 0; c < profile.Channels; c++)
            { Assert.InRange(Math.Abs(result[c] - source[c]), 0, 5e-6f); }
        }
    }

    [Theory]
    [InlineData("grayscale")]
    [InlineData("patches")]
    [InlineData("cmyk_layers")]
    public void icc_tag_ranges_are_checked_before_parsing(string name)
    {
        byte[] profile = ReadOriginalIcc(name);
        BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(136), uint.MaxValue);
        Assert.Throws<InvalidImageContentException>(() => IccColorProfile.Read(profile));
    }

    [Fact]
    public void inverse_curve_rejects_nonmonotone_tables()
    {
        IccCurve curve = IccCurve.Table(new byte[] { 0, 255, 128 }, 3, 1);
        Assert.Throws<InvalidImageContentException>(curve.ValidateInverse);
    }

    [Fact]
    public void icc_hdr_uses_absolute_reference_luminance()
    {
        // 100-nit gray in PQ; the ICC CICP reference white is 203 nits.
        Assert.InRange(Math.Abs(IccHdrTransfer.Decode(.5080784f, 16) - (100f / 203)), 0, 1e-6f);
        Assert.InRange(Math.Abs(IccHdrTransfer.Encode(100f / 203, 16) - .5080784f), 0, 1e-7f);
        Assert.InRange(Math.Abs(IccHdrTransfer.Decode(1, 16) - (10000f / 203)), 0, 1e-5f);
        Span<float> input = stackalloc float[3], output = stackalloc float[3];
        foreach (byte transfer in new byte[] { 16, 18 })
        {
            IccColorProfile profile = IccColorProfile.Read(JxlIccSynthesis.Create(new() { Primaries = 9, TransferFunction = transfer }));
            for (int i = 1; i <= 64; i++)
            {
                input.Fill(i / 64f);
                profile.FromXyz(profile.ToXyz(input), output);
                for (int c = 0; c < 3; c++) { Assert.InRange(Math.Abs(input[c] - output[c]), 0, 2e-6f); }
            }
        }
    }

    [Fact]
    public void parametric_curve_retains_negative_offsets()
    {
        IccCurve curve = new() { A = 1, G = 1, C = 1, D = .5f, E = -.1f, F = -.1f };
        Assert.Equal(-.1f, curve.Evaluate(0));
        Assert.InRange(Math.Abs(curve.Evaluate(.25f) - .15f), 0, 1e-7f);
    }

    [Theory]
    [InlineData(1U)]
    [InlineData(8U)]
    [InlineData(13U)]
    [InlineData(16U)]
    [InlineData(17U)]
    [InlineData(18U)]
    public void enumerated_transfer_functions_roundtrip_reference_points(uint transfer)
    {
        JxlEnumeratedColorSpace color = new(new() { TransferFunction = transfer }, 1000);
        foreach (float value in new[] { .001f, .01f, .18f, .5f, 1f, 1.5f })
        { Assert.InRange(Math.Abs(color.DecodeTransfer(color.EncodeTransfer(value)) - value), 0, 5e-6); }
    }

    [Fact]
    public void pq_and_hlg_reference_luminances_are_correct()
    {
        Assert.InRange(Math.Abs(JxlEnumeratedColorSpace.LinearToPq(1, 100) - .5080784f), 0, 1e-7f);
        Assert.InRange(Math.Abs(JxlEnumeratedColorSpace.PqToLinear(.5080784f, 100) - 1), 0, 1e-6f);
        JxlEnumeratedColorSpace hlg = new(new() { TransferFunction = 18 }, 1000);
        Assert.InRange(Math.Abs(hlg.DecodeTransfer(.5f) - (1f / 12)), 0, 1e-7f);
        Assert.InRange(Math.Abs(hlg.EncodeTransfer(1) - 1), 0, 1e-7f);
    }

    [Theory]
    [InlineData(1U, 0U)]
    [InlineData(8U, 0U)]
    [InlineData(13U, 0U)]
    [InlineData(16U, 0U)]
    [InlineData(17U, 0U)]
    [InlineData(18U, 0U)]
    [InlineData(8U, 1U)]
    [InlineData(13U, 1U)]
    public void synthesized_profiles_describe_enumerated_transfer_functions(uint transfer, uint space)
    {
        JxlColorEncoding encoding = new() { TransferFunction = transfer, ColorSpace = space };
        JxlEnumeratedColorSpace color = new(encoding, 10000);
        byte[] bytes = JxlIccSynthesis.Create(encoding);
        Assert.Equal(bytes.Length, (int)BinaryPrimitives.ReadUInt32BigEndian(bytes));
        IccColorProfile profile = IccColorProfile.Read(bytes);
        Assert.Equal(space == 1 ? 1 : 3, profile.Channels);
        for (int i = 0; i <= 256; i++)
        {
            float value = i / 256f;
            Assert.InRange(Math.Abs(profile.Curves[0].Evaluate(value) - color.DecodeTransfer(value)), 0, 3e-5f);
        }
    }

    [Fact]
    public void enumerated_original_profile_is_attached_and_srgb_profile_is_dropped()
    {
        string path = Path.Combine(Path.GetDirectoryName(JxlHeaderTests.ResourceRoot), "JxlSynthetic", "integer31.jxl");
        using FileStream source = File.OpenRead(path);
        using JxlRawImage original = new JxlDecoder().DecodeRaw(Configuration.Default, source, TestContext.Current.CancellationToken);
        IccColorProfile profile = IccColorProfile.Read(original.Metadata.IccProfile.ToByteArray());
        Assert.Equal(1, profile.Channels);
        Assert.Equal(JxlSampleType.Integer, original.Frames[0].Planes[0].SampleType);
        source.Position = 0;
        using JxlRawImage srgb = new JxlDecoder { TargetColor = JxlTargetColor.Srgb }.DecodeRaw(Configuration.Default, source, TestContext.Current.CancellationToken);
        Assert.Null(srgb.Metadata.IccProfile);
        Assert.Equal(3, srgb.Frames[0].ColorChannelCount);
    }

    [Fact]
    public void original_icc_is_retained_only_for_original_color_output()
    {
        string path = Path.Combine(JxlHeaderTests.ResourceRoot, "grayscale", "input.jxl");
        using FileStream source = File.OpenRead(path);
        using Image<RgbaVector> original = new JxlDecoder { TargetColor = JxlTargetColor.Original }.Decode<RgbaVector>(Configuration.Default, source, TestContext.Current.CancellationToken);
        Assert.Equal(ReadOriginalIcc("grayscale"), original.Metadata.IccProfile.ToByteArray());
        source.Position = 0;
        using Image<RgbaVector> srgb = new JxlDecoder().Decode<RgbaVector>(Configuration.Default, source, TestContext.Current.CancellationToken);
        Assert.Null(srgb.Metadata.IccProfile);
        Assert.Equal(original.Width, srgb.Width); Assert.Equal(original.Height, srgb.Height);
        IccColorProfile profile = IccColorProfile.Read(ReadOriginalIcc("grayscale"));
        Span<float> channels = stackalloc float[3];
        for (int y = 0; y < original.Height; y += 11)
        {
            for (int x = 0; x < original.Width; x += 11)
            {
                channels[0] = original[x, y].R;
                IccColorProfile.Srgb.FromXyz(profile.ToXyz(channels), channels);
                // XYB-to-gray uses luminance; small chroma residuals are discarded in Original output.
                Assert.InRange(Math.Abs(channels[0] - srgb[x, y].R), 0, .004f);
            }
        }
    }

    private static byte[] ReadOriginalIcc(string name)
    {
        if (name is "pq" or "hlg" or "lut8" or "lut16")
        { return File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(JxlHeaderTests.ResourceRoot), "JxlSynthetic", "Color", name + ".icc")); }
        using FileStream input = File.OpenRead(Path.Combine(JxlHeaderTests.ResourceRoot, name, "input.jxl"));
        using JxlCodestreamStream stream = new(input, 64 << 20, true, TestContext.Current.CancellationToken);
        JxlBitReader reader = new(stream, TestContext.Current.CancellationToken);
        JxlImageHeader header = JxlImageHeader.Read(reader, 100_000_000, 64 << 20);
        Assert.True(header.Color.WantIcc);
        return JxlIccCodec.Decode(JxlIccCodec.ReadEncoded(reader, 64 << 20, Configuration.Default.MemoryAllocator), 64 << 20);
    }

    private static float[] ReadSamples(string root, string name, JsonElement hashes)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(root, name));
        Assert.Equal(hashes.GetProperty(name).GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
        float[] values = new float[bytes.Length / 4];
        for (int i = 0; i < values.Length; i++) { values[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * 4)); }
        return values;
    }
}
