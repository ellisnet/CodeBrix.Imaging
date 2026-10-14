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

public class JxlGlobalModularEncoderTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void shared_tree_and_histograms_preserve_group_and_palette_samples(bool palette, bool prefix)
    {
        byte[] bytes = EncodeGlobal(palette, prefix);
        using MemoryStream input = new(bytes, false); using Image<Rgba64> image = Image.Load<Rgba64>(input);
        Verify(image, 263, 17);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    [Trait("Category", "JxlNativeReference")]
    public async Task pinned_djxl_reads_shared_global_trees_and_palettes(bool palette, bool prefix)
    {
        JxlExternalResources.RequireEnabled();
        await Native(EncodeGlobal(palette, prefix), 263, 17);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void each_weighted_predictor_preset_round_trips(int mode)
    {
        using MemoryStream input = new(EncodeWeighted(mode), false); using Image<Rgba64> image = Image.Load<Rgba64>(input);
        Verify(image, 65, 33);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [Trait("Category", "JxlNativeReference")]
    public async Task pinned_djxl_reads_each_weighted_predictor_preset(int mode)
    {
        JxlExternalResources.RequireEnabled();
        await Native(EncodeWeighted(mode), 65, 33);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void independent_channel_palettes_share_a_global_tree(bool prefix)
    {
        using MemoryStream input = new(EncodeGlobal(true, prefix, independent: true), false);
        using Image<Rgba64> image = Image.Load<Rgba64>(input); Verify(image, 263, 17);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    [Trait("Category", "JxlNativeReference")]
    public async Task pinned_djxl_reads_independent_channel_palettes(bool prefix)
    {
        JxlExternalResources.RequireEnabled();
        await Native(EncodeGlobal(true, prefix, independent: true), 263, 17);
    }

    private static byte[] EncodeGlobal(bool palette, bool prefix, bool independent = false)
    {
        const int width = 263, height = 17;
        using JxlModularWorkspace workspace = new(Configuration.Default.MemoryAllocator, 262144);
        JxlModularPlane[] planes = Planes(workspace, width, height);
        JxlRctEncoder.Apply(planes, 6, TestContext.Current.CancellationToken);
        JxlPaletteEncoder transform = independent ? JxlPaletteEncoder.TryIndependent(planes, 1024, workspace, TestContext.Current.CancellationToken) :
            palette ? JxlPaletteEncoder.TryCreate(planes, 4, 1024, workspace, TestContext.Current.CancellationToken) : null;
        if (palette) { Assert.NotNull(transform); planes = transform.Planes; }
        JxlImageHeader image = Header(width, height); JxlFrameHeader frame = Frame(width, height);
        byte[][] payloads = JxlModularEncoder.GlobalPayloads(image, frame, planes, 6, transform, prefix, 10, Configuration.Default.MemoryAllocator, TestContext.Current.CancellationToken);
        return Finish(image, frame, payloads);
    }

    private static byte[] EncodeWeighted(int mode)
    {
        const int width = 65, height = 33;
        using JxlModularWorkspace workspace = new(Configuration.Default.MemoryAllocator, 262144);
        JxlModularPlane[] planes = Planes(workspace, width, height);
        JxlWeightedPredictorHeader weighted = JxlWeightedPredictorEncoder.Preset(mode);
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream);
        writer.WriteBool(true); writer.WriteBool(false); writer.WriteBool(false); weighted.Write(writer);
        writer.WriteBits(2, 0); // No transforms.
        JxlMaEncoder tree = JxlMaEncoder.Learn(planes, 3, weighted, Configuration.Default.MemoryAllocator, TestContext.Current.CancellationToken, 16);
        tree.Write(writer, planes, weighted, Configuration.Default.MemoryAllocator, false, 3, TestContext.Current.CancellationToken);
        writer.ZeroPadToByte(); return Finish(Header(width, height), Frame(width, height), [stream.ToArray()]);
    }

    private static JxlModularPlane[] Planes(JxlModularWorkspace workspace, int width, int height)
    {
        JxlModularPlane[] planes = [workspace.Allocate(width, height), workspace.Allocate(width, height), workspace.Allocate(width, height), workspace.Allocate(width, height)];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Rgba64 pixel = Sample(x, y);
                planes[0].Set(x, y, pixel.R); planes[1].Set(x, y, pixel.G); planes[2].Set(x, y, pixel.B); planes[3].Set(x, y, pixel.A);
            }
        }
        return planes;
    }

    private static Rgba64 Sample(int x, int y)
    { int v = (x + y) % 41; return new((ushort)(v * 997), (ushort)(v * 1139), (ushort)(v * 1237), (ushort)(v % 3 == 0 ? 0 : 65535)); }

    private static JxlImageHeader Header(int width, int height) => new() { Size = new((uint)width, (uint)height), XybEncoded = false,
        BitDepth = new(16, 0), Modular16BitBuffers = false, ExtraChannels = [new() { Type = 0, BitDepth = new(16, 0) }] };
    private static JxlFrameHeader Frame(int width, int height) => new() { Width = (uint)width, Height = (uint)height, Modular = true,
        ExtraUpsampling = [1], ExtraBlending = [default], Restoration = new() { GaborEnabled = false, EpfIterations = 0 } };

    private static byte[] Finish(JxlImageHeader image, JxlFrameHeader frame, byte[][] payloads)
    {
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream); image.Write(writer); frame.Write(writer, image);
        uint[] sizes = new uint[payloads.Length]; for (int i = 0; i < sizes.Length; i++) { sizes[i] = (uint)payloads[i].Length; }
        JxlFrameToc.WriteOrdered(writer, frame, sizes); foreach (byte[] payload in payloads) { writer.WriteBytes(payload); }
        return stream.ToArray();
    }

    private static void Verify(Image<Rgba64> image, int width, int height)
    {
        Assert.Equal(width, image.Width); Assert.Equal(height, image.Height);
        for (int y = 0; y < height; y++) { for (int x = 0; x < width; x++) { Assert.Equal(Sample(x, y), image[x, y]); } }
    }

    private static async Task Native(byte[] bytes, int width, int height)
    {
        JxlExternalResources.RequireEnabled();
        Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken));
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-global-encode-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.jxl"), output = Path.Combine(directory, "decoded.png");
            await File.WriteAllBytesAsync(input, bytes, TestContext.Current.CancellationToken);
            await JxlNativeReference.RunAsync("djxl", [input, output, "--bits_per_sample=16", "--num_threads=1"], TestContext.Current.CancellationToken);
            using Image<Rgba64> image = Image.Load<Rgba64>(output); Verify(image, width, height);
        }
        finally { Directory.Delete(directory, true); }
    }
}
