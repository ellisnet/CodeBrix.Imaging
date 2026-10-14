// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-coding/src/ans.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).
// Upstream alias tables derive from libjxl (BSD-3-Clause):
// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// See THIRD-PARTY-NOTICES.txt for the retained license and patent grant.

using System;
using System.Collections.Generic;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;

namespace CodeBrix.Imaging.Formats.Jxl.Coding;

internal sealed class JxlAnsHistogram
{
    private readonly Bucket[] buckets;
    private readonly int bucketBits;
    internal uint? SingleSymbol { get; }

    private struct Bucket
    {
        internal int Frequency;
        internal int Cutoff;
        internal int Alias;
        internal int Offset;
    }

    private JxlAnsHistogram(int[] frequencies, int alphabetSize, int alphabetBits)
    {
        this.bucketBits = 12 - alphabetBits;
        int bucketSize = 1 << this.bucketBits;
        this.buckets = new Bucket[frequencies.Length];
        int single = Array.IndexOf(frequencies, 4096);
        if (single >= 0)
        {
            this.SingleSymbol = (uint)single;
            for (int i = 0; i < frequencies.Length; i++)
            {
                this.buckets[i] = new() { Frequency = frequencies[i], Alias = single, Offset = bucketSize * i };
            }
            return;
        }

        Stack<int> underfull = new(), overfull = new();
        for (int i = 0; i < frequencies.Length; i++)
        {
            this.buckets[i] = new() { Frequency = frequencies[i], Cutoff = frequencies[i], Alias = i < alphabetSize ? i : 0 };
            if (frequencies[i] < bucketSize) { underfull.Push(i); }
            else if (frequencies[i] > bucketSize) { overfull.Push(i); }
        }

        while (overfull.Count > 0 && underfull.Count > 0)
        {
            int over = overfull.Pop(), under = underfull.Pop();
            this.buckets[over].Cutoff -= bucketSize - this.buckets[under].Cutoff;
            this.buckets[under].Alias = over;
            this.buckets[under].Offset = this.buckets[over].Cutoff;
            if (this.buckets[over].Cutoff < bucketSize) { underfull.Push(over); }
            else if (this.buckets[over].Cutoff > bucketSize) { overfull.Push(over); }
        }

        for (int i = 0; i < this.buckets.Length; i++)
        {
            ref Bucket bucket = ref this.buckets[i];
            if (bucket.Cutoff == bucketSize) { bucket.Alias = i; bucket.Cutoff = bucket.Offset = 0; }
            else { bucket.Offset -= bucket.Cutoff; }
        }
    }

    internal uint ReadSymbol(JxlBitReader reader, ref uint state)
    {
        int index = (int)(state & 4095);
        int symbol = index >> this.bucketBits;
        int offset = index & ((1 << this.bucketBits) - 1);
        Bucket bucket = this.buckets[symbol];
        if (offset >= bucket.Cutoff) { symbol = bucket.Alias; offset += bucket.Offset; }
        state = ((state >> 12) * (uint)this.buckets[symbol].Frequency) + (uint)offset;
        if (state < 65536) { state = (state << 16) | reader.ReadBits(16); }
        return (uint)symbol;
    }

    // Invert the same alias mapping for the managed ANS encoder. Every offset
    // of every populated symbol has exactly one position in the 4096-entry table.
    internal static ushort[][] CreateReverseMap(ReadOnlySpan<int> counts, int alphabetBits)
    {
        int[] frequencies = new int[1 << alphabetBits]; counts.CopyTo(frequencies);
        JxlAnsHistogram table = new(frequencies, counts.Length, alphabetBits);
        ushort[][] reverse = new ushort[counts.Length][];
        for (int i = 0; i < reverse.Length; i++) { reverse[i] = new ushort[counts[i]]; }
        for (int i = 0; i < 4096; i++)
        {
            int symbol = i >> table.bucketBits, offset = i & ((1 << table.bucketBits) - 1);
            Bucket bucket = table.buckets[symbol];
            if (offset >= bucket.Cutoff) { symbol = bucket.Alias; offset += bucket.Offset; }
            reverse[symbol][offset] = (ushort)i;
        }
        return reverse;
    }

