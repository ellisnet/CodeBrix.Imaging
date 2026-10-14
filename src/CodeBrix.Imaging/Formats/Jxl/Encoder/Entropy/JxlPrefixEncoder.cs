// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from libjxl lib/jxl/enc_huffman.cc and enc_huffman_tree.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Collections.Generic;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Coding;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Entropy;

internal sealed class JxlPrefixEncoder
{
    private readonly record struct Node(ulong Count, int Left, int Right);
    private readonly record struct LengthToken(byte Symbol, byte Extra);
    private readonly int[] symbols;
    internal readonly byte[] Lengths;
    internal readonly ushort[] Codes;
    internal int AlphabetSize => this.Lengths.Length;

    internal JxlPrefixEncoder(ReadOnlySpan<uint> histogram)
    {
        if (histogram.Length is < 1 or > 32768) { throw new ArgumentOutOfRangeException(nameof(histogram)); }
        int length = histogram.Length;
        while (length > 1 && histogram[length - 1] == 0) { length--; }
        List<int> symbols = [];
        for (int i = 0; i < length; i++) { if (histogram[i] != 0) { symbols.Add(i); } }
        if (symbols.Count == 0) { symbols.Add(0); }
        this.symbols = symbols.ToArray();
        this.Lengths = symbols.Count == 1 ? new byte[length] : BuildLengths(histogram[..length], 15);
        this.Codes = BuildCodes(this.Lengths);
    }

    internal JxlPrefixEncoder(ReadOnlySpan<byte> codeLengths)
    {
        if (codeLengths.Length is < 2 or > 32768) { throw new ArgumentOutOfRangeException(nameof(codeLengths)); }
        List<int> symbols = []; int space = 0;
        for (int i = 0; i < codeLengths.Length; i++)
        {
            int bits = codeLengths[i];
            if (bits > 15) { throw new ArgumentOutOfRangeException(nameof(codeLengths)); }
            if (bits != 0) { symbols.Add(i); space += 1 << (15 - bits); }
        }
        if (space != 32768 || symbols.Count < 2) { throw new ArgumentException("Prefix lengths must describe a complete code.", nameof(codeLengths)); }
        this.symbols = symbols.ToArray(); this.Lengths = codeLengths.ToArray(); this.Codes = BuildCodes(this.Lengths);
    }

    internal void WriteSymbol(JxlBitWriter writer, uint symbol)
    {
        if (symbol >= this.Lengths.Length || (this.Lengths[symbol] == 0 && (this.symbols.Length != 1 || this.symbols[0] != symbol)))
        { throw new ArgumentOutOfRangeException(nameof(symbol)); }
        writer.WriteBits(this.Lengths[symbol], this.Codes[symbol]);
    }

    internal void Write(JxlBitWriter writer)
    {
        if (this.AlphabetSize == 1) { return; }
        if (this.symbols.Length <= 4)
        {
            int[] sorted = (int[])this.symbols.Clone();
            Array.Sort(sorted, (a, b) => this.Lengths[a] != this.Lengths[b] ? this.Lengths[a].CompareTo(this.Lengths[b]) : a.CompareTo(b));
            writer.WriteBits(2, 1); writer.WriteBits(2, (uint)sorted.Length - 1);
            int bits = JxlIntegerConfig.ContextBits((uint)this.AlphabetSize - 1);
            foreach (int symbol in sorted) { writer.WriteBits(bits, (uint)symbol); }
            if (sorted.Length == 4) { writer.WriteBool(this.Lengths[sorted[0]] == 1); }
            return;
        }
        List<LengthToken> tokens = TokenizeLengths(this.Lengths);
        uint[] histogram = new uint[18];
        foreach (LengthToken token in tokens) { histogram[token.Symbol]++; }
        byte[] lengths = BuildLengths(histogram, 5);
        ushort[] codes = BuildCodes(lengths);
        int nonzero = 0, single = 0;
        for (int i = 0; i < histogram.Length; i++) { if (histogram[i] != 0) { nonzero++; single = i; } }
        ReadOnlySpan<byte> order = [1, 2, 3, 4, 0, 5, 17, 6, 16, 7, 8, 9, 10, 11, 12, 13, 14, 15];
        ReadOnlySpan<byte> fixedBits = [2, 4, 3, 2, 2, 4], fixedCodes = [0, 7, 3, 2, 1, 15];
        int end = order.Length;
        if (nonzero > 1) { while (lengths[order[end - 1]] == 0) { end--; } }
        int skip = lengths[order[0]] == 0 && lengths[order[1]] == 0 ? lengths[order[2]] == 0 ? 3 : 2 : 0;
        writer.WriteBits(2, (uint)skip);
        for (int i = skip; i < end; i++) { int length = lengths[order[i]]; writer.WriteBits(fixedBits[length], fixedCodes[length]); }
        if (nonzero == 1) { lengths[single] = 0; }
        foreach (LengthToken token in tokens)
        {
            writer.WriteBits(lengths[token.Symbol], codes[token.Symbol]);
            if (token.Symbol is 16 or 17) { writer.WriteBits(token.Symbol == 16 ? 2 : 3, token.Extra); }
        }
    }

