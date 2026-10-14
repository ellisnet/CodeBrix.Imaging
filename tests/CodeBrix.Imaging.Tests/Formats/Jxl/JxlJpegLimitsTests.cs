// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.IO.Compression;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Jpeg;
using CodeBrix.Imaging.Formats.Jxl.JpegReconstruction;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlJpegLimitsTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void input_scan_limit_is_enforced_before_the_pixel_fallback_path(bool fallback)
    {
        byte[] bytes = Read("progressive-444", ".jpg");
        // This unused table forces the parser's unsupported-input branch,
        // before it reaches the scans. The outer limit must still reject it.
        byte[] quant = new byte[69]; quant[0] = 255; quant[1] = 0xdb; quant[3] = 67; quant[4] = 3; quant.AsSpan(5).Fill(3);
        bytes = [.. bytes.AsSpan(0, 2), .. quant, .. bytes.AsSpan(2)];
        using JxlTestStream input = new(bytes); using MemoryStream output = new();
        Assert.Contains("MaxScans", Assert.Throws<InvalidImageContentException>(() => JxlJpegTranscoder.JpegToJxl(input, output,
            new JxlJpegTranscodeOptions { MaxScans = 1, AllowPixelFallback = fallback }, TestContext.Current.CancellationToken)).Message);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void scan_lookalikes_in_metadata_and_after_eoi_do_not_consume_scan_budget()
    {
        byte[] original = Read("baseline-444", ".jpg");
        byte[] bytes = [.. original.AsSpan(0, 2), 255, 0xef, 0, 8, 255, 0xda, 0, 4, 1, 2,
            .. original.AsSpan(2), 255, 0xda, 0, 4, 1, 2];
        using MemoryStream input = new(bytes, false); using MemoryStream jxl = new(); using MemoryStream output = new();
        JxlJpegTranscoder.JpegToJxl(input, jxl, new JxlJpegTranscodeOptions { Effort = 1, MaxScans = 1 }, TestContext.Current.CancellationToken);
        jxl.Position = 0; JxlJpegTranscoder.JxlToJpeg(jxl, output, TestContext.Current.CancellationToken);
        Assert.Equal(bytes, output.ToArray());
    }

    [Fact]
    public void reconstruction_scan_flood_is_rejected_before_allocating_scan_tables()
    {
        using MemoryStream encoded = new(); JxlBitWriter writer = new(encoded); writer.WriteBool(true);
        for (int i = 0; i < 501; i++) { writer.WriteBits(6, 0xda - 0xc0); }
        writer.WriteBits(6, 0xd9 - 0xc0); writer.ZeroPadToByte(); byte[] bytes = encoded.ToArray();
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Contains("MaxScans", Assert.Throws<InvalidImageContentException>(() =>
            JxlJpegData.Read(bytes, 64 << 20, TestContext.Current.CancellationToken)).Message);
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 1 << 20);
    }

    [Theory]
    [InlineData("pixels")] [InlineData("metadata")] [InlineData("memory")]
    [InlineData("scans")] [InlineData("blocks")]
    public void public_reconstruction_honors_configured_resource_limits(string limit)
    {
        JxlJpegReconstructionOptions options = new();
        if (limit == "pixels") { options.MaxPixels = 1; }
        if (limit == "metadata") { options.MaxMetadataBytes = 8; }
        if (limit == "memory") { options.MaxModularBytes = 1; }
        if (limit == "scans") { options.MaxScans = 1; }
        if (limit == "blocks") { options.MaxScanBlocks = 1; }
        using JxlTestStream input = new(Read("progressive-444", ".jxl")); using MemoryStream output = new();
        Assert.Throws<InvalidImageContentException>(() => JxlJpegTranscoder.JxlToJpeg(input, output, options, TestContext.Current.CancellationToken));
        Assert.False(input.IsDisposed); Assert.True(output.CanWrite);
    }

    [Fact]
    public void scan_block_budget_is_cumulative_and_allows_the_exact_boundary()
    {
        using JxlJpegParser parsed = JxlJpegParser.Read(Read("progressive-444", ".jpg"), Configuration.Default.MemoryAllocator,
            100_000_000, TestContext.Current.CancellationToken);
        Assert.All(parsed.Coefficients.Frame.JpegUpsampling, value => Assert.Equal(0U, value));
        long blocksPerComponent = ((parsed.Coefficients.Image.Size.Width + 7) / 8) * ((parsed.Coefficients.Image.Size.Height + 7) / 8);
        long total = 0;
        foreach (JxlJpegData.Scan scan in parsed.Data.Scans) { total += blocksPerComponent * scan.Components.Length; }
        byte[] encoded = Read("progressive-444", ".jxl");
        using MemoryStream input = new(encoded, false); using MemoryStream output = new();
        JxlJpegReconstructionOptions options = new() { MaxScanBlocks = total, MaxScans = parsed.Data.Scans.Length };
        JxlJpegTranscoder.JxlToJpeg(input, output, options, TestContext.Current.CancellationToken);
        Assert.Equal(Read("progressive-444", ".jpg"), output.ToArray());
        input.Position = 0; output.SetLength(0); options.MaxScanBlocks--;
        Assert.Contains("MaxScanBlocks", Assert.Throws<InvalidImageContentException>(() => JxlJpegTranscoder.JxlToJpeg(input, output, options, TestContext.Current.CancellationToken)).Message);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void output_limit_counts_only_new_bytes_and_never_crosses_its_boundary(bool forwardOnly)
    {
        byte[] jpeg = Read("baseline-444", ".jpg"), jxl = Read("baseline-444", ".jxl");
        using MemoryStream storage = new(); storage.Write([17, 19, 23]);
        using Stream output = forwardOnly ? new ForwardOutput(storage) : storage;
        using MemoryStream input = new(jxl, false);
        JxlJpegReconstructionOptions options = new() { MaxOutputBytes = jpeg.Length };
        JxlJpegTranscoder.JxlToJpeg(input, output, options, TestContext.Current.CancellationToken);
        Assert.Equal(new byte[] { 17, 19, 23 }, storage.ToArray().AsSpan(0, 3).ToArray());
        Assert.Equal(jpeg, storage.ToArray().AsSpan(3).ToArray());
        storage.SetLength(3); storage.Position = 3; input.Position = 0; options.MaxOutputBytes--;
        Assert.Contains("MaxOutputBytes", Assert.Throws<InvalidImageContentException>(() => JxlJpegTranscoder.JxlToJpeg(input, output, options, TestContext.Current.CancellationToken)).Message);
        Assert.True(storage.Length - 3 <= options.MaxOutputBytes); Assert.True(output.CanWrite);
    }

    [Fact]
    public void absent_brotli_payload_does_not_allocate_its_declared_reconstruction_size()
    {
        using JxlJpegParser jpeg = JxlJpegParser.Read(Read("baseline-444", ".jpg"), Configuration.Default.MemoryAllocator,
            100_000_000, TestContext.Current.CancellationToken);
        jpeg.Data.TailLength = 4 << 20;
        jpeg.Data.Data = new byte[jpeg.Data.AppLength + jpeg.Data.CommentLength + jpeg.Data.IntermarkerLength + jpeg.Data.TailLength];
        byte[] encoded = jpeg.Data.Write(1, TestContext.Current.CancellationToken);
        byte[] compressed = new byte[BrotliEncoder.GetMaxCompressedLength(jpeg.Data.Data.Length)];
        Assert.True(BrotliEncoder.TryCompress(jpeg.Data.Data, compressed, out int written, 1, 22));
        Assert.True(encoded.AsSpan(encoded.Length - written).SequenceEqual(compressed.AsSpan(0, written)));
        byte[] empty = new byte[16]; Assert.True(BrotliEncoder.TryCompress([], empty, out int emptySize, 1, 22));
        byte[] truncated = [.. encoded.AsSpan(0, encoded.Length - written), .. empty.AsSpan(0, emptySize)];
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<InvalidImageContentException>(() => JxlJpegData.Read(truncated, 64 << 20, TestContext.Current.CancellationToken));
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 1 << 20);
    }

    [Fact]
    public void invalid_options_fail_before_reading_or_writing()
    {
        using MemoryStream input = new(); using MemoryStream output = new();
        JxlJpegReconstructionOptions[] invalid = [new() { MaxPixels = 0 }, new() { MaxMetadataBytes = -1 },
            new() { MaxModularBytes = 0 }, new() { MaxScans = 0 }, new() { MaxScanBlocks = 0 }, new() { MaxOutputBytes = 0 }];
        foreach (JxlJpegReconstructionOptions options in invalid)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => JxlJpegTranscoder.JxlToJpeg(input, output, options, TestContext.Current.CancellationToken));
            Assert.Equal(0, input.Position); Assert.Equal(0, output.Length);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => JxlJpegTranscoder.JpegToJxl(input, output,
            new JxlJpegTranscodeOptions { MaxScans = 0 }, TestContext.Current.CancellationToken));
    }

    private static byte[] Read(string name, string extension) => File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(JxlHeaderTests.ResourceRoot), "JxlSynthetic", "Jpeg", name + extension));

    private sealed class ForwardOutput(MemoryStream destination) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => destination.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => destination.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => destination.Write(buffer);
        public override void Flush() => destination.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
