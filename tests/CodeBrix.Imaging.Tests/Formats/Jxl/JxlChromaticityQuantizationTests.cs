// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlChromaticityQuantizationTests
{
    [Theory]
    [InlineData(1, 3)] [InlineData(3, 4)] [InlineData(6, 5)] [InlineData(10, 6)]
    public void distance_adjustment_applies_even_without_pixel_search(float distance, uint expected)
    {
        using JxlFloatFrame source = new(Configuration.Default.MemoryAllocator, 3, 3);
        JxlFrameHeader frame = new();
        JxlChromaticityQuantization.Adjust(source, frame, distance, 6, TestContext.Current.CancellationToken);
        Assert.Equal(expected, frame.XQuantScale); Assert.Equal(2U, frame.BQuantScale);
    }

    [Theory]
    [InlineData(17, 13, 0, 7, 1)] [InlineData(17, 13, 1, 7, 1)] [InlineData(17, 13, 2, 7, 1)]
    [InlineData(17, 13, 1, 6, 3)] [InlineData(17, 13, 2, 7, 6)]
    [InlineData(1, 13, 1, 7, 1)] [InlineData(17, 1, 2, 7, 1)] [InlineData(1, 1, 2, 7, 1)]
    [Trait("Category", "JxlNativeReference")]
    public async Task encoded_color_precision_matches_native_for_edges_and_thin_images(int width, int height, int pattern, int effort, float distance)
    {
        JxlExternalResources.RequireEnabled();
        Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("cjxl", ["--version"], TestContext.Current.CancellationToken));
        using Image<Rgb24> source = new(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool alternate = ((x + y) & 1) == 0;
                source[x, y] = pattern == 0 ? new Rgb24(100, 100, 100) : pattern == 1
                    ? alternate ? new Rgb24(255, 0, 0) : new Rgb24(0, 255, 0)
                    : alternate ? new Rgb24(0, 0, 255) : new Rgb24(255, 255, 0);
            }
        }
        using MemoryStream encoded = new();
        new JxlEncoder { Effort = effort, Distance = distance, UseContainer = false, Patches = false, Dots = false }.Encode(source, encoded);
        encoded.Position = 0; JxlFrameHeader managed = ReadFrame(encoded);
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-chromaticity-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string original = Path.Combine(directory, "original.png"), nativePath = Path.Combine(directory, "native.jxl");
            source.SaveAsPng(original);
            await JxlNativeReference.RunAsync("cjxl", [original, nativePath, "--distance=" + distance.ToString("R", CultureInfo.InvariantCulture),
                "--effort=" + effort, "--container=0", "--num_threads=1", "--intensity_target=255"], TestContext.Current.CancellationToken);
            using FileStream native = File.OpenRead(nativePath); JxlFrameHeader expected = ReadFrame(native);
            Assert.Equal(expected.XQuantScale, managed.XQuantScale); Assert.Equal(expected.BQuantScale, managed.BQuantScale);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static JxlFrameHeader ReadFrame(Stream source)
    {
        JxlBitReader bits = new(source, TestContext.Current.CancellationToken);
        JxlImageHeader image = JxlImageHeader.Read(bits, 1 << 20, 1 << 20);
        return JxlFrameHeader.Read(bits, image, 1 << 20, 1 << 20);
    }
}
