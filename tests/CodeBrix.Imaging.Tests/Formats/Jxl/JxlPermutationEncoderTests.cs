// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Coding;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Entropy;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlPermutationEncoderTests
{
    [Theory]
    [InlineData(0, 0)] [InlineData(1, 0)] [InlineData(63, 1)] [InlineData(1024, 16)] [InlineData(65536, 0)] [InlineData(32, 32)]
    public void entropy_permutations_preserve_identity_reverse_shuffle_and_skipped_prefix(int size, int skip)
    {
        int[] values = new int[size]; for (int i = 0; i < size; i++) { values[i] = i; }
        Verify(); Array.Reverse(values, skip, size - skip); Verify(); new Random(27504).Shuffle(values.AsSpan(skip)); Verify();
        void Verify()
        {
            using MemoryStream stream = new(); JxlBitWriter writer = new(stream);
            JxlPermutationEncoder.Write(writer, values, skip, cancellationToken: TestContext.Current.CancellationToken);
            writer.WriteBits(13, 7919); writer.ZeroPadToByte(); stream.Position = 0;
            JxlBitReader reader = new(stream, TestContext.Current.CancellationToken);
            using JxlEntropyDecoder entropy = JxlEntropyDecoder.Read(reader, 8, Configuration.Default.MemoryAllocator);
            entropy.Begin(reader); Assert.Equal(values, entropy.ReadPermutation(reader, size, skip)); entropy.Finish();
            Assert.Equal(7919U, reader.ReadBits(13));
        }
    }

    [Fact]
    public void permutation_validation_precedes_output()
    {
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream);
        Assert.Throws<ArgumentException>(() => JxlPermutationEncoder.Write(writer, [0, 1, 1], cancellationToken: TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(() => JxlPermutationEncoder.Write(writer, [0, -1, 2], cancellationToken: TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(() => JxlPermutationEncoder.Write(writer, [1, 0, 2], 1, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentOutOfRangeException>(() => JxlPermutationEncoder.Write(writer, [0], 2, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(0, writer.Position);
    }

    [Fact]
    public void permuted_toc_reorders_real_modular_sections_on_forward_only_input()
    {
        using Image<Rgb24> source = CreateImage();
        byte[] bytes = Reordered(source); using JxlTestStream input = new(bytes, 3);
        using Image<Rgb24> actual = Image.Load<Rgb24>(input); Compare(source, actual);
    }

    [Fact]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_accepts_reordered_frame_sections()
    {
        JxlExternalResources.RequireEnabled();
        Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken));
        using Image<Rgb24> source = CreateImage(); byte[] bytes = Reordered(source);
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-toc-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.jxl"), output = Path.Combine(directory, "output.png");
            await File.WriteAllBytesAsync(input, bytes, TestContext.Current.CancellationToken);
            await JxlNativeReference.RunAsync("djxl", [input, output, "--bits_per_sample=8", "--num_threads=1"], TestContext.Current.CancellationToken);
            using Image<Rgb24> actual = Image.Load<Rgb24>(output); Compare(source, actual);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static byte[] Reordered(Image<Rgb24> source)
    {
        using MemoryStream encoded = new(); new JxlEncoder { Lossless = true, Effort = 1, UseContainer = false }.Encode(source, encoded);
        byte[] bytes = encoded.ToArray(); encoded.Position = 0;
        JxlBitReader reader = new(encoded, TestContext.Current.CancellationToken);
        JxlImageHeader image = JxlImageHeader.Read(reader, 1_000_000, 1_000_000);
        JxlFrameHeader frame = JxlFrameHeader.Read(reader, image, 1_000_000, 1_000_000);
        long headerBits = reader.Position;
        JxlFrameToc toc = JxlFrameToc.Read(reader, frame, Configuration.Default.MemoryAllocator);
        Assert.True(toc.Sizes.Length > 1); Assert.Null(toc.LogicalToPhysical);
        int count = toc.Sizes.Length; int[] permutation = new int[count]; uint[] sizes = new uint[count];
        for (int i = 0; i < count; i++) { permutation[i] = count - 1 - i; sizes[count - 1 - i] = toc.Sizes[i]; }
        using MemoryStream result = new(); JxlBitWriter writer = new(result); writer.WriteBitstream(bytes, headerBits);
        JxlFrameToc.Write(writer, frame, sizes, permutation, cancellationToken: TestContext.Current.CancellationToken);
        for (int i = count - 1; i >= 0; i--)
        { writer.WriteBytes(bytes.AsSpan(checked((int)(toc.DataPosition + (long)toc.Offsets[i])), (int)toc.Sizes[i])); }
        return result.ToArray();
    }

    private static Image<Rgb24> CreateImage()
    {
        Image<Rgb24> image = new(513, 9);
        for (int y = 0; y < image.Height; y++)
        { for (int x = 0; x < image.Width; x++) { image[x, y] = new((byte)(x * 37), (byte)(y * 71), (byte)(x * y)); } }
        return image;
    }

    private static void Compare(Image<Rgb24> expected, Image<Rgb24> actual)
    {
        Assert.Equal((expected.Width, expected.Height), (actual.Width, actual.Height));
        for (int y = 0; y < expected.Height; y++)
        { Assert.True(expected.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y).SequenceEqual(actual.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y))); }
    }
}
