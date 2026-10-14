// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Color;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Metadata.Profiles.Icc;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlIccEncoderTests
{
    [Theory]
    [InlineData(65536)] [InlineData(2 * 1024 * 1024)]
    public void large_profile_entropy_uses_bounded_scratch_and_preserves_following_bits(int size)
    {
        byte[] profile = new byte[size]; new Random(793).NextBytes(profile);
        byte[] predicted = JxlIccCodec.Encode(profile, TestContext.Current.CancellationToken);
        using MemoryStream encoded = new(); JxlBitWriter writer = new(encoded); writer.WriteBits(3, 5);
        long before = GC.GetAllocatedBytesForCurrentThread();
        JxlIccCodec.WriteEncoded(writer, predicted, 10, TestContext.Current.CancellationToken);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 4L * predicted.Length + (1 << 20), $"Large ICC entropy allocated {allocated} bytes for {predicted.Length} bytes.");
        writer.WriteBits(17, 0x15937); writer.ZeroPadToByte(); encoded.Position = 0;
        JxlBitReader reader = new(encoded, TestContext.Current.CancellationToken); Assert.Equal(5U, reader.ReadBits(3));
        Assert.Equal(profile, JxlIccCodec.Decode(JxlIccCodec.ReadEncoded(reader, size + 65536, Configuration.Default.MemoryAllocator), size + 65536));
        Assert.Equal(0x15937U, reader.ReadBits(17));
    }

    [Fact]
    public void expanding_tag_tables_fall_back_to_literal_commands()
    {
        byte[] profile = new byte[6 << 20];
        BinaryPrimitives.WriteUInt32BigEndian(profile, (uint)profile.Length);
        BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(128), 70000);
        for (int i = 0; i < 70000; i++)
        {
            Span<byte> tag = profile.AsSpan(132 + i * 12, 12); "zzzz"u8.CopyTo(tag);
            BinaryPrimitives.WriteUInt32BigEndian(tag[4..], (1U << 21) + (uint)(i % 2) * 2);
            BinaryPrimitives.WriteUInt32BigEndian(tag[8..], (1U << 21) + (uint)(i % 2));
        }
        byte[] encoded = JxlIccCodec.Encode(profile, TestContext.Current.CancellationToken);
        Assert.InRange(encoded.Length, profile.Length, profile.Length + 32);
        Assert.Equal(profile, JxlIccCodec.Decode(encoded, profile.Length + 65536));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void large_icc_raises_the_level_and_checks_container_requirement_before_output(bool raw)
    {
        byte[] profile = LargeProfile();
        using Image<Rgb24> source = new(1, 1); source.Metadata.IccProfile = new(profile);
        using JxlRawImage planes = new(1, 1, [new([JxlRawPlane.FromIntegers(1, 1, [1], 8, 0),
            JxlRawPlane.FromIntegers(1, 1, [2], 8, 1), JxlRawPlane.FromIntegers(1, 1, [3], 8, 2)])]);
        planes.Metadata.IccProfile = new(profile);
        JxlEncoder encoder = new() { Lossless = true, Effort = 1, UseContainer = false };
        using MemoryStream encoded = new();
        Assert.Throws<ArgumentException>(Write); Assert.Equal(0, encoded.Length);
        encoder.UseContainer = null; Write(); encoded.Position = 0;
        IImageInfo info = new JxlDecoder().Identify(Configuration.Default, encoded, TestContext.Current.CancellationToken);
        Assert.Equal(10, info.Metadata.GetJxlMetadata().Level); Assert.Equal(profile, info.Metadata.IccProfile.ToByteArray());
        void Write()
        {
            if (raw) { encoder.EncodeRaw(planes, encoded, cancellationToken: TestContext.Current.CancellationToken); }
            else { encoder.Encode(source, encoded); }
        }
    }

    [Fact]
    public void jpeg_transcoding_raises_the_level_for_a_large_embedded_icc()
    {
        using Image<Rgb24> source = new(1, 1); source.Metadata.IccProfile = new(LargeProfile());
        using MemoryStream jpeg = new(); source.SaveAsJpeg(jpeg); jpeg.Position = 0;
        using MemoryStream encoded = new(); JxlJpegTranscoder.JpegToJxl(jpeg, encoded, cancellationToken: TestContext.Current.CancellationToken);
        encoded.Position = 0;
        Assert.Equal(10, new JxlDecoder().Identify(Configuration.Default, encoded, TestContext.Current.CancellationToken).Metadata.GetJxlMetadata().Level);
        encoded.Position = 0; using MemoryStream restored = new(); JxlJpegTranscoder.JxlToJpeg(encoded, restored, TestContext.Current.CancellationToken);
        Assert.Equal(jpeg.ToArray(), restored.ToArray());
    }

    [Fact]
    [Trait("Category", "JxlNativeReference")]
    public async Task native_decoder_preserves_large_prefix_encoded_icc()
    {
        JxlExternalResources.RequireEnabled();
        byte[] profile = LargeProfile(); using Image<Rgb24> source = new(1, 1); source.Metadata.IccProfile = new(profile);
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-large-icc-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "image.jxl"), output = Path.Combine(directory, "native.png"), original = Path.Combine(directory, "original.icc");
            await using (FileStream stream = File.Create(input)) { await new JxlEncoder { Lossless = true, Effort = 1 }.EncodeAsync(source, stream, TestContext.Current.CancellationToken); }
            await JxlNativeReference.RunAsync("djxl", [input, output, "--orig_icc_out=" + original, "--num_threads=1"], TestContext.Current.CancellationToken);
            Assert.Equal(profile, await File.ReadAllBytesAsync(original, TestContext.Current.CancellationToken));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static byte[] LargeProfile()
    {
        byte[] profile = new byte[(4 << 20) + 4];
        JxlIccSynthesis.Create(new()).CopyTo(profile, 0); BinaryPrimitives.WriteUInt32BigEndian(profile, (uint)profile.Length);
        return profile;
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(3)] [InlineData(4)] [InlineData(40)] [InlineData(44)]
    [InlineData(127)] [InlineData(128)] [InlineData(129)] [InlineData(132)] [InlineData(537)] [InlineData(9011)]
    public void icc_command_writer_preserves_even_short_or_unrecognized_profiles(int size)
    {
        byte[] profile = new byte[size]; new Random(59711).NextBytes(profile);
        Assert.Equal(profile, JxlIccCodec.Decode(JxlIccCodec.Encode(profile, TestContext.Current.CancellationToken), 1 << 20));
    }

    [Theory]
    [InlineData(0, 13)] [InlineData(0, 16)] [InlineData(0, 18)] [InlineData(1, 13)]
    public void compressed_icc_stream_preserves_every_byte_and_following_bits(uint colorSpace, uint transfer)
    {
        byte[] profile = JxlIccSynthesis.Create(new JxlColorEncoding { ColorSpace = colorSpace, TransferFunction = transfer });
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream);
        JxlIccCodec.Write(writer, profile, 9, TestContext.Current.CancellationToken);
        writer.WriteBits(17, 0x15937); writer.ZeroPadToByte(); stream.Position = 0;
        JxlBitReader reader = new(stream, TestContext.Current.CancellationToken);
        byte[] coded = JxlIccCodec.ReadEncoded(reader, 1 << 20, Configuration.Default.MemoryAllocator);
        Assert.Equal(profile, JxlIccCodec.Decode(coded, 1 << 20)); Assert.Equal(0x15937U, reader.ReadBits(17));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    [Trait("Category", "JxlNativeReference")]
    public async Task pinned_djxl_reads_managed_icc_and_public_lossless_pixels(bool compress)
    {
        JxlExternalResources.RequireEnabled();
        string version = await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken);
        Assert.Contains("ef67fde2", version);
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-icc-encode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using Image<Rgba64> source = new(33, 17);
            for (int y = 0; y < source.Height; y++)
            { for (int x = 0; x < source.Width; x++) { source[x, y] = new((ushort)(x * 1587), (ushort)(y * 3141), (ushort)((x + y) * 913), (ushort)(x % 3 == 0 ? 0 : 65535)); } }
            byte[] profile = JxlIccSynthesis.Create(new JxlColorEncoding()); source.Metadata.IccProfile = new IccProfile(profile);
            source.Metadata.XmpProfile = new("<x:xmpmeta>managed JPEG XL metadata metadata metadata metadata metadata</x:xmpmeta>"u8.ToArray());
            string input = Path.Combine(directory, "input.jxl"), output = Path.Combine(directory, "decoded.png");
            await using (FileStream stream = File.Create(input))
            { await new JxlEncoder { Lossless = true, CompressMetadataBoxes = compress }.EncodeAsync(source, stream, TestContext.Current.CancellationToken); }
            string icc = Path.Combine(directory, "original.icc"), xmp = Path.Combine(directory, "original.xmp");
            await JxlNativeReference.RunAsync("djxl", [input, output, "--bits_per_sample=16", "--orig_icc_out=" + icc, "--num_threads=1"], TestContext.Current.CancellationToken);
            await JxlNativeReference.RunAsync("djxl", [input, xmp], TestContext.Current.CancellationToken);
            Assert.Equal(profile, await File.ReadAllBytesAsync(icc, TestContext.Current.CancellationToken));
            Assert.Equal(source.Metadata.XmpProfile.Data, await File.ReadAllBytesAsync(xmp, TestContext.Current.CancellationToken));
            using Image<Rgba64> actual = Image.Load<Rgba64>(output);
            for (int y = 0; y < source.Height; y++)
            { Assert.True(source.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y).SequenceEqual(actual.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y))); }
        }
        finally { Directory.Delete(directory, true); }
    }
}
