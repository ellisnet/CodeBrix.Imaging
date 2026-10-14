// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Modular;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlFasterDecodingTests
{
    [Theory]
    [InlineData(1, 1)] [InlineData(2, 3)] [InlineData(3, 7)] [InlineData(4, 10)]
    [InlineData(1, 10)] [InlineData(2, 11)] [InlineData(4, 11)]
    public void lossless_speed_settings_preserve_samples_and_select_smaller_groups(int speed, int effort)
    {
        using Image<Rgba64> source = Make(effort == 11 ? 19 : 259, 17);
        using MemoryStream encoded = new();
        new JxlEncoder { Lossless = true, FasterDecoding = speed, Effort = effort, AllowExpertEffort = effort == 11, UseContainer = false }.Encode(source, encoded);
        JxlFrameHeader frame = Header(encoded);
        if (speed >= 2) { Assert.Equal(0U, frame.GroupSizeShift); }
        encoded.Position = 0; using Image<Rgba64> decoded = Image.Load<Rgba64>(encoded);
        for (int y = 0; y < source.Height; y++)
        { Assert.True(source.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y).SequenceEqual(decoded.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y))); }
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void lossy_speed_settings_reduce_filter_work_and_keep_alpha_exact(int speed)
    {
        using Image<Rgba64> source = Make(259, 17);
        foreach (float distance in new[] { 1f, 3f, 5f })
        {
            using MemoryStream encoded = new();
            new JxlEncoder { FasterDecoding = speed, Effort = 8, Distance = distance, Progressive = JxlProgressiveMode.Ac, UseContainer = false }.Encode(source, encoded);
            JxlFrameHeader frame = Header(encoded);
            Assert.Equal(speed < 4, frame.Restoration.GaborEnabled);
            Assert.Equal(speed >= 3 ? 0U : (uint)((distance == 5 ? 3 : distance == 3 ? 2 : 1) - (speed == 2 ? 1 : 0)), frame.Restoration.EpfIterations);
            encoded.Position = 0; using Image<Rgba64> decoded = Image.Load<Rgba64>(encoded);
            for (int y = 0; y < source.Height; y++)
            { for (int x = 0; x < source.Width; x++) { Assert.Equal(source[x, y].A, decoded[x, y].A); } }
        }
    }

    [Theory]
    [InlineData(1, 3)] [InlineData(1, 10)] [InlineData(2, 7)] [InlineData(3, 10)] [InlineData(4, 11)]
    public void modular_trees_obey_predictor_and_histogram_restrictions(int speed, int effort)
    {
        var allocator = Configuration.Default.MemoryAllocator; var cancellation = TestContext.Current.CancellationToken;
        using JxlModularWorkspace workspace = new(allocator, 8192);
        JxlModularPlane[] planes = [workspace.Allocate(47, 29), workspace.Allocate(47, 29), workspace.Allocate(47, 29)];
        for (int c = 0; c < 3; c++)
        { for (int y = 0; y < 29; y++) { for (int x = 0; x < 47; x++) { planes[c].Set(x, y, ((x * 173 + y * 97 + c * 31) ^ (x * y)) & 65535); } } }
        JxlModularSearchOptions search = JxlModularSearchOptions.ForDecodingSpeed(null, speed, effort, 1);
        JxlWeightedPredictorHeader weighted = new();
        JxlMaEncoder encoder = JxlMaEncoder.Learn(planes, effort, weighted, allocator, cancellation, 16, search);
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream);
        encoder.Write(writer, planes, weighted, allocator, false, effort, cancellation, search.MaximumHistograms); writer.ZeroPadToByte(); stream.Position = 0;
        JxlMaTree tree = JxlMaTree.Read(new(stream, cancellation), allocator, 1 << 20);
        using (tree.Distributions)
        {
            Assert.False(tree.NeedsSelfCorrection);
            if (speed >= 3) { foreach (JxlMaTree.Node node in tree.Nodes) { if (node.Cluster >= 0) { Assert.Equal(5, node.Predictor); } } }
            if (speed == 4) { Assert.Single(tree.Nodes); }
            foreach (byte cluster in tree.Distributions.Clusters) { Assert.True(cluster < search.MaximumHistograms); }
        }
    }

    [Theory]
    [InlineData(1, false)] [InlineData(2, false)] [InlineData(3, false)] [InlineData(4, false)]
    [InlineData(1, true)] [InlineData(2, true)] [InlineData(3, true)] [InlineData(4, true)]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_agrees_for_every_speed_setting(int speed, bool lossless)
    {
        JxlExternalResources.RequireEnabled();
        Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken));
        using Image<Rgba64> source = Make(259, 17);
        using MemoryStream encoded = new();
        new JxlEncoder { Lossless = lossless, FasterDecoding = speed, Effort = 9, Distance = 3,
            Progressive = lossless ? JxlProgressiveMode.None : JxlProgressiveMode.Ac }.Encode(source, encoded);
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-faster-decoding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.jxl"), output = Path.Combine(directory, "output.png");
            await File.WriteAllBytesAsync(input, encoded.ToArray(), TestContext.Current.CancellationToken);
            await JxlNativeReference.RunAsync("djxl", [input, output, "--bits_per_sample=16", "--num_threads=1"], TestContext.Current.CancellationToken);
            encoded.Position = 0; using Image<Rgba64> managed = Image.Load<Rgba64>(encoded), native = Image.Load<Rgba64>(output);
            int peak = 0;
            for (int y = 0; y < source.Height; y++)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    Rgba64 a = managed[x, y], b = native[x, y]; Assert.Equal(source[x, y].A, a.A); Assert.Equal(a.A, b.A);
                    peak = Math.Max(peak, Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B))));
                    if (lossless) { Assert.Equal(source[x, y], b); }
                }
            }
            Assert.InRange(peak, 0, lossless ? 0 : 8);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static JxlFrameHeader Header(MemoryStream stream)
    {
        stream.Position = 0; JxlBitReader reader = new(stream, TestContext.Current.CancellationToken);
        JxlImageHeader image = JxlImageHeader.Read(reader, 1 << 24, 1 << 20);
        return JxlFrameHeader.Read(reader, image, 1 << 24, 1 << 20);
    }

    private static Image<Rgba64> Make(int width, int height)
    {
        Image<Rgba64> result = new(width, height);
        for (int y = 0; y < height; y++)
        { for (int x = 0; x < width; x++) { result[x, y] = new((ushort)(x * 173), (ushort)(y * 2911), (ushort)((x * 31 + y * 107) ^ (x * y)), (ushort)((x + y) * 419)); } }
        return result;
    }
}
