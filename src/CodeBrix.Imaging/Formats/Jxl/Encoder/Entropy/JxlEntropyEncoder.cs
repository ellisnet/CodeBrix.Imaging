// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Inverse field grammar of the jxl-oxide-derived managed entropy decoder.
// Prefix histogram writing uses the separately attributed libjxl port.
// Context-map MTF, entropy and run-length candidate selection follows libjxl
// enc_context_map.cc and enc_ans_params.h, ef67fde2ec16d52e644c5a0969230b9a85e3eb31
// (BSD-3-Clause plus PATENTS). Copyright (c) the JPEG XL Project Authors.

using System;
using System.IO;
using System.Numerics;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Coding;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Entropy;

internal readonly record struct JxlToken
{
    private readonly int contextAndLength;
    internal int Context => this.contextAndLength & int.MaxValue;
    internal uint Value { get; }
    internal bool IsLzLength => this.contextAndLength < 0;

    internal JxlToken(int context, uint value, bool isLzLength = false)
    {
        if (context < 0) { throw new ArgumentOutOfRangeException(nameof(context)); }
        this.contextAndLength = context | (isLzLength ? int.MinValue : 0); this.Value = value;
    }
}

internal sealed class JxlEntropyEncoder
{
    internal readonly byte[] Clusters;
    private readonly JxlIntegerConfig[] configs;
    private readonly JxlPrefixEncoder[] prefix;
    private readonly JxlAnsEncoder[] ans;
    private readonly int alphabetBits;
    private readonly bool lz77;
    internal const uint MinLzSymbol = 224, MinLzLength = 3;
    internal static readonly JxlIntegerConfig LengthConfig = new(0, 0, 0);

    private JxlEntropyEncoder(JxlPrefixEncoder bytePrefix, int contexts)
    {
        this.Clusters = new byte[contexts]; this.configs = [new(8, 0, 0)];
        this.prefix = [bytePrefix]; this.alphabetBits = 15;
    }