    internal static JxlAnsHistogram Read(JxlBitReader reader, int alphabetBits)
    {
        int[] frequencies = new int[1 << alphabetBits];
        int alphabetSize;
        if (reader.ReadBool())
        {
            bool binary = reader.ReadBool();
            int first = ReadU8(reader);
            int second = binary ? ReadU8(reader) : first;
            alphabetSize = Math.Max(first, second) + 1;
            if (alphabetSize > frequencies.Length || (binary && first == second)) { JxlThrowHelper.Invalid("invalid ANS alphabet."); }
            if (binary)
            {
                frequencies[first] = (int)reader.ReadBits(12);
                frequencies[second] = 4096 - frequencies[first];
            }
            else { frequencies[first] = 4096; }
        }
        else if (reader.ReadBool())
        {
            alphabetSize = ReadU8(reader) + 1;
            if (alphabetSize > frequencies.Length) { JxlThrowHelper.Invalid("invalid uniform ANS alphabet."); }
            for (int i = 0; i < alphabetSize; i++) { frequencies[i] = (4096 / alphabetSize) + (i < 4096 % alphabetSize ? 1 : 0); }
        }
        else
        {
            int length = 0;
            while (length < 3 && reader.ReadBool()) { length++; }
            int shift = (int)reader.ReadBits(length) + (1 << length) - 1;
            if (shift > 13) { JxlThrowHelper.Invalid("invalid ANS frequency precision."); }
            alphabetSize = ReadU8(reader) + 3;
            if (alphabetSize > frequencies.Length) { JxlThrowHelper.Invalid("invalid ANS alphabet."); }
            int[] repeats = new int[alphabetSize];
            int omitted = -1;
            for (int i = 0; i < alphabetSize; i++)
            {
                int code = frequencies[i] = ReadLength(reader);
                if (code == 13)
                {
                    int count = repeats[i] = ReadU8(reader) + 4;
                    if (count > alphabetSize - i) { JxlThrowHelper.Invalid("invalid ANS frequency repeat."); }
                    i += count - 1;
                }
                else if (omitted < 0 || code > frequencies[omitted]) { omitted = i; }
            }

            if (omitted < 0 || (omitted + 1 < alphabetSize && repeats[omitted + 1] != 0)) { JxlThrowHelper.Invalid("invalid omitted ANS frequency."); }
            int sum = 0, previous = 0;
            for (int i = 0; i < alphabetSize; i++)
            {
                if (repeats[i] > 0)
                {
                    int end = i + repeats[i];
                    for (; i < end; i++) { frequencies[i] = previous; sum += previous; }
                    i--;
                }
                else if (i == omitted) { previous = 0; }
                else
                {
                    int code = frequencies[i];
                    if (code > 1)
                    {
                        int zeros = code - 1;
                        int count = Math.Clamp(shift - ((12 - zeros) >> 1), 0, zeros);
                        code = (1 << zeros) + ((int)reader.ReadBits(count) << (zeros - count));
                    }
                    previous = frequencies[i] = code;
                    sum += code;
                }

                if (sum > 4096) { JxlThrowHelper.Invalid("ANS frequencies exceed normalization."); }
            }
            frequencies[omitted] = 4096 - sum;
        }
        return new(frequencies, alphabetSize, alphabetBits);
    }

    private static int ReadU8(JxlBitReader reader)
    {
        if (!reader.ReadBool()) { return 0; }
        int bits = (int)reader.ReadBits(3);
        return (1 << bits) + (int)reader.ReadBits(bits);
    }

    private static int ReadLength(JxlBitReader reader)
    {
        switch (reader.ReadBits(3))
        {
            case 0: return 10;
            case 1:
                if (reader.ReadBool()) { return 4; }
                if (reader.ReadBool()) { return 0; }
                if (reader.ReadBool()) { return 11; }
                return reader.ReadBool() ? 13 : 12;
            case 2: return 7;
            case 3: return reader.ReadBool() ? 1 : 3;
            case 4: return 6;
            case 5: return 8;
            case 6: return 9;
            default: return reader.ReadBool() ? 2 : 5;
        }
    }
}
