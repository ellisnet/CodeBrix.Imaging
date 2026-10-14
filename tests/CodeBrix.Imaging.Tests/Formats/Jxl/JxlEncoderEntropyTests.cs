// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Coding;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Entropy;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlEncoderEntropyTests
{
    [Fact]
    public void flat_context_maps_cover_every_alphabet_size()
    {
        // 130 equal-frequency symbols round upward to 32 states each at
        // ANS precision zero, initially exceeding the 4096-state table.
        // Normalization must retain all symbols while repairing that excess.
        for (int clusters = 1; clusters <= 256; clusters++)
        {
            byte[] map = new byte[clusters];
            for (int i = 0; i < clusters; i++) { map[i] = (byte)i; }
            VerifyContextMap(map, clusters);
        }
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(7)] [InlineData(8)] [InlineData(256)]
    public void entropy_context_maps_preserve_runs_and_following_bits(int clusters)
    {
        byte[] map = new byte[clusters * 47];
        for (int i = 0; i < map.Length; i++) { map[i] = (byte)(i / 47); }
        VerifyContextMap(map, clusters);
        for (int i = 0; i < map.Length; i++) { map[i] = (byte)(i % clusters); }
        VerifyContextMap(map, clusters);
        VerifyContextMap(clusters == 1 ? [0] : [0, 1], Math.Min(clusters, 2));
    }

    [Fact]
    public void sparse_ac_context_map_does_not_pay_one_bit_per_unused_context()
    {
        byte[] map = new byte[7425];
        map[13] = 1; map[519] = 2; map[3217] = 3; map[7139] = 1;
        long bits = VerifyContextMap(map, 4);
        Assert.True(bits < 1200, $"Sparse map costs {bits} bits; zero runs should be compressed.");
    }

    private static long VerifyContextMap(byte[] map, int clusters)
    {
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream);
        JxlEntropyEncoder.WriteContextMap(writer, map); long bits = writer.Position;
        writer.WriteBits(21, 0x135792); writer.ZeroPadToByte(); stream.Position = 0;
        JxlBitReader reader = new(stream, TestContext.Current.CancellationToken);
        byte[] decoded = JxlEntropyDecoder.ReadClusters(reader, map.Length, Configuration.Default.MemoryAllocator, out int count);
        Assert.Equal(clusters, count); Assert.Equal(map, decoded);
        Assert.Equal(bits, reader.Position); Assert.Equal(0x135792U, reader.ReadBits(21));
        return bits;
    }

    [Theory]
    [InlineData(true, 1)] [InlineData(false, 1)] [InlineData(true, 6)] [InlineData(false, 6)]
    [InlineData(true, 12)] [InlineData(false, 12)]
    public void decoder_speed_histogram_limits_preserve_sparse_full_width_tokens(bool prefix, int maximum)
    {
        const int contexts = 990;
        JxlToken[] tokens = new JxlToken[9000]; Random random = new(75021);
        for (int i = 0; i < tokens.Length; i++) { tokens[i] = new(i % 19 * 37, (uint)random.NextInt64(1L << (i % 33))); }
        JxlEntropyEncoder encoder = JxlEntropyEncoder.CreateOptimized(tokens, contexts, prefix, 9, false, TestContext.Current.CancellationToken, maximum);
        foreach (byte cluster in encoder.Clusters) { Assert.InRange(cluster, (byte)0, (byte)(maximum - 1)); }
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream);
        encoder.WriteHeader(writer); encoder.WriteTokens(writer, tokens, TestContext.Current.CancellationToken);
        writer.WriteBits(21, 0x135792); writer.ZeroPadToByte(); stream.Position = 0;
        JxlBitReader reader = new(stream, TestContext.Current.CancellationToken);
        using JxlEntropyDecoder decoder = JxlEntropyDecoder.Read(reader, contexts, Configuration.Default.MemoryAllocator);
        decoder.Begin(reader);
        foreach (JxlToken token in tokens) { Assert.Equal(token.Value, decoder.Read(reader, token.Context)); }
        decoder.Finish(); Assert.Equal(0x135792U, reader.ReadBits(21));
    }

    [Theory]
    [InlineData(true, 2)] [InlineData(false, 2)]
    [InlineData(true, 7)] [InlineData(false, 7)] [InlineData(false, 10)]
    public void optimized_histograms_keep_distinct_distributions_beyond_256_contexts(bool prefix, int effort)
    {
        // Arrange: all three used contexts collide under a modulo-256 mapping.
        const int contexts = 768;
        JxlToken[] tokens = new JxlToken[12000];
        for (int i = 0; i < tokens.Length; i++) { int kind = i % 3; tokens[i] = new(17 + kind * 256, (uint)kind); }
        JxlEntropyEncoder baseline = new(tokens, contexts, usePrefix: prefix, effort: effort);

        // Act: choose histograms using the distributions, including sparse contexts.
        JxlEntropyEncoder encoder = JxlEntropyEncoder.CreateOptimized(tokens, contexts, prefix, effort, false, TestContext.Current.CancellationToken);

        // Assert: constant contexts should cost far less than a mixed distribution.
        Assert.True(encoder.GetBitCount(tokens, TestContext.Current.CancellationToken) * 4 < baseline.GetBitCount(tokens, TestContext.Current.CancellationToken));
        Assert.NotEqual(encoder.Clusters[17], encoder.Clusters[273]);
        Assert.NotEqual(encoder.Clusters[273], encoder.Clusters[529]);
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream);
        encoder.WriteHeader(writer); encoder.WriteTokens(writer, tokens, TestContext.Current.CancellationToken);
        Assert.Equal(encoder.GetBitCount(tokens, TestContext.Current.CancellationToken), writer.Position);
        writer.WriteBits(21, 0x135792); writer.ZeroPadToByte(); stream.Position = 0;
        JxlBitReader reader = new(stream, TestContext.Current.CancellationToken);
        using JxlEntropyDecoder decoder = JxlEntropyDecoder.Read(reader, contexts, Configuration.Default.MemoryAllocator);
        decoder.Begin(reader);
        foreach (JxlToken token in tokens) { Assert.Equal(token.Value, decoder.Read(reader, token.Context)); }
        decoder.Finish(); Assert.Equal(0x135792U, reader.ReadBits(21));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(41)]
    [InlineData(256)]
    [InlineData(4096)]
    public void clustered_prefix_stream_preserves_contexts_and_full_width_values(int contexts)
    {
        Random random = new(66794);
        JxlToken[] tokens = new JxlToken[4096];
        for (int i = 0; i < tokens.Length; i++) { tokens[i] = new(i % contexts, i < 32 ? uint.MaxValue >> i : (uint)random.NextInt64(0, 1L << (i % 32))); }
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream);
        JxlEntropyEncoder encoder = new(tokens, contexts);
        encoder.WriteHeader(writer); encoder.WriteTokens(writer, tokens, TestContext.Current.CancellationToken);
        writer.WriteBits(21, 0x135792); writer.ZeroPadToByte(); stream.Position = 0;
        JxlBitReader reader = new(stream, TestContext.Current.CancellationToken);
        using JxlEntropyDecoder decoder = JxlEntropyDecoder.Read(reader, contexts, Configuration.Default.MemoryAllocator);
        decoder.Begin(reader);
        foreach (JxlToken token in tokens) { Assert.Equal(token.Value, decoder.Read(reader, token.Context)); }
        decoder.Finish(); Assert.Equal(0x135792U, reader.ReadBits(21));
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 3, 2)]
    [InlineData(3, 5, 4)]
    [InlineData(41, 19, 7)]
    [InlineData(8, 255, 10)]
    public void ans_histograms_and_reversed_streams_preserve_tokens(int contexts, int alphabet, int effort)
    {
        Random random = new(98117);
        JxlToken[] tokens = new JxlToken[3000];
        for (int i = 0; i < tokens.Length; i++)
        { tokens[i] = new(random.Next(contexts), i % 4 == 0 ? 0 : (uint)random.Next(alphabet)); }
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream);
        JxlEntropyEncoder encoder = new(tokens, contexts, usePrefix: false, effort: effort);
        encoder.WriteHeader(writer); encoder.WriteTokens(writer, tokens, TestContext.Current.CancellationToken);
        writer.WriteBits(21, 0x135792); writer.ZeroPadToByte(); stream.Position = 0;
        JxlBitReader reader = new(stream, TestContext.Current.CancellationToken);
        using JxlEntropyDecoder decoder = JxlEntropyDecoder.Read(reader, contexts, Configuration.Default.MemoryAllocator);
        decoder.Begin(reader);
        foreach (JxlToken token in tokens) { Assert.Equal(token.Value, decoder.Read(reader, token.Context)); }
        decoder.Finish(); Assert.Equal(0x135792U, reader.ReadBits(21));
    }

    [Theory]
    [InlineData(5, 2, 0)] [InlineData(6, 1, 2)] [InlineData(7, 3, 1)]
    public void ans_alphabet_keeps_nondefault_hybrid_fields_even_when_symbols_are_small(int split, int msb, int lsb)
    {
        JxlToken[] tokens = new JxlToken[900];
        for (int i = 0; i < tokens.Length; i++) { tokens[i] = new(0, (uint)(i % 7)); }
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream);
        JxlEntropyEncoder encoder = new(tokens, 1, configs: [new(split, msb, lsb)], usePrefix: false);
        encoder.WriteHeader(writer); encoder.WriteTokens(writer, tokens, TestContext.Current.CancellationToken);
        Assert.Equal(encoder.GetBitCount(tokens, TestContext.Current.CancellationToken), writer.Position);
        writer.WriteBits(17, 0x17593); writer.ZeroPadToByte(); stream.Position = 0;
        JxlBitReader reader = new(stream, TestContext.Current.CancellationToken);
        using JxlEntropyDecoder decoder = JxlEntropyDecoder.Read(reader, 1, Configuration.Default.MemoryAllocator);
        decoder.Begin(reader);
        foreach (JxlToken token in tokens) { Assert.Equal(token.Value, decoder.Read(reader, 0)); }
        decoder.Finish(); Assert.Equal(0x17593U, reader.ReadBits(17));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(64, false)]
    [InlineData(255, true)]
    [InlineData(32768, false)]
    [InlineData(32768, true)]
    public void prefix_trees_decode_all_used_symbols_and_preserve_the_following_bits(int alphabet, bool sparse)
    {
        uint[] histogram = new uint[alphabet];
        for (int i = 0; i < alphabet; i++)
        { histogram[i] = sparse && i % 17 != 0 && i != alphabet - 1 ? 0U : (uint)((i % 11) + 1); }
        VerifyPrefix(histogram);
    }

    [Fact]
    public void prefix_lengths_are_bounded_for_skewed_and_large_counts()
    {
        uint[] histogram = new uint[40];
        histogram[0] = histogram[1] = 1;
        for (int i = 2; i < histogram.Length; i++) { histogram[i] = histogram[i - 1] + histogram[i - 2]; }
        VerifyPrefix(histogram);
        Array.Fill(histogram, uint.MaxValue); VerifyPrefix(histogram);
        Array.Clear(histogram); histogram[39] = 100; VerifyPrefix(histogram);
    }

    [Fact]
    public void all_hybrid_configurations_preserve_uint_boundaries_and_random_values()
    {
        Random random = new(431909);
        for (int split = 0; split <= 15; split++)
        {
            for (int msb = 0; msb <= split; msb++)
            {
                for (int lsb = 0; lsb <= split - msb; lsb++)
                {
                    if (split == 15 && (msb != 0 || lsb != 0)) { continue; }
                    JxlIntegerConfig config = new(split, msb, lsb);
                    uint[] values = new uint[72];
                    for (int i = 0; i < 32; i++) { values[i * 2] = (1U << i) - 1; values[(i * 2) + 1] = 1U << i; }
                    values[64] = uint.MaxValue;
                    for (int i = 65; i < values.Length; i++) { values[i] = (uint)random.NextInt64(0, 1L << 32); }
                    using MemoryStream stream = new();
                    JxlBitWriter writer = new(stream); config.Write(writer, 15);
                    uint[] tokens = new uint[values.Length];
                    for (int i = 0; i < values.Length; i++)
                    { (uint token, int bits, uint extra) = config.Encode(values[i]); tokens[i] = token; writer.WriteBits(bits, extra); }
                    long end = writer.Position; writer.ZeroPadToByte(); stream.Position = 0;
                    JxlBitReader reader = new(stream, TestContext.Current.CancellationToken);
                    JxlIntegerConfig decoded = JxlIntegerConfig.Read(reader, 15); Assert.Equal(config, decoded);
                    for (int i = 0; i < values.Length; i++) { Assert.Equal(values[i], decoded.Decode(reader, tokens[i])); }
                    Assert.Equal(end, reader.Position);
                }
            }
        }
    }

    private static void VerifyPrefix(uint[] histogram)
    {
        JxlPrefixEncoder encoder = new(histogram);
        foreach (byte length in encoder.Lengths) { Assert.InRange(length, 0, 15); }
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream);
        encoder.Write(writer);
        for (int repeat = 0; repeat < 2; repeat++)
        { for (int i = histogram.Length - 1; i >= 0; i--) { if (histogram[i] != 0) { encoder.WriteSymbol(writer, (uint)i); } } }
        writer.WriteBits(17, 0x15935); writer.ZeroPadToByte(); stream.Position = 0;
        JxlBitReader reader = new(stream, TestContext.Current.CancellationToken);
        JxlPrefixHistogram decoder = JxlPrefixHistogram.Read(reader, encoder.AlphabetSize);
        for (int repeat = 0; repeat < 2; repeat++)
        { for (int i = histogram.Length - 1; i >= 0; i--) { if (histogram[i] != 0) { Assert.Equal((uint)i, decoder.ReadSymbol(reader)); } } }
        Assert.Equal(0x15935U, reader.ReadBits(17));
    }
}
