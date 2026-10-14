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

public class JxlLossyModularEncoderTests
{
    [Theory]
    [InlineData(1, 1, 1)] [InlineData(7, 13, 3)] [InlineData(263, 259, 7)]
    [InlineData(2057, 9, 10)] [InlineData(9, 2057, 7)]
    public void progressive_squeeze_preserves_final_pixels_and_exact_alpha(int width, int height, int effort)
    {
        using Image<Rgba64> source = Make(width, height);
        using MemoryStream plain = Encode(source, effort, false), progressive = Encode(source, effort, true);
        using JxlCodestreamStream codestream = new(progressive, 1 << 20, false, TestContext.Current.CancellationToken);
        JxlBitReader reader = new(codestream, TestContext.Current.CancellationToken);
        JxlImageHeader header = JxlImageHeader.Read(reader, 1 << 24, 1 << 20);
        JxlFrameHeader frame = JxlFrameHeader.Read(reader, header, 1 << 24, 1 << 20);
        Assert.True(header.XybEncoded); Assert.True(frame.Modular); Assert.Equal(3U, frame.PassCount);
        progressive.Position = 0;
        using Image<Rgba64> a = Image.Load<Rgba64>(plain), b = Image.Load<Rgba64>(progressive);
        for (int y = 0; y < height; y++)
        {
            Assert.True(a.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y).SequenceEqual(b.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y)));
            for (int x = 0; x < width; x++) { Assert.Equal(source[x, y].A, a[x, y].A); }
        }
    }

    [Fact]
    public void greater_distance_trades_color_accuracy_for_smaller_size()
    {
        using Image<Rgba64> source = Make(131, 97);
        using MemoryStream fine = Encode(source, 7, false, distance: .05f), coarse = Encode(source, 7, false, distance: 3);
        Assert.True(coarse.Length < fine.Length);
        using Image<Rgba64> a = Image.Load<Rgba64>(fine), b = Image.Load<Rgba64>(coarse);
        double fineError = 0, coarseError = 0;
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                fineError += System.Numerics.Vector4.DistanceSquared(source[x, y].ToVector4(), a[x, y].ToVector4());
                coarseError += System.Numerics.Vector4.DistanceSquared(source[x, y].ToVector4(), b[x, y].ToVector4());
            }
        }
        Assert.True(fineError < coarseError);
    }

    [Theory]
    [InlineData(1, 1, 1, 1, 0, 0)] [InlineData(7, 13, 7, 1, 1, 0)]
    [InlineData(263, 259, 7, 1, 2, 0)] [InlineData(2057, 9, 10, 1, 4, 0)]
    [InlineData(263, 19, 7, 2, 3, 0)] [InlineData(263, 19, 10, 4, 0, 1000000)]
    [InlineData(263, 19, 7, 8, 4, 0)]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_agrees_at_group_boundaries_and_with_resampling_and_noise(int width, int height, int effort, int factor, int speed, int noise)
    {
        JxlExternalResources.RequireEnabled();
        Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken));
        using Image<Rgba64> source = Make(width, height);
        using MemoryStream encoded = Encode(source, effort, true, factor, speed, noise);
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-lossy-modular-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.jxl"), output = Path.Combine(directory, "output.png");
            await File.WriteAllBytesAsync(input, encoded.ToArray(), TestContext.Current.CancellationToken);
            await JxlNativeReference.RunAsync("djxl", [input, output, "--bits_per_sample=16", "--num_threads=1"], TestContext.Current.CancellationToken);
            using Image<Rgba64> managed = Image.Load<Rgba64>(encoded), native = Image.Load<Rgba64>(output);
            int colorPeak = 0, alphaPeak = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Rgba64 a = managed[x, y], b = native[x, y];
                    colorPeak = Math.Max(colorPeak, Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B))));
                    alphaPeak = Math.Max(alphaPeak, Math.Abs(a.A - b.A));
                }
            }
            Assert.True(colorPeak <= 8 && alphaPeak <= 1, $"RGB peak {colorPeak}, alpha peak {alphaPeak}");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static MemoryStream Encode(Image<Rgba64> source, int effort, bool progressive, int factor = 1, int speed = 0, int noise = 0, float distance = .5f)
    {
        MemoryStream output = new();
        try
        {
            new JxlEncoder { Modular = true, Distance = distance, Effort = effort, Resampling = factor, FasterDecoding = speed,
                PhotonNoiseIso = noise, Progressive = progressive ? JxlProgressiveMode.Ac : JxlProgressiveMode.None }.Encode(source, output);
            output.Position = 0; return output;
        }
        catch { output.Dispose(); throw; }
    }

    private static Image<Rgba64> Make(int width, int height)
    {
        Image<Rgba64> image = new(width, height);
        for (int y = 0; y < height; y++)
        { for (int x = 0; x < width; x++) { image[x, y] = new((ushort)(12000 + x * 117), (ushort)(17000 + y * 291), (ushort)(24000 + (x + y) * 57), (ushort)((x + y) * 233)); } }
        return image;
    }
}
