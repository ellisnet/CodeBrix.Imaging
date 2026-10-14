// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using CodeBrix.Imaging.Formats.Jxl;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

[CollectionDefinition("JPEG XL playback performance", DisableParallelization = true)]
public class JxlPlaybackPerformanceCollection;

[Collection("JPEG XL playback performance")]
public class JxlPlaybackPerformanceTests(ITestOutputHelper output)
{
    private const string EnableVariable = "CODEBRIX_IMAGING_RUN_JXL_PERFORMANCE_TESTS";

    [Theory]
    [InlineData("animation_icos4d", 48)]
    [InlineData("animation_newtons_cradle", 36)]
    [InlineData("animation_spline", 60)]
    public void displayed_frames_meet_reference_playback_budget(string name, int expectedFrames)
    {
        // Arrange: timing acceptance is opt-in because it targets the agreed host.
        // These inputs are repository resources; no native tools or downloads.
        Assert.SkipUnless(Environment.GetEnvironmentVariable(EnableVariable) == "1",
            "Set " + EnableVariable + "=1 on an idle reference host to run the Release playback timing gate.");
#if DEBUG
        Assert.Skip("Playback timing acceptance requires a Release build.");
#endif
        byte[] encoded = File.ReadAllBytes(Path.Combine(JxlHeaderTests.ResourceRoot, name, "input.jxl"));
        Configuration configuration = Configuration.Default.Clone();
        List<double> times = [], ratios = [];

        // Act: two complete warm-up passes, then five measured complete passes.
        for (int pass = 0; pass < 7; pass++)
        {
            TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
            using MemoryStream input = new(encoded, false);
            using JxlAnimationReader reader = JxlAnimationReader.Open(configuration, input,
                new() { Loop = false, DecodeAhead = 1 }, TestContext.Current.CancellationToken);
            int count = 0;
            while (reader.TryGetNextFrame(out JxlAnimationFrame frame))
            {
                using (frame)
                {
                    count++;
                    if (pass < 2 || frame.Duration <= TimeSpan.Zero) { continue; }
                    times.Add(frame.DecodeTime.TotalMilliseconds);
                    ratios.Add(frame.DecodeTime / frame.Duration);
                }
            }
            Assert.Equal(expectedFrames, count);
        }

        // Assert: report measurements through the standard test runner output.
        Assert.Equal(expectedFrames * 5, times.Count);
        times.Sort(); ratios.Sort();
        double ratio = Median(ratios);
        output.WriteLine(JsonSerializer.Serialize(new
        {
            animation = name,
            frames = expectedFrames,
            measuredFrames = times.Count,
            medianMilliseconds = Median(times),
            p95Milliseconds = times[(int)((times.Count - 1) * .95)],
            medianDurationRatio = ratio,
            runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            processors = Environment.ProcessorCount,
            maxDegreeOfParallelism = configuration.MaxDegreeOfParallelism,
            passed = ratio <= .5,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Assert.True(ratio <= .5, $"{name}: median decode-time/display-duration ratio {ratio:F6} exceeds 0.5.");
    }

    private static double Median(List<double> sorted) => sorted.Count % 2 == 0
        ? (sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2 : sorted[sorted.Count / 2];
}
