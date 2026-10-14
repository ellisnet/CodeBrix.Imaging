// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Formats.Jxl.VarDct;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlEncoderSizeTests(ITestOutputHelper output)
{
    private static readonly string[] Corpus = ["grayscale", "lz77_flower", "bicycles", "noise", "delta_palette", "patches"];

    public static IEnumerable<TheoryDataRow<int, float>> QualityPoints()
    {
        for (int effort = 1; effort <= 10; effort++)
        {
            foreach (float distance in new[] { .5f, 1f, 2f, 3f })
            {
                yield return new(effort, distance)
                {
                    Traits = new()
                    {
                        ["JxlEffort"] = [effort.ToString(CultureInfo.InvariantCulture)],
                        ["JxlDistance"] = [Format(distance)],
                        ["JxlQualityPoint"] = [$"{effort}:{Format(distance)}"],
                    },
                };
            }
        }
    }

    [Theory]
    [MemberData(nameof(QualityPoints))]
    [Trait("Category", "JxlEncoderSize")]
    public async Task matched_butteraugli_sizes_meet_the_agreed_corpus_limits(int effort, float distance)
    {
        JxlExternalResources.RequireEnabled();
        Assert.SkipUnless(Environment.GetEnvironmentVariable("CODEBRIX_IMAGING_RUN_JXL_SIZE_TESTS") == "1", "Set CODEBRIX_IMAGING_RUN_JXL_SIZE_TESTS=1 for the long-running matched-quality corpus gate.");
        await Pin(); double logRatio = 0;
        List<string> failures = [];
        foreach (string name in Corpus)
        {
            Result result = await Compare(name, effort, distance);
            if (result.RelativeScoreDifference > .005) { failures.Add($"Quality matching failed: {JsonSerializer.Serialize(result)}"); }
            if (result.Ratio > 1.20) { failures.Add($"Individual size gate failed: {JsonSerializer.Serialize(result)}"); }
            logRatio += Math.Log(result.Ratio);
        }
        double geometricMean = Math.Exp(logRatio / Corpus.Length);
        if (geometricMean > 1.10) { failures.Add($"Geometric mean {geometricMean:R} exceeds 1.10."); }
        Report(effort, distance, new { Phase = "summary", effort, distance, geometricMean, failures });
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    [Trait("Category", "JxlEncoderSizeProbe")]
    public async Task report_small_corpus_quality_matches_for_encoder_tuning()
    {
        JxlExternalResources.RequireEnabled();
        Assert.SkipUnless(Environment.GetEnvironmentVariable("CODEBRIX_IMAGING_RUN_JXL_SIZE_PROBE") == "1", "Set CODEBRIX_IMAGING_RUN_JXL_SIZE_PROBE=1 for the encoder tuning probe.");
        string[] point = (Environment.GetEnvironmentVariable("CODEBRIX_IMAGING_JXL_SIZE_PROBE_POINT") ?? "7:1").Split(':');
        Assert.Equal(2, point.Length);
        int effort = int.Parse(point[0], CultureInfo.InvariantCulture); float distance = float.Parse(point[1], CultureInfo.InvariantCulture);
        Assert.InRange(effort, 1, 10); Assert.InRange(distance, .1f, 25);
        string image = Environment.GetEnvironmentVariable("CODEBRIX_IMAGING_JXL_SIZE_PROBE_IMAGE");
        if (!string.IsNullOrEmpty(image)) { Assert.Contains(image, Corpus); }
        string dotsOption = Environment.GetEnvironmentVariable("CODEBRIX_IMAGING_JXL_SIZE_PROBE_DOTS");
        bool? dots = string.IsNullOrEmpty(dotsOption) ? null : bool.Parse(dotsOption);
        await Pin();
        foreach (string name in Corpus)
        {
            if (!string.IsNullOrEmpty(image) && image != name) { continue; }
            // Report every image even when a discrete quantization step prevents
            // matching. The corpus gate above enforces quality and size limits.
            Report(effort, distance, new { Phase = "probe-options", name, Dots = dots });
            await Compare(name, effort, distance, dots);
        }
    }

    private static async Task Pin() => Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("cjxl", ["--version"], TestContext.Current.CancellationToken));

    private async Task<Result> Compare(string name, int effort, float distance, bool? dots = null)
    {
        using Image<Rgb24> loaded = Image.Load<Rgb24>(Path.Combine(JxlHeaderTests.ResourceRoot, name, "ref.png"));
        // Both encoders see the same RGB pixels without source metadata or ICC.
        using Image<Rgb24> source = new(loaded.Width, loaded.Height);
        for (int y = 0; y < source.Height; y++) { loaded.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y).CopyTo(source.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y)); }
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-size-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string original = Path.Combine(directory, "original.png"), native = Path.Combine(directory, "native.jxl"), managed = Path.Combine(directory, "managed.jxl");
            source.SaveAsPng(original);
            await JxlNativeReference.RunAsync("cjxl", [original, native, "--distance=" + Format(distance), "--effort=" + effort,
                "--container=0", "--num_threads=1", "--intensity_target=255"], TestContext.Current.CancellationToken);
            double target = await Score(native); long nativeBytes = new FileInfo(native).Length;
            bool describe = Environment.GetEnvironmentVariable("CODEBRIX_IMAGING_RUN_JXL_SIZE_PROBE") == "1";
            if (describe) { Report(effort, distance, new { Phase = "native-layout", name, frames = Describe(native) }); }
            string artifacts = describe ? Environment.GetEnvironmentVariable("CODEBRIX_IMAGING_JXL_SIZE_REPORT_DIRECTORY") : null;
            if (!string.IsNullOrEmpty(artifacts))
            {
                artifacts = Path.Combine(artifacts, $"effort-{effort}-distance-{Format(distance)}-{name}"); Directory.CreateDirectory(artifacts);
                File.Copy(original, Path.Combine(artifacts, "original.png"), true); File.Copy(native, Path.Combine(artifacts, "native.jxl"), true);
            }
            float lower = .05f, upper = 25, trial = distance, selectedDistance = 0, anchor = 0;
            double bestDifference = double.PositiveInfinity, selectedScore = 0; long selectedBytes = 0;
            object[] selectedLayout = null;
            // Transform and integer quantizer choices make measured quality
            // discontinuous. Follow bisection with a bounded nearby search
            // instead of assuming every successive sample is monotonic.
            HashSet<float> attempted = [];
            List<(float Distance, double Score)> samples = [];
            // Restart the local scan around an improved match after each
            // refinement round. A fixed initial anchor misses nearby solutions
            // when interpolation discovers a better region later in the search.
            const int localRoundLength = 64;
            for (int step = 0; step < 18 + 3 * localRoundLength; step++)
            {
                if (step >= 18)
                {
                    int offset = (step - 18) % localRoundLength;
                    if (offset == 0) { anchor = selectedDistance; }
                    trial = Math.Clamp(anchor * (1 + (offset % 2 == 0 ? 1 : -1) * ((offset / 2 + 1) * .0025f)), .05f, 25);
                    if (offset >= 16)
                    {
                        // Refine crossings found by the local scan. Repeating
                        // bisection at a discrete jump cannot improve a match;
                        // instead inspect the other observed quality crossings.
                        samples.Sort((a, b) => a.Distance.CompareTo(b.Distance));
                        double bestPriority = double.PositiveInfinity;
                        for (int i = 1; i < samples.Count; i++)
                        {
                            var a = samples[i - 1]; var b = samples[i];
                            if ((a.Score - target) * (b.Score - target) >= 0 || b.Distance - a.Distance < .0001f * a.Distance) { continue; }
                            double fraction = Math.Clamp((target - a.Score) / (b.Score - a.Score), .1, .9);
                            float candidate = (float)(a.Distance + (b.Distance - a.Distance) * fraction);
                            double priority = Math.Min(Math.Abs(a.Score - target), Math.Abs(b.Score - target)) + .15 * Math.Abs(a.Score - b.Score);
                            if (priority >= bestPriority || attempted.Contains(candidate)) { continue; }
                            bestPriority = priority; trial = candidate;
                        }
                    }
                }
                if (!attempted.Add(trial)) { continue; }
                TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
                using (FileStream stream = File.Create(managed)) { new JxlEncoder { Effort = effort, Distance = trial, UseContainer = false, Dots = dots }.Encode(source, stream); }
                double score = await Score(managed), difference = Math.Abs(score - target); long bytes = new FileInfo(managed).Length;
                samples.Add((trial, score));
                Report(effort, distance, new { Phase = "trial", name, effort, distance, trial, target, score, bytes });
                if (describe && step == 0) { Report(effort, distance, new { Phase = "initial-layout", name, frames = Describe(managed) }); }
                if (difference < bestDifference)
                {
                    bestDifference = difference; selectedDistance = trial; selectedScore = score; selectedBytes = bytes;
                    if (describe) { selectedLayout = Describe(managed); }
                    if (!string.IsNullOrEmpty(artifacts)) { File.Copy(managed, Path.Combine(artifacts, "managed.jxl"), true); }
                }
                if (difference <= target * .005) { break; }
                if (score > target) { upper = trial; trial = (lower + upper) * .5f; }
                else { lower = trial; trial = upper == 25 ? Math.Min(upper, trial * 2) : (lower + upper) * .5f; }
            }
            Result result = new(name, effort, distance, target, nativeBytes, selectedDistance, selectedScore, selectedBytes,
                selectedBytes / (double)nativeBytes, bestDifference / target);
            if (describe) { Report(effort, distance, new { Phase = "managed-layout", name, frames = selectedLayout }); }
            Report(effort, distance, result); return result;

            async Task<double> Score(string encoded)
            {
                string decoded = Path.Combine(directory, "decoded.png");
                await JxlNativeReference.RunAsync("djxl", [encoded, decoded, "--bits_per_sample=16", "--num_threads=1"], TestContext.Current.CancellationToken);
                string log = await JxlNativeReference.RunAsync("butteraugli_main", [original, decoded, "--intensity_target", "255"], TestContext.Current.CancellationToken);
                double score = double.Parse(log.AsSpan(0, log.IndexOf('\n')), CultureInfo.InvariantCulture);
                Assert.True(double.IsFinite(score) && score > 0, $"Invalid Butteraugli score {score:R}"); return score;
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static object[] Describe(string path)
    {
        using FileStream stream = File.OpenRead(path); JxlBitReader reader = new(stream, TestContext.Current.CancellationToken);
        JxlImageHeader image = JxlImageHeader.Read(reader, 100_000_000, 64 << 20);
        Assert.False(image.Color.WantIcc);
        List<object> frames = [];
        while (true)
        {
            JxlFrameHeader frame = JxlFrameHeader.Read(reader, image, 100_000_000, 64 << 20);
            JxlFrameToc toc = JxlFrameToc.Read(reader, frame, Configuration.Default.MemoryAllocator);
            uint[] sizes = new uint[toc.Sizes.Length];
            for (int i = 0; i < sizes.Length; i++) { sizes[i] = toc.Sizes[toc.LogicalToPhysical?[i] ?? i]; }
            object quantization = frame.Modular ? null : DescribeQuantization(reader, image, frame, toc);
            frames.Add(new { frame.FrameType, frame.Modular, frame.Width, frame.Height, frame.XQuantScale, frame.BQuantScale,
                frame.Flags, frame.Restoration.GaborEnabled, frame.Restoration.EpfIterations, Sections = sizes, Quantization = quantization });
            if (frame.Modular) { reader.SkipBits(toc.TotalBytes * 8); }
            if (frame.IsLast) { return frames.ToArray(); }
        }
    }

    private static object DescribeQuantization(JxlBitReader reader, JxlImageHeader image, JxlFrameHeader frame, JxlFrameToc toc)
    {
        Assert.Empty(image.ExtraChannels); Assert.False(frame.UseLfFrame);
        var allocator = Configuration.Default.MemoryAllocator;
        using JxlModularWorkspace workspace = new(allocator, (long)frame.Width * frame.Height);
        JxlFrameSections sections = new(reader, toc, allocator, TestContext.Current.CancellationToken);
        JxlBitReader globalReader = sections.Get(0);
        if ((frame.Flags & 2) != 0) { JxlPatches.Read(globalReader, image, frame, allocator); }
        if ((frame.Flags & 16) != 0) { JxlSplines.Read(globalReader, frame, allocator); }
        if ((frame.Flags & 1) != 0) { JxlNoise.Read(globalReader); }
        if (!globalReader.ReadBool()) { for (int c = 0; c < 3; c++) { globalReader.ReadF16(); } }
        JxlLfGlobal global = JxlLfGlobal.Read(globalReader, allocator);
        JxlMaTree tree = globalReader.ReadBool() ? JxlMaTree.Read(globalReader, allocator, 1 << 22) : null;
        Dictionary<string, int> counts = []; long multiplierSum = 0, area = 0;
        int columns = ((int)frame.Width + 2047) / 2048;
        for (int group = 0; group < (int)frame.LfGroupCount; group++)
        {
            int width = Math.Min(2048, (int)frame.Width - group % columns * 2048), height = Math.Min(2048, (int)frame.Height - group / columns * 2048);
            JxlBitReader lf = sections.Get(1 + group); lf.ReadBits(2);
            List<JxlModularPlane> planes = [];
            for (int c = 0; c < 3; c++) { planes.Add(workspace.Allocate((width + 7) / 8, (height + 7) / 8)); }
            JxlModularFrame.DecodeGroup(lf, planes, tree, 1 + group, (int)image.BitDepth.Bits, workspace);
            using JxlHfMetadata metadata = JxlHfMetadata.Read(lf, frame, group, width, height, (int)image.BitDepth.Bits, tree, global.GlobalScale, workspace);
            for (int y = 0; y < metadata.Blocks.Height; y++)
            {
                foreach (JxlVarBlock block in metadata.Blocks.DangerousGetRowSpan(y))
                {
                    if (block.Multiplier <= 0) { continue; }
                    string name = block.Transform.ToString(); counts.TryGetValue(name, out int count); counts[name] = count + 1;
                    (int w, int h) = block.Transform.Blocks(); area += w * h; multiplierSum += (long)block.Multiplier * w * h;
                }
            }
        }
        return new { global.GlobalScale, global.QuantLf, global.BlockContext.ClusterCount,
            MeanAcPrecision = global.GlobalScale / 65536.0 * multiplierSum / area, Transforms = counts };
    }

    private void Report<T>(int effort, float distance, T value)
    {
        string json = JsonSerializer.Serialize(value);
        output.WriteLine(json);
        // Optional progress files survive a failed gate and make long runs
        // inspectable while the test runner buffers its normal output.
        string reportDirectory = Environment.GetEnvironmentVariable("CODEBRIX_IMAGING_JXL_SIZE_REPORT_DIRECTORY");
        if (string.IsNullOrEmpty(reportDirectory)) { return; }
        Directory.CreateDirectory(reportDirectory);
        File.AppendAllText(Path.Combine(reportDirectory, $"effort-{effort}-distance-{Format(distance)}.jsonl"), json + Environment.NewLine);
    }

    private static string Format(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    private sealed record Result(string Name, int Effort, float NativeDistance, double NativeScore, long NativeBytes,
        float ManagedDistance, double ManagedScore, long ManagedBytes, double Ratio, double RelativeScoreDifference);
}
