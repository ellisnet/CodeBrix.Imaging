// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Color;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlHdrTests
{
    private static string Root => Path.Combine(Path.GetDirectoryName(JxlHeaderTests.ResourceRoot), "JxlSynthetic", "Hdr");

    public static IEnumerable<object[]> Cases()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "manifest.json")));
        foreach (JsonElement test in manifest.RootElement.GetProperty("cases").EnumerateArray())
        {
            foreach (JsonProperty reference in test.GetProperty("references").EnumerateObject())
            {
                yield return [test.GetProperty("name").GetString(), reference.Name,
                    test.GetProperty("inputSha256").GetString(), reference.Value.GetString()];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void hdr_output_matches_offline_native_reference(string name, string output, string inputHash, string referenceHash)
    {
        byte[] encoded = File.ReadAllBytes(Path.Combine(Root, name + ".jxl"));
        byte[] reference = File.ReadAllBytes(Path.Combine(Root, name + "." + output + ".f32"));
        Assert.Equal(inputHash, Convert.ToHexStringLower(SHA256.HashData(encoded)));
        Assert.Equal(referenceHash, Convert.ToHexStringLower(SHA256.HashData(reference)));
        using MemoryStream input = new(encoded, false);
        JxlDecoder decoder = new() { TargetColor = output == "original" ? JxlTargetColor.Original : JxlTargetColor.Srgb,
            TargetDisplayLuminance = output == "sdr" ? 255 : 0 };
        using Image<RgbaVector> actual = decoder.Decode<RgbaVector>(Configuration.Default, input, TestContext.Current.CancellationToken);
        ReadOnlySpan<float> expected = MemoryMarshal.Cast<byte, float>(reference);
        Assert.Equal(actual.Width * actual.Height * 3, expected.Length);
        float peak = 0, worstExpected = 0, worstActual = 0; int worstIndex = 0; double sum = 0;
        for (int y = 0, i = 0; y < actual.Height; y++)
        {
            for (int x = 0; x < actual.Width; x++)
            {
                RgbaVector value = actual[x, y];
                Vector3 rendered = new(value.R, value.G, value.B), native = new(expected[i], expected[i + 1], expected[i + 2]);
                if (output == "srgb")
                {
                    // Compare unbounded HDR in relative XYZ. Native CMS transfer-function
                    // approximations can cancel near an RGB gamut boundary.
                    rendered = ToXyz(rendered); native = ToXyz(native);
                    float scale = Math.Max(1, Math.Max(Math.Abs(native.X), Math.Max(Math.Abs(native.Y), Math.Abs(native.Z))));
                    rendered /= scale; native /= scale;
                }
                for (int c = 0; c < 3; c++)
                {
                    float error = Math.Abs(rendered[c] - native[c]); i++;
                    if (error > peak) { peak = error; worstExpected = native[c]; worstActual = rendered[c]; worstIndex = i - 1; }
                    sum += error * error;
                }
            }
        }
        double rms = Math.Sqrt(sum / expected.Length);
        Assert.True(peak <= .0005f && rms <= .0001, $"{name}/{output}: peak={peak}, RMS={rms}, worst [{worstIndex}]: expected={worstExpected}, actual={worstActual}");
    }

    private static Vector3 ToXyz(Vector3 value) => JxlEnumeratedColorSpace.SrgbToXyz.Apply(new(
        JxlEnumeratedColorSpace.LinearSrgb(value.X), JxlEnumeratedColorSpace.LinearSrgb(value.Y), JxlEnumeratedColorSpace.LinearSrgb(value.Z)));

    [Fact]
    public void tone_mapping_preserves_black_and_maps_hdr_peak_to_display_white()
    {
        JxlToneMapper tone = new(10000, 0, 255, false);
        Assert.Equal(System.Numerics.Vector3.Zero, tone.Apply(System.Numerics.Vector3.Zero));
        var white = tone.Apply(System.Numerics.Vector3.One);
        Assert.InRange(white.X, .99999f, 1.00001f);
        float previous = 0;
        for (int i = 0; i <= 100; i++)
        {
            var mapped = tone.Apply(new(i / 100f));
            Assert.InRange(mapped.X, previous - 1e-6f, 1.00001f); previous = mapped.X;
        }
    }
}
