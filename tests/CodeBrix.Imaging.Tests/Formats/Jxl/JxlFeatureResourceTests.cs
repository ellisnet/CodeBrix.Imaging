// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Imaging.Formats.Jxl;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Entropy;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.PixelFormats;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlFeatureResourceTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void truncated_extension_does_not_allocate_its_advertised_payload(bool forwardOnly)
    {
        using MemoryStream encoded = new(); JxlBitWriter writer = new(encoded);
        writer.WriteU64(1); writer.WriteU64(64UL * 1024 * 1024 * 8); writer.ZeroPadToByte();
        using Stream input = forwardOnly ? new JxlTestStream(encoded.ToArray(), 1) : new MemoryStream(encoded.ToArray());
        JxlBitReader reader = new(input, TestContext.Current.CancellationToken);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<InvalidImageContentException>(() => JxlExtensions.Read(reader, 64 << 20));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 1 << 20, $"Truncated extension allocated {allocated} bytes.");
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void extension_chunks_preserve_unaligned_payloads_and_following_fields(int offset)
    {
        JxlExtensions expected = new() { Mask = 7 };
        for (int i = 0; i < 3; i++)
        {
            int length = i == 0 ? 0 : i == 1 ? 8193 : 16389;
            byte[] data = new byte[length]; new Random(717 + i).NextBytes(data);
            if (length != 0) { data[^1] &= 7; }
            expected.Data[i] = data; expected.BitLengths[i] = length == 0 ? 0 : (ulong)length * 8 - 5;
        }
        using MemoryStream encoded = new(); JxlBitWriter writer = new(encoded);
        writer.WriteBits(offset, 0); expected.Write(writer); writer.WriteBits(7, 91); writer.ZeroPadToByte();
        using JxlTestStream input = new(encoded.ToArray(), 113); JxlBitReader reader = new(input, TestContext.Current.CancellationToken);
        reader.ReadBits(offset); JxlExtensions actual = JxlExtensions.Read(reader, 65536);
        Assert.Equal(expected.Mask, actual.Mask); Assert.Equal(expected.BitLengths, actual.BitLengths);
        for (int i = 0; i < 3; i++) { Assert.Equal(expected.Data[i], actual.Data[i]); }
        Assert.Equal(91U, reader.ReadBits(7));
    }

    [Fact]
    public void spline_count_is_rejected_before_starting_point_allocation()
    {
        using MemoryStream encoded = Tokens([new(2, 1U << 20)], 6);
        JxlBitReader reader = new(encoded, TestContext.Current.CancellationToken);
        long before = GC.GetAllocatedBytesForCurrentThread();
        InvalidImageContentException error = Assert.Throws<InvalidImageContentException>(() =>
            JxlSplines.Read(reader, new() { Width = 8192, Height = 8192 }, Configuration.Default.MemoryAllocator));
        Assert.Contains("too many splines", error.Message);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 1 << 20, $"Invalid spline count allocated {allocated} bytes.");
    }

    [Theory]
    [InlineData(1, 1, true)] [InlineData(2, 2, true)] [InlineData(2, 3, false)]
    public void spline_point_budget_includes_all_starting_points(int first, int second, bool valid)
    {
        // A 2x2 or 4x2 frame allows two or four total control points.
        List<JxlToken> tokens = [new(2, 1), new(1, 0), new(1, 0), new(1, 2), new(1, 0), new(0, 0)];
        foreach (int count in new[] { first, second })
        {
            tokens.Add(new(3, (uint)count - 1));
            for (int p = 1; p < count; p++) { tokens.Add(new(4, p == 1 ? 2U : 0)); tokens.Add(new(4, 0)); }
            for (int c = 0; c < 128; c++) { tokens.Add(new(5, 0)); }
        }
        using MemoryStream encoded = Tokens(tokens.ToArray(), 6);
        JxlBitReader reader = new(encoded, TestContext.Current.CancellationToken);
        JxlFrameHeader frame = new() { Width = first == 1 ? 2U : 4U, Height = 2 };
        if (valid) { Assert.NotNull(JxlSplines.Read(reader, frame, Configuration.Default.MemoryAllocator)); }
        else { Assert.Contains("control points", Assert.Throws<InvalidImageContentException>(() => JxlSplines.Read(reader, frame, Configuration.Default.MemoryAllocator)).Message); }
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void public_patch_decoding_counts_repeated_rectangle_work(bool modular, bool raw)
    {
        const int width = 32, height = 16, count = 8;
        using JxlRawImage source = new(width, height, [Frame(true), Frame(false)]);
        source.Frames[1].Encoding.Patches = [new() { Reference = 1, Source = new(0, 0, width, height), Targets = new JxlPatchTarget[count] }];
        for (int i = 0; i < count; i++) { source.Frames[1].Encoding.Patches[0].Targets[i] = new() { Position = new(0, 0), Blending = new(JxlPatchBlendMode.Replace) }; }
        using MemoryStream encoded = new();
        new JxlEncoder { Modular = modular, Effort = 1 }.EncodeRaw(source, encoded, cancellationToken: TestContext.Current.CancellationToken);
        JxlDecoder decoder = new() { MaxDecodedPixels = (count + 2) * width * height - 1 };
        Assert.Contains("MaxDecodedPixels", Assert.Throws<InvalidImageContentException>(Decode).Message);
        decoder.MaxDecodedPixels++; Decode();
        void Decode()
        {
            encoded.Position = 0;
            if (raw) { using JxlRawImage image = decoder.DecodeRaw(Configuration.Default, encoded, TestContext.Current.CancellationToken); }
            else { using Image<Rgba32> image = decoder.Decode<Rgba32>(Configuration.Default, encoded, TestContext.Current.CancellationToken); }
        }
        static JxlRawFrame Frame(bool reference)
        {
            JxlRawPlane[] planes = new JxlRawPlane[3];
            for (int c = 0; c < 3; c++) { int[] samples = new int[width * height]; Array.Fill(samples, reference ? 80 : 20); planes[c] = JxlRawPlane.FromIntegers(width, height, samples, 8, c); }
            return new(planes, metadata: new() { IsReferenceOnly = reference, SaveAsReference = reference ? 1 : 0, SaveBeforeColorTransform = reference });
        }
    }

    [Theory]
    [InlineData(-3, -2, 5, 4, 4)]
    [InlineData(30, 14, 5, 4, 4)]
    [InlineData(32, 16, 5, 4, 1)]
    [InlineData(int.MinValue, int.MinValue, uint.MaxValue, uint.MaxValue, 512)]
    public void patch_work_uses_clipped_area_with_a_minimum_per_target(int x, int y, uint width, uint height, long work)
    {
        JxlFrameHeader frame = new() { Width = 64, Height = 32, Upsampling = 2 };
        JxlDecodeBudget budget = new(new() { MaxDecodedPixels = work });
        budget.ReservePatchTarget(frame, x, y, width, height);
        Assert.Throws<InvalidImageContentException>(() => budget.ReservePatchTarget(frame, x, y, width, height));
    }

    private static MemoryStream Tokens(JxlToken[] tokens, int contexts)
    {
        MemoryStream stream = new(); JxlBitWriter writer = new(stream); JxlEntropyEncoder encoder = new(tokens, contexts);
        encoder.WriteHeader(writer); encoder.WriteTokens(writer, tokens, TestContext.Current.CancellationToken); writer.ZeroPadToByte();
        stream.Position = 0; return stream;
    }
}
