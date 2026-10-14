// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Runtime.InteropServices;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlParallelTests
{
    [Theory]
    [InlineData("animation_newtons_cradle")]
    [InlineData("animation_spline")]
    [InlineData("progressive")]
    public void independent_groups_and_color_channels_match_serial_decode_exactly(string name)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(JxlHeaderTests.ResourceRoot, name, "input.jxl"));
        Configuration serial = Configuration.Default.Clone(), parallel = Configuration.Default.Clone();
        serial.MaxDegreeOfParallelism = 1; parallel.MaxDegreeOfParallelism = 4;
        JxlDecoder decoder = new() { MaxFrames = 2 };
        using MemoryStream first = new(bytes, false), second = new(bytes, false);
        using Image<RgbaVector> expected = decoder.Decode<RgbaVector>(serial, first, TestContext.Current.CancellationToken);
        using Image<RgbaVector> actual = decoder.Decode<RgbaVector>(parallel, second, TestContext.Current.CancellationToken);
        Assert.Equal(expected.Frames.Count, actual.Frames.Count);
        for (int f = 0; f < expected.Frames.Count; f++)
        {
            for (int y = 0; y < expected.Height; y++)
            {
                ReadOnlySpan<byte> a = MemoryMarshal.AsBytes(expected.Frames[f].PixelBuffer.DangerousGetRowSpan(y));
                ReadOnlySpan<byte> b = MemoryMarshal.AsBytes(actual.Frames[f].PixelBuffer.DangerousGetRowSpan(y));
                Assert.True(a.SequenceEqual(b), $"{name}: frame {f}, row {y} differs with parallel decoding.");
            }
        }
    }

    [Fact]
    public void parallel_failure_preserves_the_decode_exception_contract()
    {
        Assert.Throws<InvalidImageContentException>(() => JxlParallel.For(4, 2, TestContext.Current.CancellationToken,
            _ => throw new InvalidImageContentException("Invalid test group.")));
    }
}
