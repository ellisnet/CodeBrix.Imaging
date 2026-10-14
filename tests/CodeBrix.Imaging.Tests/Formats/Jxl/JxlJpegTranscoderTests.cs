// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Imaging.Formats.Jpeg;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Jpeg;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlJpegTranscoderTests
{
    [Theory]
    [InlineData(257, 17, 1)] [InlineData(513, 271, 7)] [InlineData(2057, 17, 10)] [InlineData(17, 2057, 11)]
    public void public_transcode_preserves_jpeg_across_hf_and_lf_group_boundaries(int width, int height, int effort)
    {
        byte[] original = MakeJpeg(width, height);
        using JxlTestStream source = new(original, 13); using ForwardOutput output = new();
        JxlJpegTranscoder.JpegToJxl(source, output, new JxlJpegTranscodeOptions { Effort = effort, AllowExpertEffort = true }, TestContext.Current.CancellationToken);
        using JxlTestStream jxl = new(output.Bytes, 7); using MemoryStream restored = new();
        JxlJpegTranscoder.JxlToJpeg(jxl, restored, TestContext.Current.CancellationToken);
        Assert.Equal(original, restored.ToArray()); Assert.False(source.IsDisposed); Assert.True(output.CanWrite);
    }

    [Theory]
    [InlineData("empty-dht")] [InlineData("quant-ids")] [InlineData("reordered-icc")]
    public void original_unusual_jpeg_markers_round_trip(string variation)
    {
        byte[] original = Variant(variation); byte[] encoded = Transcode(original);
        using JxlTestStream jxl = new(encoded); using MemoryStream restored = new();
        JxlJpegTranscoder.JxlToJpeg(jxl, restored, TestContext.Current.CancellationToken);
        Assert.Equal(original, restored.ToArray());
    }

    [Theory]
    [InlineData("empty-dht")] [InlineData("quant-ids")] [InlineData("reordered-icc")]
    [Trait("Category", "JxlNativeReference")]
    public async Task pinned_native_decoder_preserves_unusual_jpeg_markers(string variation)
    {
        JxlExternalResources.RequireEnabled();
        Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken));
        byte[] original = Variant(variation); byte[] encoded = Transcode(original);
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-jpeg-variant-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.jxl"), output = Path.Combine(directory, "output.jpg");
            await File.WriteAllBytesAsync(input, encoded, TestContext.Current.CancellationToken);
            await JxlNativeReference.RunAsync("djxl", [input, output, "--reconstruct_jpeg", "--num_threads=1"], TestContext.Current.CancellationToken);
            Assert.Equal(original, await File.ReadAllBytesAsync(output, TestContext.Current.CancellationToken));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void unsupported_quantization_layout_requires_explicit_pixel_fallback()
    {
        byte[] original = Read("baseline-444");
        // Insert a valid but unused distinct quantization table ahead of the used tables.
        byte[] extra = new byte[69]; extra[0] = 255; extra[1] = 0xdb; extra[3] = 67; extra[4] = 3; extra.AsSpan(5).Fill(3);
        original = [.. original.AsSpan(0, 2), .. extra, .. original.AsSpan(2)];
        using MemoryStream source = new(original, false); using MemoryStream output = new();
        Assert.Throws<NotSupportedException>(() => JxlJpegTranscoder.JpegToJxl(source, output, TestContext.Current.CancellationToken));
        Assert.Equal(0, output.Length); source.Position = 0;
        JxlJpegTranscoder.JpegToJxl(source, output, new JxlJpegTranscodeOptions { AllowPixelFallback = true }, TestContext.Current.CancellationToken);
        using Image<Rgba32> expected = Image.Load<Rgba32>(original); output.Position = 0;
        using Image<Rgba32> actual = Image.Load<Rgba32>(output);
        Assert.False(actual.Metadata.GetJxlMetadata().HasJpegReconstruction);
        for (int y = 0; y < expected.Height; y++)
        { Assert.True(expected.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y).SequenceEqual(actual.Frames.RootFrame.PixelBuffer.DangerousGetRowSpan(y))); }
    }

    [Fact]
    public void cancellation_limits_and_invalid_options_do_not_write_output()
    {
        byte[] original = Read("baseline-444");
        using MemoryStream source = new(original, false); using MemoryStream output = new();
        using CancellationTokenSource canceled = new(); canceled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => JxlJpegTranscoder.JpegToJxl(source, output, canceled.Token));
        Assert.Equal(0, source.Position); Assert.Equal(0, output.Length);
        foreach (JxlJpegTranscodeOptions options in new JxlJpegTranscodeOptions[] { new() { Effort = 11 }, new() { Effort = 0 }, new() { MaxPixels = 0 }, new() { MaxInputBytes = 0 } })
        { Assert.ThrowsAny<ArgumentException>(() => JxlJpegTranscoder.JpegToJxl(source, output, options, TestContext.Current.CancellationToken)); Assert.Equal(0, source.Position); }
        Assert.Throws<InvalidImageContentException>(() => JxlJpegTranscoder.JpegToJxl(source, output, new JxlJpegTranscodeOptions { MaxInputBytes = 4 }, TestContext.Current.CancellationToken));
        source.Position = 0;
        Assert.Throws<InvalidImageContentException>(() => JxlJpegTranscoder.JpegToJxl(source, output, new JxlJpegTranscodeOptions { MaxPixels = 1 }, TestContext.Current.CancellationToken));
        Assert.Equal(0, output.Length); Assert.True(source.CanRead); Assert.True(output.CanWrite);
    }

    [Theory]
    [InlineData(513, 271)] [InlineData(2057, 17)] [InlineData(17, 2057)]
    [Trait("Category", "JxlNativeReference")]
    public async Task pinned_native_reconstruction_crosses_group_boundaries(int width, int height)
    {
        JxlExternalResources.RequireEnabled();
        await NativeRoundTrip(MakeJpeg(width, height));
    }

    [Theory]
    [InlineData("bench_oriented_brg")] [InlineData("grayscale_jpeg")] [InlineData("cafe")]
    [Trait("Category", "JxlConformance")]
    public async Task original_conformance_jpegs_round_trip_through_public_encoder(string name)
    {
        JxlExternalResources.RequireEnabled();
        JxlReferenceFixture fixture = new();
        string reference = await fixture.GetAsync(name, "reconstructed.jpg", TestContext.Current.CancellationToken);
        await NativeRoundTrip(await File.ReadAllBytesAsync(reference, TestContext.Current.CancellationToken));
    }

    private static async Task NativeRoundTrip(byte[] original)
    {
        JxlExternalResources.RequireEnabled();
        Assert.Contains("ef67fde2", await JxlNativeReference.RunAsync("djxl", ["--version"], TestContext.Current.CancellationToken));
        byte[] encoded = Transcode(original);
        using MemoryStream jxl = new(encoded, false); using MemoryStream restored = new();
        JxlJpegTranscoder.JxlToJpeg(jxl, restored, TestContext.Current.CancellationToken); Assert.Equal(original, restored.ToArray());
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-jxl-jpeg-full-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string input = Path.Combine(directory, "input.jxl"), output = Path.Combine(directory, "output.jpg");
            await File.WriteAllBytesAsync(input, encoded, TestContext.Current.CancellationToken);
            await JxlNativeReference.RunAsync("djxl", [input, output, "--reconstruct_jpeg", "--num_threads=1"], TestContext.Current.CancellationToken);
            Assert.Equal(original, await File.ReadAllBytesAsync(output, TestContext.Current.CancellationToken));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static byte[] Variant(string variation)
    {
        byte[] original = Read(variation == "reordered-icc" ? "split-icc" : "baseline-444");
        if (variation == "empty-dht") { return [.. original.AsSpan(0, 2), 255, 0xc4, 0, 2, .. original.AsSpan(2)]; }
        List<(int Offset, int Length)> icc = [];
        for (int position = 2; position < original.Length;)
        {
            int marker = original[position + 1]; if (marker == 0xda) { break; }
            int length = BinaryPrimitives.ReadUInt16BigEndian(original.AsSpan(position + 2));
            if (variation == "quant-ids" && marker == 0xdb)
            {
                for (int table = position + 4; table < position + 2 + length; table += 1 + (((original[table] >> 4) + 1) * 64))
                { original[table] ^= 3; }
            }
            if (variation == "quant-ids" && marker == 0xc0)
            { for (int c = 0; c < original[position + 9]; c++) { original[position + 12 + (c * 3)] ^= 3; } }
            if (marker == 0xe2 && original.AsSpan(position + 4, 12).SequenceEqual("ICC_PROFILE\0"u8)) { icc.Add((position, length + 2)); }
            position += length + 2;
        }
        if (variation != "reordered-icc") { return original; }
        Assert.True(icc.Count >= 2); using MemoryStream rearranged = new(); int current = 0;
        for (int i = 0; i < icc.Count; i++)
        {
            rearranged.Write(original.AsSpan(current, icc[i].Offset - current));
            var replacement = icc[icc.Count - 1 - i]; rearranged.Write(original.AsSpan(replacement.Offset, replacement.Length));
            current = icc[i].Offset + icc[i].Length;
        }
        rearranged.Write(original.AsSpan(current)); byte[] bytes = rearranged.ToArray();
        using JxlJpegParser parsed = JxlJpegParser.Read(bytes, Configuration.Default.MemoryAllocator, 100_000_000, TestContext.Current.CancellationToken);
        using JxlJpegParser expected = JxlJpegParser.Read(original, Configuration.Default.MemoryAllocator, 100_000_000, TestContext.Current.CancellationToken);
        Assert.NotEmpty(parsed.Icc); Assert.Equal(expected.Icc, parsed.Icc);
        return bytes;
    }

    private static byte[] Read(string name) => File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(JxlHeaderTests.ResourceRoot), "JxlSynthetic", "Jpeg", name + ".jpg"));

    private static byte[] Transcode(byte[] original)
    {
        using MemoryStream input = new(original, false); using MemoryStream output = new();
        JxlJpegTranscoder.JpegToJxl(input, output, TestContext.Current.CancellationToken); return output.ToArray();
    }

    private static byte[] MakeJpeg(int width, int height)
    {
        using Image<Rgb24> image = new(width, height);
        for (int y = 0; y < height; y++)
        { for (int x = 0; x < width; x++) { image[x, y] = new((byte)(x * 5 + y), (byte)(y * 7 + x / 2), (byte)(x ^ y)); } }
        using MemoryStream output = new(); new JpegEncoder { Quality = 87 }.Encode(image, output); return output.ToArray();
    }

    private sealed class ForwardOutput : Stream
    {
        private readonly MemoryStream inner = new();
        internal byte[] Bytes => this.inner.ToArray();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => this.inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => this.inner.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => this.inner.Write(buffer);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Flush() => this.inner.Flush();
        protected override void Dispose(bool disposing) { if (disposing) { this.inner.Dispose(); } base.Dispose(disposing); }
    }
}
