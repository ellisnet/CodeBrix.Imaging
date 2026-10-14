// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Butteraugli;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Memory;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlButteraugliTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1, 1)] [InlineData(7, 13)] [InlineData(15, 17)] [InlineData(33, 31)]
    public void identical_images_have_zero_error_and_repeated_comparisons_preserve_the_reference(int width, int height)
    {
        using JxlFloatFrame reference = Make(width, height, 0, 0);
        using JxlFloatFrame changed = Make(width, height, 0, .01f);
        using Buffer2D<float> difference = reference.Allocator.Allocate2D<float>(width, height);
        using JxlButteraugli metric = new(reference, 255, TestContext.Current.CancellationToken);
        Assert.Equal(0, metric.Compare(reference, difference, TestContext.Current.CancellationToken));
        float first = metric.Compare(changed, difference, TestContext.Current.CancellationToken);
        Assert.True(float.IsFinite(first) && first > 0);
        Assert.Equal(first, metric.Compare(changed, difference, TestContext.Current.CancellationToken));
        Assert.Equal(0, metric.Compare(reference, difference, TestContext.Current.CancellationToken));
        for (int y = 0; y < height; y++) { foreach (float value in difference.DangerousGetRowSpan(y)) { Assert.Equal(0, value); } }
    }

    [Fact]
    public void larger_uniform_color_errors_have_larger_perceptual_scores()
    {
        using JxlFloatFrame reference = Make(32, 32, 1, 0), small = Make(32, 32, 1, .003f), large = Make(32, 32, 1, .03f);
        using Buffer2D<float> difference = reference.Allocator.Allocate2D<float>(32, 32);
        using JxlButteraugli metric = new(reference, 80, TestContext.Current.CancellationToken);
        float a = metric.Compare(small, difference, TestContext.Current.CancellationToken), b = metric.Compare(large, difference, TestContext.Current.CancellationToken);
        Assert.True(a > 0 && b > a * 3, $"small {a}, large {b}");
    }

    [Fact]
    public void invalid_dimensions_nonfinite_samples_and_cancellation_are_rejected()
    {
        using JxlFloatFrame reference = Make(16, 16, 0, 0), wrong = Make(17, 16, 0, 0);
        using Buffer2D<float> difference = reference.Allocator.Allocate2D<float>(16, 16);
        using JxlButteraugli metric = new(reference, 80, TestContext.Current.CancellationToken);
        Assert.Throws<ArgumentException>(() => metric.Compare(wrong, difference, TestContext.Current.CancellationToken));
        reference.Planes[0].DangerousGetRowSpan(1)[2] = float.NaN;
        Assert.Throws<ArgumentException>(() => metric.Compare(reference, difference, TestContext.Current.CancellationToken));
        reference.Planes[0].DangerousGetRowSpan(1)[2] = 0;
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => metric.Compare(reference, difference, cancellation.Token));
        metric.Dispose();
        Assert.Throws<ObjectDisposedException>(() => metric.Compare(reference, difference, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(1, 1, 80, 1)] [InlineData(7, 13, 255, 0)] [InlineData(8, 8, 80, 0)]
    [InlineData(15, 17, 255, 0)] [InlineData(33, 31, 80, 0)] [InlineData(129, 97, 1000, 2)]
    [InlineData(32, 64, 80, 1)] [InlineData(64, 32, 80, 3)]
    [Trait("Category", "JxlNativeReference")]
    public async Task managed_score_and_every_error_map_pixel_agree_with_pinned_butteraugli(int width, int height, float intensity, int pattern)
    {
        JxlExternalResources.RequireEnabled();
        Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken));
        using JxlFloatFrame reference = Make(width, height, pattern, 0), changed = Make(width, height, pattern, .01f);
        using Buffer2D<float> difference = reference.Allocator.Allocate2D<float>(width, height);
        using JxlButteraugli metric = new(reference, intensity, TestContext.Current.CancellationToken);
        float score = metric.Compare(changed, difference, TestContext.Current.CancellationToken);
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-butteraugli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string a = Path.Combine(directory, "original.pfm"), b = Path.Combine(directory, "changed.pfm"), map = Path.Combine(directory, "difference.pfm");
            WritePfm(reference, a); WritePfm(changed, b);
            string nativeLog = await JxlNativeReference.RunAsync("butteraugli_main", [a, b, "--colorspace", "RGB_D65_SRG_Rel_Lin", "--intensity_target", intensity.ToString(CultureInfo.InvariantCulture), "--rawdistmap", map], TestContext.Current.CancellationToken);
            float nativeScore = float.Parse(nativeLog.AsSpan(0, nativeLog.IndexOf('\n')).Trim(), CultureInfo.InvariantCulture);
            byte[] data = await File.ReadAllBytesAsync(map, TestContext.Current.CancellationToken);
            // The CLI's comparator bypasses the public small-image padding
            // helper. Feed it that helper's replicated canvas, then crop its map.
            int nativeWidth = Math.Max(8, width), nativeHeight = Math.Max(8, height);
            int left = (nativeWidth - width) / 2, top = (nativeHeight - height) / 2;
            byte[] header = Encoding.ASCII.GetBytes($"Pf\n{nativeWidth} {nativeHeight}\n-1.0\n");
            Assert.True(data.AsSpan(0, header.Length).SequenceEqual(header));
            Assert.Equal(header.Length + nativeWidth * nativeHeight * 4, data.Length);
            float peak = 0, nativePeak = 0; double squared = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float expected = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(header.Length + ((nativeHeight - 1 - y - top) * nativeWidth + x + left) * 4, 4));
                    float error = Math.Abs(expected - difference.DangerousGetRowSpan(y)[x]);
                    peak = Math.Max(peak, error); nativePeak = Math.Max(nativePeak, expected); squared += error * error;
                }
            }
            if (width == nativeWidth && height == nativeHeight) { Assert.Equal(nativeScore, nativePeak, .000001f); }
            output.WriteLine($"{width}x{height}, intensity {intensity}, pattern {pattern}: managed {score}, native {nativePeak}, peak {peak}, RMS {Math.Sqrt(squared / (width * height))}");
            float tolerance = .00001f + nativePeak * .00002f;
            Assert.True(peak <= tolerance && Math.Abs(score - nativePeak) <= tolerance,
                $"{width}x{height}, intensity {intensity}, pattern {pattern}: managed {score}, native {nativePeak}, peak {peak}, RMS {Math.Sqrt(squared / (width * height))}");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static JxlFloatFrame Make(int width, int height, int pattern, float error)
    {
        JxlFloatFrame result = JxlButteraugliKernels.Create(width, height, 3, Configuration.Default.MemoryAllocator);
        for (int c = 0; c < 3; c++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float value = pattern == 1 ? .3f : .1f + .5f * ((x * 17 + y * 31 + c * 13) % 257) / 256 + .1f * MathF.Sin(x * .1f + y * .03f);
                    if (pattern == 2) { value *= 4; }
                    if (pattern == 3) { value -= .2f; }
                    result.Planes[c].DangerousGetRowSpan(y)[x] = value + error * (c + 1) * (pattern == 1 ? 1 : ((x * 3 + y * 7) % 11 - 5) / 5f);
                }
            }
        }
        return result;
    }

    private static void WritePfm(JxlFloatFrame frame, string path)
    {
        int width = Math.Max(8, frame.Planes[0].Width), height = Math.Max(8, frame.Planes[0].Height);
        int left = (width - frame.Planes[0].Width) / 2, top = (height - frame.Planes[0].Height) / 2;
        using FileStream file = File.Create(path);
        file.Write(Encoding.ASCII.GetBytes($"PF\n{width} {height}\n-1.0\n"));
        byte[] row = new byte[width * 12];
        for (int y = height - 1; y >= 0; y--)
        {
            for (int x = 0; x < width; x++)
            { for (int c = 0; c < 3; c++) { BinaryPrimitives.WriteSingleLittleEndian(row.AsSpan((x * 3 + c) * 4, 4),
                frame.Planes[c].DangerousGetRowSpan(Math.Clamp(y - top, 0, frame.Planes[c].Height - 1))[Math.Clamp(x - left, 0, frame.Planes[c].Width - 1)]); } }
            file.Write(row);
        }
    }
}