    private static byte[] BuildLengths(ReadOnlySpan<uint> histogram, int maximumDepth)
    {
        byte[] lengths = new byte[histogram.Length];
        for (ulong minimum = 1; ; minimum *= 2)
        {
            List<Node> tree = [];
            for (int i = histogram.Length - 1; i >= 0; i--)
            { if (histogram[i] != 0) { tree.Add(new(Math.Max(histogram[i], minimum - 1), -1, i)); } }
            int count = tree.Count;
            if (count == 1) { lengths[tree[0].Right] = 1; return lengths; }
            tree.Sort((a, b) => a.Count != b.Count ? a.Count.CompareTo(b.Count) : b.Right.CompareTo(a.Right));
            Node sentinel = new(ulong.MaxValue, -1, -1);
            tree.Add(sentinel); tree.Add(sentinel);
            int leaf = 0, parent = count + 1;
            for (int k = count - 1; k > 0; k--)
            {
                int left = tree[leaf].Count <= tree[parent].Count ? leaf++ : parent++;
                int right = tree[leaf].Count <= tree[parent].Count ? leaf++ : parent++;
                tree[^1] = new(tree[left].Count + tree[right].Count, left, right);
                tree.Add(sentinel);
            }
            Stack<(int Node, int Depth)> pending = new(); pending.Push(((2 * count) - 1, 0));
            int deepest = 0;
            while (pending.TryPop(out (int Node, int Depth) entry))
            {
                Node node = tree[entry.Node];
                if (node.Left < 0) { lengths[node.Right] = checked((byte)entry.Depth); deepest = Math.Max(deepest, entry.Depth); }
                else { pending.Push((node.Left, entry.Depth + 1)); pending.Push((node.Right, entry.Depth + 1)); }
            }
            if (deepest <= maximumDepth) { return lengths; }
        }
    }

    private static ushort[] BuildCodes(ReadOnlySpan<byte> lengths)
    {
        Span<int> count = stackalloc int[16], next = stackalloc int[16];
        foreach (byte length in lengths) { count[length]++; }
        count[0] = 0;
        int code = 0;
        for (int i = 1; i < count.Length; i++) { code = (code + count[i - 1]) << 1; next[i] = code; }
        ushort[] codes = new ushort[lengths.Length];
        for (int i = 0; i < lengths.Length; i++)
        {
            int length = lengths[i], bits = next[length]++, reversed = 0;
            for (int k = 0; k < length; k++) { reversed = (reversed << 1) | ((bits >> k) & 1); }
            codes[i] = (ushort)reversed;
        }
        return codes;
    }

    private static List<LengthToken> TokenizeLengths(ReadOnlySpan<byte> lengths)
    {
        int end = lengths.Length;
        while (end != 0 && lengths[end - 1] == 0) { end--; }
        int zeroTotal = 0, otherTotal = 0, zeroRuns = 1, otherRuns = 1;
        if (lengths.Length > 50)
        {
            for (int i = 0; i < end;)
            {
                int run = 1;
                while (i + run < end && lengths[i + run] == lengths[i]) { run++; }
                if (run >= 3 && lengths[i] == 0) { zeroTotal += run; zeroRuns++; }
                if (run >= 4 && lengths[i] != 0) { otherTotal += run; otherRuns++; }
                i += run;
            }
        }
        bool zeroRle = zeroTotal > zeroRuns * 2, otherRle = otherTotal > otherRuns * 2;
        List<LengthToken> result = [];
        byte previous = 8;
        for (int i = 0; i < end;)
        {
            byte value = lengths[i];
            int run = 1;
            if (value == 0 ? zeroRle : otherRle)
            { while (i + run < end && lengths[i + run] == value) { run++; } }
            i += run;
            if (value != 0 && value != previous) { result.Add(new(value, 0)); run--; }
            if (run == (value == 0 ? 11 : 7)) { result.Add(new(value, 0)); run--; }
            if (run < 3) { for (int k = 0; k < run; k++) { result.Add(new(value, 0)); } }
            else
            {
                int start = result.Count, bits = value == 0 ? 3 : 2;
                run -= 3;
                while (true)
                {
                    result.Add(new(value == 0 ? (byte)17 : (byte)16, (byte)(run & ((1 << bits) - 1))));
                    run >>= bits;
                    if (run == 0) { break; }
                    run--;
                }
                result.Reverse(start, result.Count - start);
            }
            if (value != 0) { previous = value; }
        }
        return result;
    }
}
