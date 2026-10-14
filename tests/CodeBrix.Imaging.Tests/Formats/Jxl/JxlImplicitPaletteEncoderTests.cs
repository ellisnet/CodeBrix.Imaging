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

public class JxlImplicitPaletteEncoderTests
{
    [Theory]
    [InlineData(8, 1, false)] [InlineData(8, 2, true)]
    [InlineData(16, 1, true)] [InlineData(16, 2, false)]
    [InlineData(31, 1, false)] [InlineData(31, 2, true)]
    public void implicit_cubes_and_explicit_colors_preserve_original_integer_samples(int depth, int order, bool mixed)
    {
        byte[] encoded = Encode(depth, order, mixed, out int[][] source);
        using MemoryStream input = new(encoded);
        using JxlRawImage raw = new JxlDecoder().DecodeRaw(Configuration.Default, input, TestContext.Current.CancellationToken);
        for (int c = 0; c < source.Length; c++)
        {
            for (int y = 0; y < 9; y++)
            { Assert.True(source[c].AsSpan(y * 21, 21).SequenceEqual(raw.Frames[0].Planes[c].GetIntegerRowSpan(y))); }
        }
    }

    [Theory]
    [InlineData(1, false)] [InlineData(2, true)]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_agrees_with_zero_and_nonzero_explicit_palette_sizes(int order, bool mixed)
    {
        JxlExternalResources.RequireEnabled();
        Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken));
        byte[] bytes = Encode(16, order, mixed, out int[][] source);
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-implicit-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.jxl"), output = Path.Combine(directory, "output.png");
            await File.WriteAllBytesAsync(input, bytes, TestContext.Current.CancellationToken);
            await JxlNativeReference.RunAsync("djxl", [input, output, "--bits_per_sample=16", "--num_threads=1"], TestContext.Current.CancellationToken);
            using Image<Rgba64> actual = Image.Load<Rgba64>(output);
            for (int y = 0; y < 9; y++)
            {
                for (int x = 0; x < 21; x++)
                {
                    int p = y * 21 + x;
                    Assert.Equal(new Rgba64((ushort)source[0][p], (ushort)source[1][p], (ushort)source[2][p], (ushort)source[3][p]), actual[x, y]);
                }
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(2)] [InlineData(5)] [InlineData(7)] [InlineData(10)]
    public void public_encoder_searches_palettes_before_color_transform(int effort)
    {
        using Image<Rgba32> source = new(273, 9);
        for (int y = 0; y < source.Height; y++)
        { for (int x = 0; x < source.Width; x++) { int p = (x + 3 * y) % 189; source[x, y] = new((byte)Value(p, 0, 8), (byte)Value(p, 1, 8), (byte)Value(p, 2, 8), (byte)Value(p, 3, 8)); } }
        using MemoryStream output = new(); new JxlEncoder { Lossless = true, Effort = effort }.Encode(source, output);
        output.Position = 0; using Image<Rgba32> actual = Image.Load<Rgba32>(output);
        for (int y = 0; y < source.Height; y++)
        { Assert.True(source.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y).SequenceEqual(actual.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y))); }
    }

    private static byte[] Encode(int depth, int order, bool mixed, out int[][] source)
    {
        var allocator = Configuration.Default.MemoryAllocator;
        using JxlModularWorkspace workspace = new(allocator, 65536);
        JxlModularPlane[] planes = new JxlModularPlane[4]; source = new int[4][];
        for (int c = 0; c < planes.Length; c++)
        {
            planes[c] = workspace.Allocate(21, 9); source[c] = new int[189];
            for (int i = 0; i < 189; i++)
            {
                int value = mixed && i < 25 ? 7 + c : Value(i, c, depth);
                planes[c].Set(i % 21, i / 21, value); source[c][i] = value;
            }
        }
        JxlPaletteEncoder palette = JxlPaletteEncoder.TryCreate(planes, 4, 1, workspace, TestContext.Current.CancellationToken,
            order: (JxlPaletteOrder)order, bitDepth: depth);
        Assert.NotNull(palette); Assert.Equal(mixed ? 1 : 0, palette.ExplicitColors);
        using MemoryStream payload = new(); JxlBitWriter writer = new(payload); writer.WriteBool(true); writer.WriteBool(false);
        JxlModularEncoder.WriteStreamCore(writer, palette.Planes, 0, palette, null, false, 7, allocator, TestContext.Current.CancellationToken, depth);
        writer.ZeroPadToByte();
        JxlImageHeader header = new() { Size = new(21, 9), XybEncoded = false, BitDepth = new((uint)depth, 0),
            Modular16BitBuffers = depth <= 12, ExtraChannels = [new() { Type = 0, BitDepth = new((uint)depth, 0) }] };
        JxlFrameHeader frame = new() { Modular = true, Width = 21, Height = 9, ExtraUpsampling = [1], ExtraBlending = [default],
            Restoration = new() { GaborEnabled = false, EpfIterations = 0 } };
        using MemoryStream stream = new(); JxlBitWriter output = new(stream); header.Write(output); frame.Write(output, header);
        JxlFrameToc.WriteOrdered(output, frame, [(uint)payload.Length]); output.WriteBytes(payload.ToArray()); return stream.ToArray();
    }

    private static int Value(int index, int channel, int depth)
    {
        if (channel >= 3) { return 0; }
        long maximum = (1L << depth) - 1;
        if (index < 64) { return (int)((((index >> (2 * channel)) & 3) * maximum / 4) + (1L << Math.Max(0, depth - 3))); }
        index -= 64; for (int c = 0; c < channel; c++) { index /= 5; }
        return (int)((index % 5) * maximum / 4);
    }
}