    // Large byte payloads do not need twelve-byte tokens or reverse ANS scratch.
    // A shared literal prefix distribution can be collected and written in two
    // forward passes with storage independent of the input length.
    internal static void WriteBytePrefixStream(JxlBitWriter writer, ReadOnlySpan<byte> bytes, int contexts, CancellationToken cancellationToken)
    {
        Span<uint> histogram = stackalloc uint[256]; histogram.Clear();
        for (int i = 0; i < bytes.Length; i++)
        {
            if ((i & 4095) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
            histogram[bytes[i]]++;
        }
        JxlPrefixEncoder prefix = new(histogram);
        new JxlEntropyEncoder(prefix, contexts).WriteHeader(writer);
        for (int i = 0; i < bytes.Length; i++)
        {
            if ((i & 4095) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
            prefix.WriteSymbol(writer, bytes[i]);
        }
    }

    internal JxlEntropyEncoder(ReadOnlySpan<JxlToken> tokens, int contexts, byte[] clusters = null, JxlIntegerConfig[] configs = null,
        bool usePrefix = true, int effort = 7, bool lz77 = false)
        : this(new JxlTokenSequence(tokens), contexts, clusters, configs, usePrefix, effort, lz77) { }

    internal JxlEntropyEncoder(JxlTokenSequence tokens, int contexts, byte[] clusters = null, JxlIntegerConfig[] configs = null,
        bool usePrefix = true, int effort = 7, bool lz77 = false)
    {
        if (contexts is < 1 or > 1 << 20) { throw new ArgumentOutOfRangeException(nameof(contexts)); }
        this.lz77 = lz77;
        if (lz77) { contexts++; }
        this.Clusters = clusters == null ? new byte[contexts] : (byte[])clusters.Clone();
        if (clusters == null) { for (int i = 0; i < contexts; i++) { this.Clusters[i] = (byte)(i % Math.Min(256, contexts)); } }
        if (this.Clusters.Length != contexts) { throw new ArgumentException("JPEG XL context map size mismatch.", nameof(clusters)); }
        int count = 1;
        foreach (byte cluster in this.Clusters) { count = Math.Max(count, cluster + 1); }
        Span<bool> used = stackalloc bool[count];
        foreach (byte cluster in this.Clusters) { used[cluster] = true; }
        foreach (bool present in used) { if (!present) { throw new ArgumentException("JPEG XL context map contains a hole.", nameof(clusters)); } }
        this.configs = configs == null ? new JxlIntegerConfig[count] : (JxlIntegerConfig[])configs.Clone();
        if (configs == null) { Array.Fill(this.configs, new(4, 1, 0)); }
        if (this.configs.Length != count) { throw new ArgumentException("JPEG XL integer configuration count mismatch.", nameof(configs)); }
        foreach (JxlIntegerConfig config in this.configs) { config.Validate(usePrefix ? 15 : 8); }
        int[] sizes = new int[count]; Array.Fill(sizes, 1);
        foreach (JxlToken token in tokens)
        {
            if ((uint)token.Context >= contexts) { throw new ArgumentOutOfRangeException(nameof(tokens)); }
            int cluster = this.Clusters[token.Context];
            uint symbol = this.EncodeToken(token).Token;
            if (symbol >= (usePrefix ? 32768 : 256)) { throw new ArgumentException("JPEG XL entropy alphabet is too large.", nameof(configs)); }
            sizes[cluster] = Math.Max(sizes[cluster], (int)symbol + 1);
        }
        uint[][] histograms = new uint[count][];
        for (int i = 0; i < count; i++) { histograms[i] = new uint[sizes[i]]; }
        foreach (JxlToken token in tokens)
        { int cluster = this.Clusters[token.Context]; histograms[cluster][this.EncodeToken(token).Token]++; }
        if (usePrefix)
        {
            this.alphabetBits = 15; this.prefix = new JxlPrefixEncoder[count];
            for (int i = 0; i < count; i++) { this.prefix[i] = new(histograms[i]); }
        }
        else
        {
            this.alphabetBits = 5;
            foreach (int size in sizes) { this.alphabetBits = Math.Max(this.alphabetBits, JxlIntegerConfig.ContextBits((uint)size - 1)); }
            foreach (JxlIntegerConfig config in this.configs)
            { this.alphabetBits = Math.Max(this.alphabetBits, config.SplitExponent + ((config.Msb | config.Lsb) == 0 ? 0 : 1)); }
            this.ans = new JxlAnsEncoder[count];
            for (int i = 0; i < count; i++) { this.ans[i] = new(histograms[i], this.alphabetBits, effort); }
        }
    }

    internal void WriteHeader(JxlBitWriter writer)
    {
        writer.WriteBool(this.lz77);
        if (this.lz77)
        {
            writer.WriteU32(MinLzSymbol, 224, 512, 4096, JxlU32.U(15, 8));
            writer.WriteU32(MinLzLength, 3, 4, JxlU32.U(2, 5), JxlU32.U(8, 9));
            LengthConfig.Write(writer, 8);
        }
        WriteContextMap(writer, this.Clusters);
        writer.WriteBool(this.prefix != null);
        if (this.prefix == null) { writer.WriteBits(2, (uint)this.alphabetBits - 5); }
        foreach (JxlIntegerConfig config in this.configs) { config.Write(writer, this.alphabetBits); }
        if (this.ans != null) { foreach (JxlAnsEncoder histogram in this.ans) { histogram.Write(writer); } return; }
        foreach (JxlPrefixEncoder histogram in this.prefix)
        {
            uint size = (uint)histogram.AlphabetSize - 1;
            writer.WriteBool(size != 0);
            if (size != 0)
            { int bits = BitOperations.Log2(size); writer.WriteBits(4, (uint)bits); writer.WriteBits(bits, size - (1U << bits)); }
        }
        foreach (JxlPrefixEncoder histogram in this.prefix) { histogram.Write(writer); }
    }

    internal void WriteTokens(JxlBitWriter writer, ReadOnlySpan<JxlToken> tokens, CancellationToken cancellationToken = default)
    {
        uint[] renormalization = null;
        if (this.ans != null)
        {
            renormalization = new uint[tokens.Length]; uint state = 0x130000;
            for (int i = tokens.Length - 1; i >= 0; i--)
            {
                if ((i & 1023) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
                JxlToken token = tokens[i]; int cluster = this.Clusters[token.Context];
                renormalization[i] = this.ans[cluster].PutSymbol(ref state, this.EncodeToken(token).Token);
            }
            writer.WriteBits(32, state);
        }
        for (int i = 0; i < tokens.Length; i++)
        {
            if ((i & 1023) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
            JxlToken token = tokens[i]; int cluster = this.Clusters[token.Context];
            (uint symbol, int bits, uint extra) = this.EncodeToken(token);
            if (renormalization == null) { this.prefix[cluster].WriteSymbol(writer, symbol); }
            else if (renormalization[i] != 0) { writer.WriteBits(16, renormalization[i] & 65535); }
            writer.WriteBits(bits, extra);
        }
    }

    internal long GetBitCount(ReadOnlySpan<JxlToken> tokens, CancellationToken cancellationToken = default)
        => this.GetBitCount(new JxlTokenSequence(tokens), cancellationToken);

    internal long GetBitCount(JxlTokenSequence tokens, CancellationToken cancellationToken = default)
    {
        JxlBitWriter counter = new(Stream.Null); this.WriteHeader(counter);
        long bits = counter.Position; uint state = 0x130000;
        if (this.ans != null) { bits += 32; }
        uint position = 0;
        foreach (JxlToken token in tokens.Reversed())
        {
            if ((position++ & 1023) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
            int cluster = this.Clusters[token.Context];
            (uint symbol, int extra, _) = this.EncodeToken(token);
            bits += extra;
            bits += this.prefix != null ? this.prefix[cluster].Lengths[symbol] : this.ans[cluster].PutSymbol(ref state, symbol) == 0 ? 0 : 16;
        }
        return bits;
    }

    internal static bool WriteCompressed(JxlBitWriter writer, JxlToken[] tokens, int contexts, uint width,
        bool usePrefix, int effort, CancellationToken cancellationToken, int maximumHistograms = 256)
    {
        JxlEntropyEncoder selected = CreateOptimized(tokens, contexts, usePrefix, effort, false, cancellationToken, maximumHistograms);
        JxlToken[] matched = JxlLz77.Match(tokens, contexts, width, effort, usePrefix, cancellationToken);
        bool useLz = false;
        if (matched.Length != 0)
        {
            JxlEntropyEncoder alternative = CreateOptimized(matched, contexts, usePrefix, effort, true, cancellationToken, maximumHistograms);
            if (alternative.GetBitCount(matched, cancellationToken) < selected.GetBitCount(tokens, cancellationToken))
            { selected = alternative; tokens = matched; useLz = true; }
        }
        selected.WriteHeader(writer); selected.WriteTokens(writer, tokens, cancellationToken);
        return useLz;
    }

    internal static JxlEntropyEncoder CreateOptimized(JxlToken[] tokens, int contexts, bool prefix, int effort, bool lz77,
        CancellationToken cancellationToken, int maximumHistograms = 256)
        => CreateOptimized(new JxlTokenSequence(tokens), contexts, prefix, effort, lz77, cancellationToken, maximumHistograms);

    internal static JxlEntropyEncoder CreateOptimized(JxlTokenSequence tokens, int contexts, bool prefix, int effort, bool lz77,
        CancellationToken cancellationToken, int maximumHistograms = 256)
    {
        if (maximumHistograms is < 1 or > 256) { throw new ArgumentOutOfRangeException(nameof(maximumHistograms)); }
        byte[] baseline = null;
        if (maximumHistograms < 256)
        {
            baseline = new byte[contexts + (lz77 ? 1 : 0)];
            for (int i = 0; i < baseline.Length; i++) { baseline[i] = (byte)(i % maximumHistograms); }
        }
        JxlEntropyEncoder original = new(tokens, contexts, baseline, usePrefix: prefix, effort: effort, lz77: lz77);
        if ((effort <= 2 && contexts <= maximumHistograms) || tokens.Length == 0) { return original; }
        byte[] map = JxlHistogramOptimizer.Cluster(tokens, contexts + (lz77 ? 1 : 0), prefix, cancellationToken, effort, maximumHistograms);
        JxlIntegerConfig[] configs = effort <= 2 ? null : JxlHistogramOptimizer.ChooseConfigs(tokens, map, prefix, lz77, effort, cancellationToken);
        JxlEntropyEncoder candidate = new(tokens, contexts, map, configs, prefix, effort, lz77);
        return candidate.GetBitCount(tokens, cancellationToken) < original.GetBitCount(tokens, cancellationToken) ? candidate : original;
    }

    private (uint Token, int ExtraBits, uint Extra) EncodeToken(JxlToken token)
    {
        if (token.IsLzLength)
        {
            if (!this.lz77 || token.Context == this.Clusters.Length - 1) { throw new ArgumentException("Unexpected JPEG XL LZ77 length token."); }
            (uint symbol, int bits, uint extra) = LengthConfig.Encode(token.Value);
            return (symbol + MinLzSymbol, bits, extra);
        }
        var encoded = this.configs[this.Clusters[token.Context]].Encode(token.Value);
        if (this.lz77 && token.Context != this.Clusters.Length - 1 && encoded.Token >= MinLzSymbol)
        { throw new ArgumentException("JPEG XL literal overlaps the LZ77 alphabet."); }
        return encoded;
    }

    internal static void WriteContextMap(JxlBitWriter writer, ReadOnlySpan<byte> map)
    {
        if (map.Length == 1) { return; }
        uint maximum = 0;
        foreach (byte value in map) { maximum = Math.Max(maximum, value); }
        int bits = JxlIntegerConfig.ContextBits(maximum);
        if (bits == 0) { writer.WriteBool(true); writer.WriteBits(2, 0); return; }
        if (map.Length <= 2 && bits <= 3)
        { writer.WriteBool(true); writer.WriteBits(2, (uint)bits); foreach (byte value in map) { writer.WriteBits(bits, value); } return; }
        JxlToken[] tokens = new JxlToken[map.Length];
        for (int i = 0; i < map.Length; i++) { tokens[i] = new(0, map[i]); }
        JxlToken[] original = tokens;
        JxlEntropyEncoder encoder = new(tokens, 1, configs: [new(8, 0, 0)]);
        long cost = 2 + encoder.GetBitCount(tokens);
        bool moveToFront = false;
        JxlToken[] moved = new JxlToken[map.Length]; Span<byte> order = stackalloc byte[256];
        for (int i = 0; i < order.Length; i++) { order[i] = (byte)i; }
        for (int i = 0; i < map.Length; i++)
        {
            byte value = map[i]; int index = order.IndexOf(value); moved[i] = new(0, (uint)index);
            order[..index].CopyTo(order[1..]); order[0] = value;
        }
        JxlEntropyEncoder alternative = new(moved, 1, configs: [new(8, 0, 0)]);
        long alternativeCost = 2 + alternative.GetBitCount(moved);
        if (alternativeCost < cost) { cost = alternativeCost; tokens = moved; encoder = alternative; moveToFront = true; }
        foreach (bool mtf in new[] { false, true })
        {
            JxlToken[] values = mtf ? moved : original;
            Consider(new(values, 1, configs: [new(8, 0, 0)], usePrefix: false), values, mtf);
            // An LZ model has two contexts. Their own tiny map cannot use LZ,
            // which also keeps this recursive field grammar bounded.
            JxlToken[] matched = map.Length > 2 ? JxlLz77.Match(values, 1, 0, 1, true, default) : [];
            if (matched.Length == 0) { continue; }
            foreach (bool prefix in new[] { false, true })
            {
                Consider(new(matched, 1, clusters: [0, 1], configs: [new(4, 1, 0), new(4, 1, 0)], usePrefix: prefix, lz77: true), matched, mtf);
                Consider(new(matched, 1, clusters: [0, 0], configs: [new(4, 1, 0)], usePrefix: prefix, lz77: true), matched, mtf);
            }
        }
        bool simple = bits <= 3 && 3L + ((long)bits * map.Length) <= cost;
        writer.WriteBool(simple);
        if (simple)
        { writer.WriteBits(2, (uint)bits); foreach (byte value in map) { writer.WriteBits(bits, value); } return; }
        writer.WriteBool(moveToFront);
        encoder.WriteHeader(writer); encoder.WriteTokens(writer, tokens);

        void Consider(JxlEntropyEncoder candidate, JxlToken[] values, bool mtf)
        {
            long candidateCost = 2 + candidate.GetBitCount(values);
            if (candidateCost < cost) { cost = candidateCost; tokens = values; encoder = candidate; moveToFront = mtf; }
        }
    }
}
