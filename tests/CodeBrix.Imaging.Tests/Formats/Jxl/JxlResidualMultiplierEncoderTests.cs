// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Entropy;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Modular;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlResidualMultiplierEncoderTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void shared_multiplier_uses_every_group_including_the_last_rare_sample(bool rare)
    {
        byte[] bytes = Encode(rare);
        using MemoryStream input = new(bytes); using Image<Rgb48> decoded = Image.Load<Rgb48>(input); Verify(decoded, rare);
        input.Position = 0; JxlBitReader reader = new(input, TestContext.Current.CancellationToken);
        JxlImageHeader image = JxlImageHeader.Read(reader, 100000, 100000);
        JxlFrameHeader frame = JxlFrameHeader.Read(reader, image, 100000, 100000);
        JxlFrameToc.Read(reader, frame, Configuration.Default.MemoryAllocator);
        Assert.True(reader.ReadBool()); Assert.True(reader.ReadBool());
        JxlMaTree tree = JxlMaTree.Read(reader, Configuration.Default.MemoryAllocator, 1000);
        Assert.Equal(rare ? 1U : 257U, Assert.Single(tree.Nodes).Multiplier); tree.Distributions.Dispose();
    }

    [Fact]
    public void minimum_signed_residual_uses_a_representable_exact_factor()
    {
        var allocator = Configuration.Default.MemoryAllocator;
        using JxlModularWorkspace workspace = new(allocator, 1000); JxlModularPlane plane = workspace.Allocate(3, 1);
        plane.Set(0, 0, int.MinValue); plane.Set(2, 0, int.MinValue);
        JxlWeightedPredictorHeader weighted = new();
        JxlMaEncoder encoder = JxlMaEncoder.Learn([plane], 11, weighted, allocator, TestContext.Current.CancellationToken, 32,
            new(Predictor: 0, Sampling: 0, NoWeighted: true));
        JxlToken[] tokens = encoder.CreateTokens([plane], weighted, allocator, TestContext.Current.CancellationToken);
        encoder.RefineMultipliers([tokens], TestContext.Current.CancellationToken);
        Assert.Equal(3U, tokens[0].Value); // Packed -2 times 2^30.
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream); encoder.WriteTree(writer, TestContext.Current.CancellationToken);
        JxlEntropyEncoder entropy = new(tokens, 1); entropy.WriteHeader(writer); writer.ZeroPadToByte();
        stream.Position = 0; JxlMaTree tree = JxlMaTree.Read(new(stream, TestContext.Current.CancellationToken), allocator, 1000);
        Assert.Equal(1U << 30, Assert.Single(tree.Nodes).Multiplier); tree.Distributions.Dispose();
    }

    [Fact]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_reads_a_shared_non_power_of_two_multiplier()
    {
        JxlExternalResources.RequireEnabled();
        Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken));
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-multiplier-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.jxl"), output = Path.Combine(directory, "output.png");
            await File.WriteAllBytesAsync(input, Encode(false), TestContext.Current.CancellationToken);
            await JxlNativeReference.RunAsync("djxl", [input, output, "--bits_per_sample=16", "--num_threads=1"], TestContext.Current.CancellationToken);
            using Image<Rgb48> actual = Image.Load<Rgb48>(output); Verify(actual, false);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static byte[] Encode(bool rare)
    {
        var allocator = Configuration.Default.MemoryAllocator;
        using JxlModularWorkspace workspace = new(allocator, 65536);
        JxlModularPlane[] planes = [workspace.Allocate(259, 9), workspace.Allocate(259, 9), workspace.Allocate(259, 9)];
        for (int y = 0; y < 9; y++)
        { for (int x = 0; x < 259; x++) { for (int c = 0; c < 3; c++) { planes[c].Set(x, y, Sample(x, y, c, rare)); } } }
        JxlImageHeader image = new() { Size = new(259, 9), XybEncoded = false, BitDepth = new(16, 0), Modular16BitBuffers = false };
        JxlFrameHeader frame = new() { Modular = true, Width = 259, Height = 9, Restoration = new() { GaborEnabled = false, EpfIterations = 0 } };
        byte[][] payloads = JxlModularEncoder.GlobalPayloads(image, frame, planes, 0, null, false, 10, allocator, TestContext.Current.CancellationToken,
            search: new(Predictor: 0, Sampling: 0, NoWeighted: true));
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream); image.Write(writer); frame.Write(writer, image);
        uint[] sizes = new uint[payloads.Length]; for (int i = 0; i < sizes.Length; i++) { sizes[i] = (uint)payloads[i].Length; }
        JxlFrameToc.WriteOrdered(writer, frame, sizes); foreach (byte[] payload in payloads) { writer.WriteBytes(payload); }
        return stream.ToArray();
    }

    private static int Sample(int x, int y, int c, bool rare) => rare && x == 258 && y == 8 && c == 2 ? 12345 : ((x * 11 + y * 3 + c) & 255) * 257;
    private static void Verify(Image<Rgb48> image, bool rare)
    {
        for (int y = 0; y < 9; y++)
        { for (int x = 0; x < 259; x++) { Assert.Equal(new Rgb48((ushort)Sample(x, y, 0, rare), (ushort)Sample(x, y, 1, rare), (ushort)Sample(x, y, 2, rare)), image[x, y]); } }
    }
}
