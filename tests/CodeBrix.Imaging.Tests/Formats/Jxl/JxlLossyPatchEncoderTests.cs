// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Container;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlLossyPatchEncoderTests
{
    [Theory]
    [InlineData(5, JxlProgressiveMode.None)] [InlineData(7, JxlProgressiveMode.Ac)] [InlineData(8, JxlProgressiveMode.Full)]
    public void repeated_text_creates_an_additive_atlas_and_preserves_alpha(int effort, JxlProgressiveMode progressive)
    {
        using Image<Rgba32> source = Source(); using MemoryStream stream = Encode(source, effort, progressive);
        Assert.Equal(2U, FirstFrame(stream).FrameType); stream.Position = 0;
        using Image<Rgba32> decoded = Image.Load<Rgba32>(stream);
        Assert.Equal(source.Width, decoded.Width); Assert.Equal(source.Height, decoded.Height);
        int peak = 0;
        for (int y = 0; y < source.Height; y++) { for (int x = 0; x < source.Width; x++)
        { Assert.Equal(source[x, y].A, decoded[x, y].A); peak = Math.Max(peak, Math.Abs(source[x, y].R - decoded[x, y].R)); } }
        Assert.True(peak <= 20, $"Text reconstruction peak error {peak}");
    }

    [Fact]
    public void disabling_automatic_patches_writes_the_display_frame_first()
    {
        using Image<Rgba32> source = Source(); using MemoryStream stream = Encode(source, 7, JxlProgressiveMode.None, false);
        Assert.Equal(0U, FirstFrame(stream).FrameType);
    }

    [Theory]
    [InlineData(7, JxlProgressiveMode.None)] [InlineData(8, JxlProgressiveMode.Full)]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_agrees_with_automatically_discovered_text_patches(int effort, JxlProgressiveMode progressive)
    {
        JxlExternalResources.RequireEnabled(); using Image<Rgba32> source = Source(); using MemoryStream encoded = Encode(source, effort, progressive);
        Assert.Equal(2U, FirstFrame(encoded).FrameType);
        Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken));
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-text-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.jxl"), output = Path.Combine(directory, "output.png");
            await File.WriteAllBytesAsync(input, encoded.ToArray(), TestContext.Current.CancellationToken);
            await JxlNativeReference.RunAsync("djxl", [input, output, "--bits_per_sample=16", "--num_threads=1"], TestContext.Current.CancellationToken);
            encoded.Position = 0; using Image<Rgba64> managed = Image.Load<Rgba64>(encoded), native = Image.Load<Rgba64>(output);
            int peak = 0;
            for (int y = 0; y < managed.Height; y++) { for (int x = 0; x < managed.Width; x++)
            {
                Rgba64 a = managed[x, y], b = native[x, y]; Assert.Equal(a.A, b.A);
                peak = Math.Max(peak, Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B))));
            } }
            Assert.True(peak <= 8, $"Automatic-patch native pixel peak difference {peak}");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static Image<Rgba32> Source()
    {
        Image<Rgba32> result = new(256, 129);
        for (int y = 0; y < result.Height; y++)
        {
            for (int x = 0; x < result.Width; x++)
            {
                int px = x % 16, py = y % 16;
                bool ink = py is >= 2 and <= 11 && (px == 2 || (px is >= 2 and <= 8 && py is 2 or 6 or 11));
                byte value = ink ? (byte)25 : (byte)230;
                result[x, y] = new(value, value, value, (byte)(x * 13 + y * 7));
            }
        }
        return result;
    }

    private static MemoryStream Encode(Image<Rgba32> source, int effort, JxlProgressiveMode progressive, bool? patches = null)
    {
        MemoryStream stream = new();
        try { new JxlEncoder { Effort = effort, Distance = .3f, Progressive = progressive, Patches = patches }.Encode(source, stream); stream.Position = 0; return stream; }
        catch { stream.Dispose(); throw; }
    }

    private static JxlFrameHeader FirstFrame(MemoryStream encoded)
    {
        encoded.Position = 0;
        using JxlCodestreamStream codestream = new(encoded, 1 << 20, false, TestContext.Current.CancellationToken);
        JxlBitReader reader = new(codestream, TestContext.Current.CancellationToken);
        JxlImageHeader header = JxlImageHeader.Read(reader, 1 << 20, 1 << 20);
        return JxlFrameHeader.Read(reader, header, 1 << 20, 1 << 20);
    }
}
