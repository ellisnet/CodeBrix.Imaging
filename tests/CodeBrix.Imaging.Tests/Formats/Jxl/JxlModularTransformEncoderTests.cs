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
using static CodeBrix.Imaging.Formats.Jxl.Bitstream.JxlU32;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlModularTransformEncoderTests
{
    [Theory]
    [InlineData(8)] [InlineData(16)] [InlineData(31)]
    public void all_42_reversible_color_transforms_preserve_samples(int bits)
    {
        int[][] values = Samples(17, 11, bits, false);
        for (int rct = 0; rct < 42; rct++) { Verify(Encode("rct", rct, 17, 11, bits, values), values, 17, 11); }
    }

    [Theory]
    [InlineData("palette", 8, 17, 11)]
    [InlineData("palette", 31, 33, 17)]
    [InlineData("squeeze", 8, 17, 11)]
    [InlineData("squeeze", 16, 32, 24)]
    [InlineData("squeeze", 31, 33, 17)]
    [InlineData("palette-squeeze", 16, 33, 17)]
    [InlineData("rct-palette-squeeze", 16, 33, 17)]
    [InlineData("channel-palettes-squeeze", 16, 33, 17)]
    public void explicit_palette_and_squeeze_streams_preserve_odd_edges_and_high_precision(string transform, int bits, int width, int height)
    {
        int[][] values = Samples(width, height, bits, transform.Contains("palette", StringComparison.Ordinal));
        Verify(Encode(transform, 13, width, height, bits, values), values, width, height);
    }

    [Theory]
    [InlineData("rct", 0)] [InlineData("rct", 13)] [InlineData("rct", 35)] [InlineData("rct", 41)]
    [InlineData("palette", 0)] [InlineData("squeeze", 0)]
    [InlineData("palette-squeeze", 0)] [InlineData("rct-palette-squeeze", 41)] [InlineData("channel-palettes-squeeze", 0)]
    [Trait("Category", "JxlNativeReference")]
    public async Task pinned_djxl_agrees_with_forced_encoder_transforms(string transform, int rct)
    {
        JxlExternalResources.RequireEnabled();
        string version = await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken);
        Assert.Contains("ef67fde2", version);
        const int width = 33, height = 17;
        int[][] values = Samples(width, height, 16, transform.Contains("palette", StringComparison.Ordinal));
        byte[] bytes = Encode(transform, rct, width, height, 16, values);
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-transform-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.jxl"), output = Path.Combine(directory, "decoded.png");
            await File.WriteAllBytesAsync(input, bytes, TestContext.Current.CancellationToken);
            await JxlNativeReference.RunAsync("djxl", [input, output, "--bits_per_sample=16", "--num_threads=1"], TestContext.Current.CancellationToken);
            using Image<Rgba64> actual = Image.Load<Rgba64>(output);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = (y * width) + x;
                    Assert.Equal(new Rgba64((ushort)values[0][index], (ushort)values[1][index], (ushort)values[2][index], (ushort)values[3][index]), actual[x, y]);
                }
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    private static void Verify(byte[] encoded, int[][] expected, int width, int height)
    {
        using MemoryStream stream = new(encoded, false);
        using JxlRawImage decoded = new JxlDecoder { Coalesce = false }.DecodeRaw(Configuration.Default, stream, TestContext.Current.CancellationToken);
        for (int c = 0; c < expected.Length; c++)
        {
            for (int y = 0; y < height; y++)
            { Assert.True(expected[c].AsSpan(y * width, width).SequenceEqual(decoded.Frames[0].Planes[c].GetIntegerRowSpan(y))); }
        }
    }

    private static int[][] Samples(int width, int height, int bits, bool palette)
    {
        Random random = new(459173); int[][] values = new int[4][];
        for (int c = 0; c < 4; c++)
        {
            values[c] = new int[width * height];
            for (int i = 0; i < values[c].Length; i++)
            { values[c][i] = palette ? (int)(((i % 7) * (1L << bits) / 7 + (c * 13)) & ((1L << bits) - 1)) : (int)random.NextInt64(0, 1L << bits); }
            values[c][0] = 0; values[c][^1] = (int)((1L << bits) - 1);
        }
        return values;
    }

    private static byte[] Encode(string transform, int rct, int width, int height, int bits, int[][] values)
    {
        var allocator = Configuration.Default.MemoryAllocator;
        using JxlModularWorkspace workspace = new(allocator, (long)width * height);
        JxlModularPlane[] planes = new JxlModularPlane[values.Length];
        for (int c = 0; c < planes.Length; c++)
        {
            planes[c] = workspace.Allocate(width, height);
            for (int y = 0; y < height; y++) { values[c].AsSpan(y * width, width).CopyTo(planes[c].Row(y)); }
        }
        using MemoryStream payload = new(); JxlBitWriter writer = new(payload);
        writer.WriteBool(true); writer.WriteBool(false); // Default dequant, local MA tree.
        bool color = transform.Contains("rct", StringComparison.Ordinal);
        JxlPaletteEncoder palette = null; JxlSqueezeEncoder squeeze = null;
        if (color)
        {
            JxlRctEncoder.Apply(planes, rct, TestContext.Current.CancellationToken);
        }
        if (transform.Contains("palette", StringComparison.Ordinal))
        {
            palette = transform.StartsWith("channel-", StringComparison.Ordinal)
                ? JxlPaletteEncoder.TryIndependent(planes, 256, workspace, TestContext.Current.CancellationToken)
                : JxlPaletteEncoder.TryCreate(planes, 4, 256, workspace, TestContext.Current.CancellationToken);
            Assert.NotNull(palette); planes = palette.Planes;
        }
        if (transform.Contains("squeeze", StringComparison.Ordinal))
        {
            squeeze = JxlSqueezeEncoder.Create(planes, 3, workspace, TestContext.Current.CancellationToken, palette?.MetaChannels ?? 0);
            planes = squeeze.Planes;
        }
        JxlModularEncoder.WriteStreamCore(writer, planes, color ? rct : 0, palette, squeeze, false, 7, allocator, TestContext.Current.CancellationToken, bits);
        writer.ZeroPadToByte();
        JxlImageHeader image = new() { Size = new((uint)width, (uint)height), XybEncoded = false,
            BitDepth = new((uint)bits, 0), Modular16BitBuffers = bits <= 12, ExtraChannels = [new() { Type = 0, BitDepth = new((uint)bits, 0) }] };
        JxlFrameHeader frame = new() { Modular = true, Width = (uint)width, Height = (uint)height,
            ExtraUpsampling = [1], ExtraBlending = [default], Restoration = new() { GaborEnabled = false, EpfIterations = 0 } };
        using MemoryStream result = new(); JxlBitWriter output = new(result); image.Write(output); frame.Write(output, image);
        JxlFrameToc.WriteOrdered(output, frame, [(uint)payload.Length]); output.WriteBytes(payload.ToArray()); return result.ToArray();
    }
}
