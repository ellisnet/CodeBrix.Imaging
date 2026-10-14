// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from libjxl lib/jxl/enc_ans.cc, enc_ans.h and ans_common.h,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Coding;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Entropy;

internal sealed partial class JxlAnsEncoder
{
    private readonly int[] counts;
    private readonly int method, omitted;
    private readonly int[] symbols;
    private readonly ushort[][] reverse;
    internal double Cost { get; }

    internal JxlAnsEncoder(ReadOnlySpan<uint> histogram, int alphabetBits, int effort)
    {
        if (histogram.Length is < 1 or > 256 || alphabetBits is < 5 or > 8 || histogram.Length > 1 << alphabetBits)
        { throw new ArgumentOutOfRangeException(nameof(histogram)); }
        int length = histogram.Length;
        while (length > 1 && histogram[length - 1] == 0) { length--; }
        histogram = histogram[..length];
        List<int> symbols = []; long total = 0;
        for (int i = 0; i < length; i++) { if (histogram[i] != 0) { symbols.Add(i); total += histogram[i]; } }
        if (total > int.MaxValue) { throw new ArgumentOutOfRangeException(nameof(histogram), "ANS histogram count exceeds the token limit."); }
        if (symbols.Count == 0) { symbols.Add(0); }
        this.symbols = symbols.ToArray();
        if (symbols.Count == 1)
        {
            this.counts = new int[length]; this.counts[symbols[0]] = 4096; this.method = 1;
            this.Cost = this.HeaderCost();
        }
        else
        {
            int[] best = new int[length];
            for (int i = 0; i < length; i++) { best[i] = (4096 / length) + (i < 4096 % length ? 1 : 0); }
            this.counts = best;
            double bestCost = this.HeaderCost() + DataCost(histogram, best);
            int bestMethod = 0, bestOmitted = 0;
            for (int shift = 0; shift < 12; shift++)
            {
                bool selected = effort >= 8 || (effort >= 4 ? shift % 2 == 0 || shift == 11 : shift is 0 or 6 or 11);
                if (!selected) { continue; }
                (int[] candidate, int omitted) = Normalize(histogram, total, shift);
                this.counts = candidate; this.omitted = omitted; this.method = shift + 1;
                double cost = this.HeaderCost() + DataCost(histogram, candidate);
                if (cost < bestCost) { bestCost = cost; best = candidate; bestMethod = this.method; bestOmitted = omitted; }
            }
            this.counts = best; this.method = bestMethod; this.omitted = bestOmitted;
            this.Cost = bestCost;
        }
        this.reverse = JxlAnsHistogram.CreateReverseMap(this.counts, alphabetBits);
    }

    internal uint PutSymbol(ref uint state, uint symbol)
    {
        uint frequency = (uint)this.counts[symbol], renormalization = 0;
        if (frequency == 0) { throw new ArgumentOutOfRangeException(nameof(symbol)); }
        if (state >> 20 >= frequency) { renormalization = (state & 65535) | 65536; state >>= 16; }
        state = ((state / frequency) << 12) + this.reverse[symbol][state % frequency];
        return renormalization;
    }

    internal void Write(JxlBitWriter writer)
    {
        if (this.method == 0)
        { writer.WriteBool(false); writer.WriteBool(true); WriteU8(writer, this.counts.Length - 1); return; }
        if (this.symbols.Length <= 2)
        {
            writer.WriteBool(true); writer.WriteBool(this.symbols.Length == 2);
            foreach (int symbol in this.symbols) { WriteU8(writer, symbol); }
            if (this.symbols.Length == 2) { writer.WriteBits(12, (uint)this.counts[this.symbols[0]]); }
            return;
        }
        writer.WriteBool(false); writer.WriteBool(false);
        int log = BitOperations.Log2((uint)this.method);
        writer.WriteBits(log, (1U << log) - 1);
        if (log != 3) { writer.WriteBool(false); }
        writer.WriteBits(log, (uint)this.method & ((1U << log) - 1));
        WriteU8(writer, this.counts.Length - 3);
        int[] same = new int[this.counts.Length], widths = new int[this.counts.Length];
        int last = 0;
        for (int i = 1; i <= this.counts.Length; i++)
        {
            if (i == this.counts.Length || i == this.omitted || i == this.omitted + 1 || this.counts[i] != this.counts[last])
            { same[last] = i - last; last = i; }
        }
        int omitWidth = 10;
        for (int i = 0; i < this.counts.Length; i++)
        {
            if (i == this.omitted || this.counts[i] == 0) { continue; }
            widths[i] = BitOperations.Log2((uint)this.counts[i]) + 1;
            omitWidth = Math.Max(omitWidth, widths[i] + (i < this.omitted ? 1 : 0));
        }
        widths[this.omitted] = omitWidth;
        ReadOnlySpan<byte> lengths = [5, 4, 4, 4, 4, 4, 3, 3, 3, 3, 3, 6, 7, 7];
        ReadOnlySpan<byte> codes = [17, 11, 15, 3, 9, 7, 4, 2, 5, 6, 0, 33, 1, 65];
        for (int i = 0; i < this.counts.Length; i++)
        {
            writer.WriteBits(lengths[widths[i]], codes[widths[i]]);
            if (same[i] >= 5) { writer.WriteBits(lengths[13], codes[13]); WriteU8(writer, same[i] - 5); i += same[i] - 1; }
        }
        int shift = this.method - 1;
        if (shift == 0) { return; }
        for (int i = 0; i < this.counts.Length; i++)
        {
            if (widths[i] > 1 && i != this.omitted)
            {
                int bits = Precision(widths[i] - 1, shift), dropped = widths[i] - 1 - bits;
                writer.WriteBits(bits, (uint)((this.counts[i] >> dropped) - (1 << bits)));
            }
            if (same[i] >= 5) { i += same[i] - 1; }
        }
    }

    private long HeaderCost()
    { JxlBitWriter writer = new(Stream.Null); this.Write(writer); return writer.Position; }

    private static double DataCost(ReadOnlySpan<uint> histogram, int[] counts)
    {
        double cost = 0;
        for (int i = 0; i < histogram.Length; i++) { if (histogram[i] != 0) { cost += histogram[i] * (12 - Math.Log2(counts[i])); } }
        return cost;
    }

    private static void WriteU8(JxlBitWriter writer, int value)
    {
        writer.WriteBool(value != 0);
        if (value == 0) { return; }
        int bits = BitOperations.Log2((uint)value); writer.WriteBits(3, (uint)bits); writer.WriteBits(bits, (uint)value - (1U << bits));
    }

    private static int Precision(int logCount, int shift) => Math.Clamp(shift - ((12 - logCount) >> 1), 0, logCount);
}
