// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.Metadata;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlRenderingTests
{
    private static string SyntheticRoot => Path.Combine(Path.GetDirectoryName(JxlHeaderTests.ResourceRoot), "JxlSynthetic");

    public static IEnumerable<object[]> SamplingCases()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(SyntheticRoot, "manifest.json")));
        foreach (JsonElement test in manifest.RootElement.GetProperty("cases").EnumerateArray())
        { yield return [test.GetProperty("name").GetString()]; }
    }

    public static IEnumerable<object[]> VarDctCases()
    {
        foreach (string family in new[] { "vardct", "noise", "progressive" })
        {
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(SyntheticRoot, family + "-manifest.json")));
            foreach (JsonElement test in manifest.RootElement.GetProperty("cases").EnumerateArray())
            { yield return [test.GetProperty("name").GetString()]; }
        }
    }

    [Theory]
    [MemberData(nameof(SamplingCases))]
    [MemberData(nameof(VarDctCases))]
    public void sampling_edges_match_offline_native_reference(string name)
    {
        using FileStream headerInput = File.OpenRead(Path.Combine(SyntheticRoot, name + ".jxl"));
        JxlBitReader bits = new(headerInput, TestContext.Current.CancellationToken);
        JxlImageHeader header = JxlImageHeader.Read(bits, 100_000_000, 64 << 20);
        JxlFrameHeader frame = JxlFrameHeader.Read(bits, header, 100_000_000, 64 << 20);
        uint factor = (uint)(name[^1] - '0');
        if (name.StartsWith("progressive-", StringComparison.Ordinal) && factor == 1)
        { Assert.Equal(2U, frame.LfLevel); }
        else
        {
            Assert.Equal(factor, frame.Upsampling);
            if (name.StartsWith("noise-", StringComparison.Ordinal)) { Assert.NotEqual(0UL, frame.Flags & 1); }
        }
        using JxlNpyImage reference = JxlNpyImage.Open(Path.Combine(SyntheticRoot, name + ".npy"));
        using Image<RgbaVector> actual = Image.Load<RgbaVector>(Path.Combine(SyntheticRoot, name + ".jxl"));
        Assert.Equal(reference.Width, actual.Width);
        Assert.Equal(reference.Height, actual.Height);
        float[] expected = reference.ReadFrame(0);
        float peak = 0;
        double squared = 0;
        for (int y = 0; y < actual.Height; y++)
        {
            for (int x = 0; x < actual.Width; x++)
            {
                RgbaVector pixel = actual[x, y];
                int i = ((y * actual.Width) + x) * 3;
                squared += Math.Pow(pixel.R - expected[i], 2) + Math.Pow(pixel.G - expected[i + 1], 2) + Math.Pow(pixel.B - expected[i + 2], 2);
                peak = Math.Max(peak, Math.Abs(pixel.R - expected[i]));
                peak = Math.Max(peak, Math.Abs(pixel.G - expected[i + 1]));
                peak = Math.Max(peak, Math.Abs(pixel.B - expected[i + 2]));
            }
        }
        double rms = Math.Sqrt(squared / expected.Length);
        // VarDCT implementations differ in float operation ordering and fused arithmetic.
        // These limits are stricter than the conformance suite's 0.004 peak allowance.
        float peakLimit = name.StartsWith("rgb-", StringComparison.Ordinal) ? .00002f : .0001f;
        Assert.True(peak <= peakLimit && rms <= .00001, $"{name}: peak error {peak}, RMS {rms}");
    }

    [Fact]
    public void blending_without_alpha_replaces_a_nan_background()
    {
        var allocator = Configuration.Default.MemoryAllocator;
        using JxlFloatFrame background = new(allocator, 1, 1);
        using JxlFloatFrame foreground = new(allocator, 1, 1);
        background.Planes[0] = allocator.Allocate2D<float>(1, 1);
        foreground.Planes[0] = allocator.Allocate2D<float>(1, 1);
        background.Planes[0].DangerousGetRowSpan(0)[0] = float.NaN;
        foreground.Planes[0].DangerousGetRowSpan(0)[0] = .75f;
        JxlImageHeader image = new() { Size = new(1, 1) };
        JxlFrameHeader frame = new() { Width = 1, Height = 1, Blend = new(2, 0, false, 0) };
        using JxlFloatFrame blended = JxlCompositor.Compose(image, frame, foreground, [background, null, null, null], TestContext.Current.CancellationToken);
        Assert.Equal(.75f, blended.Planes[0].DangerousGetRowSpan(0)[0]);
    }

    [Theory]
    [InlineData(FrameDecodingMode.First, 8U, 1)]
    [InlineData(FrameDecodingMode.All, 3U, 3)]
    public void limits_display_frames_without_counting_hidden_layers(FrameDecodingMode mode, uint maximum, int expected)
    {
        using FileStream input = File.OpenRead(Path.Combine(JxlHeaderTests.ResourceRoot, "animation_newtons_cradle", "input.jxl"));
        JxlDecoder decoder = new() { DecodingMode = mode, MaxFrames = maximum };
        using Image<Rgba32> image = decoder.Decode<Rgba32>(Configuration.Default, input, TestContext.Current.CancellationToken);
        Assert.Equal(expected, image.Frames.Count);
        Assert.Equal(expected, image.Metadata.GetJxlMetadata().FrameCount);
        Assert.Equal(5U, image.Frames.RootFrame.Metadata.GetJxlMetadata().Duration);
    }
}
