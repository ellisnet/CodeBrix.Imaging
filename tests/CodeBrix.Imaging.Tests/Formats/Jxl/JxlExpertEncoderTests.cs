// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
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

public class JxlExpertEncoderTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void every_expert_configuration_preserves_integer_samples_and_tree_restrictions(bool morePalette)
    {
        HashSet<JxlModularSearchOptions> options = new(JxlModularSearchOptions.Candidates(morePalette));
        foreach (JxlModularSearchOptions search in options)
        {
            byte[] bytes = Forced(search);
            using MemoryStream input = new(bytes); using Image<Rgba64> actual = Image.Load<Rgba64>(input);
            Verify(actual);
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void public_expert_search_preserves_pixels_and_cannot_exceed_effort_ten_size(bool palette)
    {
        using Image<Rgba64> source = new(131, 9);
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            { source[x, y] = palette ? Pixel(x % 5, 0) : Pixel(x, y); }
        }
        byte[] ordinary = EncodePublic(source, 10), expert = EncodePublic(source, 11);
        Assert.True(expert.Length <= ordinary.Length, $"Effort 11: {expert.Length}; effort 10: {ordinary.Length}");
        using MemoryStream input = new(expert); using Image<Rgba64> actual = Image.Load<Rgba64>(input);
        for (int y = 0; y < source.Height; y++)
        { Assert.True(source.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y).SequenceEqual(actual.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y))); }
    }

    [Theory]
    [InlineData(0, 0, true)] [InlineData(4, 1, false)] [InlineData(6, .95f, false)]
    [InlineData(14, .9f, true)] [InlineData(15, .5f, true)]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_agrees_with_expert_predictor_and_sampling_variants(int predictor, float sampling, bool noWeighted)
    {
        JxlExternalResources.RequireEnabled();
        Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken));
        byte[] bytes = Forced(new(Predictor: predictor, Sampling: sampling, NoWeighted: noWeighted));
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-expert-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.jxl"), output = Path.Combine(directory, "output.png");
            await File.WriteAllBytesAsync(input, bytes, TestContext.Current.CancellationToken);
            await JxlNativeReference.RunAsync("djxl", [input, output, "--bits_per_sample=16", "--num_threads=1"], TestContext.Current.CancellationToken);
            using Image<Rgba64> actual = Image.Load<Rgba64>(output); Verify(actual);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static byte[] Forced(JxlModularSearchOptions search)
    {
        var allocator = Configuration.Default.MemoryAllocator;
        using JxlModularWorkspace workspace = new(allocator, 65536);
        JxlModularPlane[] planes = [workspace.Allocate(23, 9), workspace.Allocate(23, 9), workspace.Allocate(23, 9), workspace.Allocate(23, 9)];
        for (int y = 0; y < 9; y++)
        {
            for (int x = 0; x < 23; x++)
            { Rgba64 p = Pixel(x, y); planes[0].Set(x, y, p.R); planes[1].Set(x, y, p.G); planes[2].Set(x, y, p.B); planes[3].Set(x, y, p.A); }
        }
        using MemoryStream payload = new(); JxlBitWriter writer = new(payload); writer.WriteBool(true); writer.WriteBool(false);
        JxlModularEncoder.WriteStreamCore(writer, planes, 0, null, null, false, 11, allocator, TestContext.Current.CancellationToken, 16, search: search);
        writer.ZeroPadToByte();
        payload.Position = 0; JxlBitReader reader = new(payload, TestContext.Current.CancellationToken);
        Assert.True(reader.ReadBool()); Assert.False(reader.ReadBool()); Assert.False(reader.ReadBool());
        JxlWeightedPredictorHeader.Read(reader); Assert.Equal(0U, reader.ReadU32(0, 1, JxlU32.U(4, 2), JxlU32.U(8, 18)));
        JxlMaTree tree = JxlMaTree.Read(reader, allocator, 10000);
        foreach (JxlMaTree.Node node in tree.Nodes)
        {
            if (search.NoWeighted)
            { Assert.False(node.Cluster < 0 && node.Property == 15); Assert.False(node.Cluster >= 0 && node.Predictor == 6); }
            if (search.Predictor <= 13 && node.Cluster >= 0) { Assert.Equal(search.Predictor, node.Predictor); }
        }
        tree.Distributions.Dispose();
        JxlImageHeader header = new() { Size = new(23, 9), XybEncoded = false, BitDepth = new(16, 0), Modular16BitBuffers = false,
            ExtraChannels = [new() { Type = 0, BitDepth = new(16, 0) }] };
        JxlFrameHeader frame = new() { Modular = true, Width = 23, Height = 9, ExtraUpsampling = [1], ExtraBlending = [default],
            Restoration = new() { GaborEnabled = false, EpfIterations = 0 } };
        using MemoryStream stream = new(); JxlBitWriter output = new(stream); header.Write(output); frame.Write(output, header);
        JxlFrameToc.WriteOrdered(output, frame, [(uint)payload.Length]); output.WriteBytes(payload.ToArray()); return stream.ToArray();
    }

    private static byte[] EncodePublic(Image<Rgba64> source, int effort)
    { using MemoryStream stream = new(); new JxlEncoder { Lossless = true, Effort = effort, AllowExpertEffort = true }.Encode(source, stream); return stream.ToArray(); }

    private static Rgba64 Pixel(int x, int y) => new((ushort)(x * 317 + y * 53), (ushort)(x * x * 91 + y * 831),
        (ushort)((x ^ y) * 1971), x % 5 == 0 ? (ushort)0 : (ushort)(x * 107 + y * 799));

    private static void Verify(Image<Rgba64> actual)
    {
        Assert.Equal((23, 9), (actual.Width, actual.Height));
        for (int y = 0; y < 9; y++) { for (int x = 0; x < 23; x++) { Assert.Equal(Pixel(x, y), actual[x, y]); } }
    }
}
