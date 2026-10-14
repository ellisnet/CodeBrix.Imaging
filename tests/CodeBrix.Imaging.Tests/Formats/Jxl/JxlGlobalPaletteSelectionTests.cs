// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Imaging.Advanced;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlGlobalPaletteSelectionTests
{
    [Theory]
    [InlineData(false, 4)] [InlineData(false, 7)] [InlineData(true, 4)] [InlineData(true, 7)]
    public void estimated_global_palettes_preserve_every_pixel_across_partial_groups(bool independent, int effort)
    {
        using Image<Rgba32> source = Source(independent); using MemoryStream encoded = Encode(source, effort);
        using Image<Rgba32> decoded = Image.Load<Rgba32>(encoded);
        Equal(source, decoded);
    }

    [Theory]
    [InlineData(false, 4)] [InlineData(false, 7)] [InlineData(true, 4)] [InlineData(true, 7)]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_reads_estimated_global_palettes_exactly(bool independent, int effort)
    {
        JxlExternalResources.RequireEnabled();
        using Image<Rgba32> source = Source(independent); using MemoryStream encoded = Encode(source, effort);
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-palette-search-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "image.jxl"), output = Path.Combine(directory, "native.png");
            await File.WriteAllBytesAsync(input, encoded.ToArray(), TestContext.Current.CancellationToken);
            await JxlNativeReference.RunAsync("djxl", [input, output, "--bits_per_sample=8", "--num_threads=1"], TestContext.Current.CancellationToken);
            using Image<Rgba32> decoded = Image.Load<Rgba32>(output); Equal(source, decoded);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static Image<Rgba32> Source(bool independent)
    {
        Image<Rgba32> image = new(513, 129);
        for (int y = 0; y < image.Height; y++)
        {
            Span<Rgba32> row = image.DangerousGetPixelRowMemory(y).Span;
            for (int x = 0; x < row.Length; x++)
            {
                row[x] = independent ? new((byte)(x % 32 * 8), (byte)(y % 32 * 8), (byte)((x + y) % 32 * 8), (byte)((x ^ y) % 64 * 4))
                    : y % 40 < 30 && x % 200 < 180
                        ? (x * 7 + y * 13) % 97 < 6 ? new(0, 0, 0, 255) : new(242, 242, 242, 255)
                        : new(51, 77, 153, 255);
            }
        }
        return image;
    }

    private static MemoryStream Encode(Image<Rgba32> source, int effort)
    {
        MemoryStream output = new();
        try { new JxlEncoder { Lossless = true, Effort = effort, Patches = false }.Encode(source, output); output.Position = 0; return output; }
        catch { output.Dispose(); throw; }
    }

    private static void Equal(Image<Rgba32> expected, Image<Rgba32> actual)
    {
        Assert.Equal(expected.Width, actual.Width); Assert.Equal(expected.Height, actual.Height);
        for (int y = 0; y < expected.Height; y++) { Assert.True(expected.DangerousGetPixelRowMemory(y).Span.SequenceEqual(actual.DangerousGetPixelRowMemory(y).Span)); }
    }
}
