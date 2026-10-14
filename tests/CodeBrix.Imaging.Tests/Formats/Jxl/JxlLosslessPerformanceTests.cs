// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Imaging.Advanced;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

[Collection("JPEG XL playback performance")]
public class JxlLosslessPerformanceTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("ui", 1920, 1080, 7)]
    [InlineData("ui", 800, 600, 7)]
    [InlineData("blank", 1920, 1080, 7)]
    [InlineData("checker", 129, 257, 7)]
    [InlineData("checker", 129, 257, 2)]
    public async Task predictable_images_do_not_trigger_quadratic_match_search(string pattern, int width, int height, int effort)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("CODEBRIX_IMAGING_RUN_JXL_PERFORMANCE_TESTS") == "1",
            "Set CODEBRIX_IMAGING_RUN_JXL_PERFORMANCE_TESTS=1 on an idle reference host for lossless encoder timing regressions.");
#if DEBUG
        Assert.Skip("Encoder timing regressions require a Release build.");
#endif
        Configuration configuration = Configuration.Default.Clone(); configuration.MaxDegreeOfParallelism = 1;
        using Image<Rgba32> source = new(configuration, width, height);
        for (int y = 0; y < height; y++)
        {
            Span<Rgba32> row = source.DangerousGetPixelRowMemory(y).Span;
            for (int x = 0; x < width; x++)
            {
                row[x] = pattern switch
                {
                    "ui" => y % 40 < 30 && x % 200 < 180
                        ? (x * 7 + y * 13) % 97 < 6 ? new(0, 0, 0, 255) : new(242, 242, 242, 255)
                        : new(51, 77, 153, 255),
                    "checker" => ((x ^ y) & 1) == 0 ? new(0, 0, 0, 255) : new(255, 255, 255, 255),
                    _ => new(0, 0, 0, 255),
                };
            }
        }
        using MemoryStream encoded = new();
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        // The reviewed regressions took 121-398 seconds at effort 7. This broad
        // ceiling catches that failure without imposing a compression benchmark.
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        long started = Stopwatch.GetTimestamp();
        await new JxlEncoder { Lossless = true, Effort = effort }.EncodeAsync(source, encoded, deadline.Token);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
        output.WriteLine($"{pattern} {width}x{height}, effort {effort}: {elapsed.TotalMilliseconds:F3} ms, {encoded.Length} bytes.");
        Assert.True(elapsed.TotalSeconds < 60, $"Lossless encoding exceeded the 60-second regression ceiling: {elapsed}.");
        encoded.Position = 0; using Image<Rgba32> decoded = Image.Load<Rgba32>(configuration, encoded);
        for (int y = 0; y < height; y++) { Assert.True(source.DangerousGetPixelRowMemory(y).Span.SequenceEqual(decoded.DangerousGetPixelRowMemory(y).Span)); }
    }
}
