// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Coding;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Entropy;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlLz77Tests
{
    [Theory]
    [InlineData(2)] [InlineData(7)] [InlineData(10)]
    public void unprofitable_periodic_matches_do_not_repeatedly_scan_the_remaining_stream(int effort)
    {
        JxlToken[] input = new JxlToken[262144];
        for (int i = 0; i < input.Length; i++) { input[i] = new(i % 2, (uint)(i % 2)); }
        using CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(10));
        Assert.Empty(JxlLz77.Match(input, 2, 0, effort, false, budget.Token));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void optimized_large_context_map_preserves_lz_lengths_and_distance_context(bool prefix)
    {
        // Arrange: one literal in every context, then a long repetition which
        // crosses all context boundaries and needs a distinct distance context.
        const int contexts = 513;
        JxlToken[] original = new JxlToken[contexts * 8];
        for (int i = 0; i < original.Length; i++) { original[i] = new(i % contexts, (uint)(i % contexts)); }
        JxlToken[] encoded = new JxlToken[contexts + 2];
        original.AsSpan(0, contexts).CopyTo(encoded);
        encoded[contexts] = new(0, (uint)(original.Length - contexts - 3), true);
        encoded[contexts + 1] = new(contexts, contexts - 1);

        // Act.
        JxlEntropyEncoder encoder = JxlEntropyEncoder.CreateOptimized(encoded, contexts, prefix, 7, true, TestContext.Current.CancellationToken);
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream);
        encoder.WriteHeader(writer); encoder.WriteTokens(writer, encoded, TestContext.Current.CancellationToken);
        long bits = writer.Position;
        writer.WriteBits(21, 0x157913); writer.ZeroPadToByte(); stream.Position = 0;

        // Assert.
        JxlBitReader reader = new(stream, TestContext.Current.CancellationToken);
        using JxlEntropyDecoder decoder = JxlEntropyDecoder.Read(reader, contexts, Configuration.Default.MemoryAllocator);
        decoder.Begin(reader);
        foreach (JxlToken token in original) { Assert.Equal(token.Value, decoder.Read(reader, token.Context)); }
        decoder.Finish(); Assert.Equal(bits, reader.Position); Assert.Equal(0x157913U, reader.ReadBits(21));
    }

    [Theory]
    [InlineData(true, 0, 1)]
    [InlineData(false, 17, 1)]
    [InlineData(true, 17, 3)]
    [InlineData(false, 0, 7)]
    [InlineData(true, 31, 8)]
    [InlineData(false, 17, 10)]
    [InlineData(false, 31, 11)]
    public void repetition_matches_preserve_context_changes_and_stream_boundaries(bool prefix, uint width, int effort)
    {
        JxlToken[] input = new JxlToken[6000];
        Random random = new(23721); uint[] pattern = new uint[97];
        for (int i = 0; i < pattern.Length; i++) { pattern[i] = (uint)random.NextInt64(0, 1L << 32); }
        for (int i = 0; i < input.Length; i++)
        { input[i] = new(i % 5, effort == 1 ? (uint)(i / 48 % 13) : i % 1300 < 350 ? 0U : pattern[i % pattern.Length]); }
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream);
        Assert.True(JxlEntropyEncoder.WriteCompressed(writer, input, 5, width, prefix, effort, TestContext.Current.CancellationToken));
        long bits = writer.Position;
        JxlEntropyEncoder plain = new(input, 5, usePrefix: prefix, effort: effort);
        Assert.True(bits < plain.GetBitCount(input, TestContext.Current.CancellationToken) / 2);
        writer.WriteBits(21, 0x157913); writer.ZeroPadToByte(); stream.Position = 0;
        JxlBitReader reader = new(stream, TestContext.Current.CancellationToken);
        using JxlEntropyDecoder decoder = JxlEntropyDecoder.Read(reader, 5, Configuration.Default.MemoryAllocator);
        decoder.Begin(reader);
        foreach (JxlToken token in input) { Assert.Equal(token.Value, decoder.Read(reader, token.Context, width)); }
        decoder.Finish(); Assert.Equal(bits, reader.Position); Assert.Equal(0x157913U, reader.ReadBits(21));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void constants_keep_single_symbol_coding_without_repeat_overhead(bool prefix)
    {
        JxlToken[] input = new JxlToken[65536];
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream);
        Assert.False(JxlEntropyEncoder.WriteCompressed(writer, input, 1, 256, prefix, 11, TestContext.Current.CancellationToken));
        Assert.True(writer.Position <= new JxlEntropyEncoder(input, 1, usePrefix: prefix, effort: 11).GetBitCount(input, TestContext.Current.CancellationToken));
        Assert.True(writer.Position < 100);
        long bits = writer.Position; writer.ZeroPadToByte(); stream.Position = 0;
        JxlBitReader reader = new(stream, TestContext.Current.CancellationToken);
        using JxlEntropyDecoder decoder = JxlEntropyDecoder.Read(reader, 1, Configuration.Default.MemoryAllocator);
        decoder.Begin(reader);
        for (int i = 0; i < input.Length; i++) { Assert.Equal(0U, decoder.Read(reader, 0)); }
        decoder.Finish(); Assert.Equal(bits, reader.Position);
    }

    [Theory]
    [InlineData(true, 0U)]
    [InlineData(false, 1U)]
    [InlineData(false, 1024U)]
    public void longest_window_distance_and_overlapping_copy_decode_exactly(bool prefix, uint width)
    {
        const int distance = 1 << 20;
        JxlToken[] coded = new JxlToken[distance + 4];
        for (int i = 0; i < distance; i++) { coded[i] = new(0, (uint)i); }
        coded[distance] = new(0, 4, true); // Seven samples from the window boundary.
        coded[distance + 1] = new(1, (uint)(distance - 1) + (width == 0 ? 0U : 120U));
        coded[distance + 2] = new(0, 100, true); // Overlapping distance-one copy.
        coded[distance + 3] = new(1, width == 0 ? 0U : 1U);
        JxlEntropyEncoder encoder = new(coded, 1, usePrefix: prefix, lz77: true);
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream);
        encoder.WriteHeader(writer); encoder.WriteTokens(writer, coded, TestContext.Current.CancellationToken);
        Assert.Equal(encoder.GetBitCount(coded, TestContext.Current.CancellationToken), writer.Position);
        writer.ZeroPadToByte(); stream.Position = 0;
        JxlBitReader reader = new(stream, TestContext.Current.CancellationToken);
        using JxlEntropyDecoder decoder = JxlEntropyDecoder.Read(reader, 1, Configuration.Default.MemoryAllocator);
        decoder.Begin(reader);
        for (int i = 0; i < distance; i++) { Assert.Equal((uint)i, decoder.Read(reader, 0, width)); }
        for (int i = 0; i < 7; i++) { Assert.Equal((uint)i, decoder.Read(reader, 0, width)); }
        for (int i = 0; i < 103; i++) { Assert.Equal(6U, decoder.Read(reader, 0, width)); }
        decoder.Finish();
    }

    [Fact]
    public void invalid_hybrid_configuration_is_rejected_before_shifts_or_histogram_allocation()
    {
        Assert.Throws<ArgumentException>(() => new JxlEntropyEncoder([], 1, configs: [new(-1, 0, 0)]));
        Assert.Throws<ArgumentException>(() => new JxlEntropyEncoder([], 1, configs: [new(4, int.MaxValue, int.MaxValue)]));
        Assert.Throws<ArgumentException>(() => new JxlEntropyEncoder([new(0, 0, true)], 1));
    }
}
