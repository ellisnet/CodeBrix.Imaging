// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlFrameSectionsTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void truncated_sections_allocate_for_received_bytes_only(bool forwardOnly)
    {
        // A small, safe version of review finding A1: the declared length must
        // not control a large allocation before any payload has arrived.
        using Stream stream = forwardOnly ? new JxlTestStream(new byte[80]) : new MemoryStream(new byte[80], false);
        JxlFrameToc toc = new() { Sizes = [16 << 20], TotalBytes = 16 << 20 };
        JxlBitReader reader = new(stream, TestContext.Current.CancellationToken);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<InvalidImageContentException>(() => new JxlFrameSections(reader, toc,
            Configuration.Default.MemoryAllocator, TestContext.Current.CancellationToken));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 1 << 20, $"Truncated section allocated {allocated} bytes.");
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(8191)] [InlineData(8192)]
    [InlineData(8193)] [InlineData(65537)]
    public void bounded_reads_preserve_sections_permutation_and_prefetched_bytes(int length)
    {
        byte[] payload = new byte[length + 7];
        new Random(3511).NextBytes(payload);
        // Prefix has one consumed flag bit and seven zero alignment bits.
        payload[0] = 1;
        using JxlTestStream stream = new(payload, 113);
        JxlBitReader input = new(stream, TestContext.Current.CancellationToken);
        input.PeekBits(32);
        Assert.True(input.ReadBool()); input.ZeroPadToByte();
        JxlFrameToc toc = new() { Sizes = [(uint)length, 6], TotalBytes = (uint)length + 6UL, LogicalToPhysical = [1, 0] };
        JxlFrameSections sections = new(input, toc, Configuration.Default.MemoryAllocator, TestContext.Current.CancellationToken);
        byte[] first = new byte[6], second = new byte[length];
        sections.Get(0).ReadBytes(first); sections.Finish(0);
        sections.Get(1).ReadBytes(second); sections.Finish(1);
        Assert.Equal(payload.AsSpan(length + 1).ToArray(), first);
        Assert.Equal(payload.AsSpan(1, length).ToArray(), second);
        Assert.Equal(payload.Length * 8L, input.Position);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void public_pixel_and_raw_decoders_reject_giant_truncated_toc_without_large_allocation(bool forwardOnly, bool raw)
    {
        using MemoryStream encoded = new(); JxlBitWriter writer = new(encoded);
        JxlImageHeader header = new() { Size = new(1, 1), XybEncoded = false };
        JxlFrameHeader frame = new() { Width = 1, Height = 1, Modular = true };
        header.Write(writer); frame.Write(writer, header);
        JxlFrameToc.WriteOrdered(writer, frame, [1U << 30]);
        using Stream input = forwardOnly ? new JxlTestStream(encoded.ToArray(), 1) : new MemoryStream(encoded.ToArray(), false);
        JxlDecoder decoder = new();
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<InvalidImageContentException>(() =>
        {
            if (raw) { using JxlRawImage image = decoder.DecodeRaw(Configuration.Default, input, TestContext.Current.CancellationToken); }
            else { using Image<Rgba32> image = decoder.Decode<Rgba32>(Configuration.Default, input, TestContext.Current.CancellationToken); }
        });
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 1 << 20, $"Truncated image allocated {allocated} bytes.");
    }
}
