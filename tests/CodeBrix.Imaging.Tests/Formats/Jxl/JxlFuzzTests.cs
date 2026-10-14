// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Original test driver using the MIT-licensed SharpFuzz 2.3.0 public API.
// Only a disposable copy of the library is instrumented; no native fuzzer runs.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.PixelFormats;
using SharpFuzz;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class JxlFuzzCollection
{
    public const string Name = "JPEG XL instrumented fuzzing";
}

[Collection(JxlFuzzCollection.Name)]
public class JxlFuzzTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "JxlFuzz")]
    public void instrumented_mutations_have_bounded_work_and_consistent_stream_results()
    {
        const int seed = 13172026;
        int iterations = 256;
        string configured = Environment.GetEnvironmentVariable("CODEBRIX_IMAGING_JXL_FUZZ_ITERATIONS");
        if (configured != null)
        { Assert.True(int.TryParse(configured, out iterations) && iterations is >= 1 and <= 100000, "JXL fuzz iterations must be from 1 through 100000."); }
        Assert.Null(Environment.GetEnvironmentVariable("__AFL_SHM_ID"));
        string directory = Path.Combine(AppContext.BaseDirectory, ".jxl-fuzz", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string library = Path.Combine(directory, "CodeBrix.Imaging.dll");
        File.Copy(typeof(JxlDecoder).Assembly.Location, library);
        FuzzLoadContext context = null;
        HashSet<(int Id, string Method)> coverage = [];
        long steps = 0; bool exhausted = false; int completed = 0, validPixels = 0;
        CancellationToken cancellation = TestContext.Current.CancellationToken;
        try
        {
            string previousCallback = Environment.GetEnvironmentVariable("SHARPFUZZ_ENABLE_ON_BRANCH_CALLBACK");
            string[] types;
            try
            {
                Environment.SetEnvironmentVariable("SHARPFUZZ_ENABLE_ON_BRANCH_CALLBACK", "1");
                types = Instrument(library);
            }
            finally { Environment.SetEnvironmentVariable("SHARPFUZZ_ENABLE_ON_BRANCH_CALLBACK", previousCallback); }
            Assert.Contains("CodeBrix.Imaging.Formats.Jxl.JxlImageFormatDetector", types);
            Assert.Contains("CodeBrix.Imaging.Formats.Jxl.Container.JxlCodestreamStream", types);
            Assert.Contains("CodeBrix.Imaging.Formats.Jxl.JxlDecoderCore", types);
            context = new(library);
            Assembly targets = context.LoadFromAssemblyPath(typeof(JxlFuzzTarget).Assembly.Location);
            var run = targets.GetType(typeof(JxlFuzzTarget).FullName).GetMethod(nameof(JxlFuzzTarget.Run))
                .CreateDelegate<Func<byte[], int, bool, CancellationToken, string>>();
            List<byte[]> corpus = Seeds(); int initial = corpus.Count;
            Random random = new(seed);
            SharpFuzz.Common.Trace.OnBranch = (id, method) =>
            {
                if (exhausted) { return; }
                if (++steps > 20_000_000 || ((steps & 1023) == 0 && cancellation.IsCancellationRequested))
                {
                    exhausted = true;
                    cancellation.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("Fuzz case exceeded twenty million instrumented branch visits.");
                }
                coverage.Add((id, method));
            };
            // Without an AFL environment, Run installs a pinned coverage map and
            // executes the action once. Mutation/selection stays in this test.
            Fuzzer.Run(_ =>
            {
                for (int i = 0; i < initial; i++)
                { for (int operation = 0; operation < 7; operation++) { Exercise(corpus[i], operation, -1 - i); } }
                for (int i = 0; i < iterations; i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    byte[] candidate = Mutate(corpus[random.Next(corpus.Count)], random);
                    int before = coverage.Count;
                    Exercise(candidate, i % 7, i);
                    if (coverage.Count > before)
                    {
                        if (corpus.Count == 512) { corpus.RemoveAt(random.Next(initial, corpus.Count)); }
                        corpus.Add(candidate);
                    }
                }
            });
            Assert.True(validPixels > 0, "The corpus never reached successful pixel decoding.");
            Assert.True(coverage.Count > 500, $"Only {coverage.Count} instrumented branches were visited.");
            output.WriteLine($"Seed={seed}; mutations={iterations}; calls={completed}; instrumentedTypes={types.Length}; branches={coverage.Count}; retainedInputs={corpus.Count}; successfulPixelCalls={validPixels}");

            void Exercise(byte[] bytes, int operation, int iteration)
            {
                string first = null;
                for (int stream = 0; stream < 2; stream++)
                {
                    steps = 0; exhausted = false; SharpFuzz.Common.Trace.PrevLocation = 0;
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    try
                    {
                        string result = run(bytes, operation, stream != 0, cancellation);
                        Assert.False(exhausted);
                        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                        Assert.True(allocated <= 128L << 20, $"Fuzz case allocated {allocated} managed bytes.");
                        if (stream == 0) { first = result; } else { Assert.Equal(first, result); }
                        if (result.StartsWith("pixels:", StringComparison.Ordinal)) { validPixels++; }
                        completed++;
                    }
                    catch (Exception error) when (!cancellation.IsCancellationRequested)
                    {
                        string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                        string failures = Path.Combine(AppContext.BaseDirectory, ".jxl-fuzz-failures"); Directory.CreateDirectory(failures);
                        string path = Path.Combine(failures, hash + ".bin"); File.WriteAllBytes(path, bytes);
                        Assert.Fail($"JXL fuzz seed={seed}, iteration={iteration}, operation={operation}, forwardOnly={stream != 0}, bytes={bytes.Length}, reproducer={path}\n{error}");
                    }
                }
            }
        }
        finally
        {
            SharpFuzz.Common.Trace.OnBranch = null;
            context?.Unload();
            Directory.Delete(directory, true);
        }
    }

    // SharpFuzz reads its immutable options once, on first use. Keep that use
    // behind a call boundary so the flag is set before options initialization.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string[] Instrument(string library)
    {
        Assert.True(Options.Value.EnableOnBranchCallback);
        return Fuzzer.Instrument(library, name => name.StartsWith("CodeBrix.Imaging.Formats.Jxl.", StringComparison.Ordinal)
            || name.StartsWith("CodeBrix.Imaging.ColorSpaces.Icc.", StringComparison.Ordinal), Options.Value).ToArray();
    }

    private static List<byte[]> Seeds()
    {
        List<byte[]> seeds = [[], [255, 10], JxlConstants.ContainerSignature.ToArray()];
        string root = Path.GetDirectoryName(JxlHeaderTests.ResourceRoot);
        string[] files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => (path.Contains("JxlConformance", StringComparison.Ordinal) || path.Contains("JxlSynthetic", StringComparison.Ordinal))
                && (path.EndsWith(".jxl", StringComparison.Ordinal) || path.EndsWith(".jpg", StringComparison.Ordinal)) && new FileInfo(path).Length <= 16384)
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        for (int i = 0; i < Math.Min(32, files.Length); i++) { seeds.Add(File.ReadAllBytes(files[i * (files.Length - 1) / Math.Max(1, Math.Min(32, files.Length) - 1)])); }
        using Image<Rgba32> image = new(17, 19);
        for (int y = 0; y < image.Height; y++)
        for (int x = 0; x < image.Width; x++)
        { image[x, y] = new((byte)(x * 13), (byte)(y * 11), (byte)(x * y), (byte)(255 - x * 7)); }
        foreach (bool lossless in new[] { true, false })
        foreach (bool progressive in new[] { false, true })
        {
            using MemoryStream encoded = new();
            new JxlEncoder { Lossless = lossless, Effort = 7, UseContainer = false,
                Progressive = progressive ? JxlProgressiveMode.Full : JxlProgressiveMode.None }.Encode(image, encoded);
            seeds.Add(encoded.ToArray());
        }
        return seeds;
    }

    private static byte[] Mutate(byte[] parent, Random random)
    {
        List<byte> bytes = new(parent);
        for (int edit = 0, count = random.Next(1, 5); edit < count; edit++)
        {
            int position = random.Next(bytes.Count + 1);
            if (bytes.Count == 0) { bytes.Add((byte)random.Next(256)); continue; }
            position = Math.Min(position, bytes.Count - 1);
            switch (random.Next(6))
            {
                case 0: bytes[position] ^= (byte)(1 << random.Next(8)); break;
                case 1: bytes[position] = (byte)random.Next(256); break;
                case 2: bytes.RemoveRange(position, random.Next(1, Math.Min(16, bytes.Count - position) + 1)); break;
                case 3: bytes.RemoveRange(position, bytes.Count - position); break;
                case 4:
                    int length = Math.Min(random.Next(1, 33), bytes.Count - position);
                    bytes.InsertRange(position, bytes.GetRange(position, length)); break;
                default:
                    byte[] field = random.Next(4) switch { 0 => [255, 255, 255, 255], 1 => [0, 0, 0, 0], 2 => [0, 0, 0, 1], _ => "jxlp"u8.ToArray() };
                    for (int i = 0; i < field.Length && position + i < bytes.Count; i++) { bytes[position + i] = field[i]; }
                    break;
            }
        }
        if (bytes.Count > 32768) { bytes.RemoveRange(32768, bytes.Count - 32768); }
        return bytes.ToArray();
    }

    private sealed class FuzzLoadContext(string library) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly Load(AssemblyName name)
        {
            if (name.Name != "CodeBrix.Imaging") { return null; }
            // Loading bytes leaves no DLL file handle behind on Windows.
            using MemoryStream bytes = new(File.ReadAllBytes(library), false);
            return this.LoadFromStream(bytes);
        }
    }
}
