// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JpegPixelReferenceTests(ITestOutputHelper output)
{
    private static string Root => Path.Combine(Path.GetDirectoryName(JxlHeaderTests.ResourceRoot), "JxlSynthetic", "Jpeg");

    public static IEnumerable<object[]> Cases()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "manifest.json")));
        foreach (JsonElement item in manifest.RootElement.GetProperty("cases").EnumerateArray())
        { yield return [item.GetProperty("name").GetString()]; }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("Category", "JxlNativeReference")]
    public async Task public_jpeg_pixels_match_native_with_the_same_chroma_upsampling(string name)
    {
        JxlExternalResources.RequireEnabled();
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jpeg-pixels-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(Root, name + ".jpg"), reference = Path.Combine(directory, "reference.ppm");
            // The existing JPEG renderer replicates chroma samples. Match that
            // explicit libjpeg option when checking IDCT and color conversion.
            await JxlNativeReference.RunAsync("djpeg", ["-nosmooth", "-ppm", "-outfile", reference, source], TestContext.Current.CancellationToken);
            using Image<Rgb24> expected = Image.Load<Rgb24>(reference), actual = Image.Load<Rgb24>(source);
            Assert.Equal(expected.Width, actual.Width); Assert.Equal(expected.Height, actual.Height);
            (int peak, double mean) = Difference(expected, actual);
            output.WriteLine($"{name}: matching chroma interpolation, peak={peak}, mean={mean:F6}");
            Assert.InRange(peak, 0, 3);
            await JxlNativeReference.RunAsync("djpeg", ["-ppm", "-outfile", reference, source], TestContext.Current.CancellationToken);
            using Image<Rgb24> smooth = Image.Load<Rgb24>(reference);
            (peak, mean) = Difference(smooth, actual);
            output.WriteLine($"{name}: native smooth interpolation, peak={peak}, mean={mean:F6}");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static (int Peak, double Mean) Difference(Image<Rgb24> expected, Image<Rgb24> actual)
    {
        int peak = 0; long total = 0;
        for (int y = 0; y < expected.Height; y++)
        for (int x = 0; x < expected.Width; x++)
        {
            Rgb24 a = expected[x, y], b = actual[x, y];
            int r = Math.Abs(a.R - b.R), g = Math.Abs(a.G - b.G), blue = Math.Abs(a.B - b.B);
            peak = Math.Max(peak, Math.Max(r, Math.Max(g, blue))); total += r + g + blue;
        }
        return (peak, total / (3.0 * expected.Width * expected.Height));
    }
}
