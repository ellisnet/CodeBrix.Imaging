// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-coding/src/prefix.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

using System;
using System.Numerics;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;

namespace CodeBrix.Imaging.Formats.Jxl.Coding;

internal sealed class JxlPrefixHistogram
{
    private readonly uint[] table;
    private readonly int bits;

    private JxlPrefixHistogram(uint symbol) => this.table = [symbol << 5];

    // Canonical codes are stored in least-significant-bit-first lookup order.
    private JxlPrefixHistogram(byte[] lengths)
    {
        Span<int> counts = stackalloc int[16];
        foreach (byte length in lengths)
        {
            if (length > 15) { JxlThrowHelper.Invalid("invalid prefix code length."); }
            counts[length]++;
            this.bits = Math.Max(this.bits, length);
        }

        Span<int> next = stackalloc int[16];
        int code = 0;
        counts[0] = 0;
        for (int i = 1; i <= 15; i++)
        {
            code = (code + counts[i - 1]) << 1;
            next[i] = code;
            if (code + counts[i] > (1 << i)) { JxlThrowHelper.Invalid("oversubscribed prefix histogram."); }
        }

        if (code + counts[15] != 32768) { JxlThrowHelper.Invalid("incomplete prefix histogram."); }
        this.table = new uint[1 << this.bits];
        for (int symbol = 0; symbol < lengths.Length; symbol++)
        {
            int length = lengths[symbol];
            if (length == 0) { continue; }
            int value = next[length]++;
            int reversed = 0;
            for (int i = 0; i < length; i++) { reversed = (reversed << 1) | ((value >> i) & 1); }
            uint entry = ((uint)symbol << 5) | (uint)length;
            for (int i = reversed; i < this.table.Length; i += 1 << length) { this.table[i] = entry; }
        }
    }

    internal uint? SingleSymbol => this.bits == 0 ? this.table[0] >> 5 : null;

    internal uint ReadSymbol(JxlBitReader reader)
    {
        uint entry = this.table[reader.PeekBits(this.bits)];
        reader.ConsumeBits((int)(entry & 31));
        return entry >> 5;
    }

    internal static JxlPrefixHistogram Read(JxlBitReader reader, int alphabetSize)
    {
        if (alphabetSize is < 1 or > 32768) { JxlThrowHelper.Invalid("invalid prefix alphabet size."); }
        if (alphabetSize == 1) { return new(0); }
        int skip = (int)reader.ReadBits(2);
        return skip == 1 ? ReadSimple(reader, alphabetSize) : ReadComplex(reader, alphabetSize, skip);
    }

    private static JxlPrefixHistogram ReadSimple(JxlBitReader reader, int alphabetSize)
    {
        int alphabetBits = 32 - BitOperations.LeadingZeroCount((uint)(alphabetSize - 1));
        int count = (int)reader.ReadBits(2) + 1;
        Span<int> symbols = stackalloc int[4];
        for (int i = 0; i < count; i++)
        {
            symbols[i] = (int)reader.ReadBits(alphabetBits);
            if (symbols[i] >= alphabetSize || symbols[..i].Contains(symbols[i])) { JxlThrowHelper.Invalid("invalid simple prefix histogram."); }
        }

        if (count == 1) { return new((uint)symbols[0]); }
        byte[] lengths = new byte[alphabetSize];
        if (count == 2) { lengths[symbols[0]] = lengths[symbols[1]] = 1; }
        else if (count == 3) { lengths[symbols[0]] = 1; lengths[symbols[1]] = lengths[symbols[2]] = 2; }
        else if (reader.ReadBool())
        {
            lengths[symbols[0]] = 1;
            lengths[symbols[1]] = 2;
            lengths[symbols[2]] = lengths[symbols[3]] = 3;
        }
        else { foreach (int symbol in symbols) { lengths[symbol] = 2; } }
        return new(lengths);
    }

    private static JxlPrefixHistogram ReadComplex(JxlBitReader reader, int alphabetSize, int skip)
    {
        ReadOnlySpan<byte> order = [1, 2, 3, 4, 0, 5, 17, 6, 16, 7, 8, 9, 10, 11, 12, 13, 14, 15];
        byte[] codeLengths = new byte[18];
        int space = 0, nonzero = 0, single = 0;
        for (int i = skip; i < order.Length; i++)
        {
            int length = (int)reader.ReadU32(0, 4, 3, 8);
            if (length == 8) { length = reader.ReadBool() ? (reader.ReadBool() ? 5 : 1) : 2; }
            codeLengths[order[i]] = (byte)length;
            if (length == 0) { continue; }
            nonzero++;
            single = order[i];
            space += 32 >> length;
            if (space > 32) { JxlThrowHelper.Invalid("oversubscribed prefix length histogram."); }
            if (space == 32) { break; }
        }

        if (nonzero != 1 && space != 32) { JxlThrowHelper.Invalid("incomplete prefix length histogram."); }
        JxlPrefixHistogram lengthHistogram = nonzero == 1 ? new((uint)single) : new(codeLengths);
        byte[] lengths = new byte[alphabetSize];
        int previous = 8, lastNonzero = 8, lastRepeat = 0, remaining = 0, repeatSymbol = 0;
        space = 0;
        for (int i = 0; i < lengths.Length; i++)
        {
            int length;
            if (remaining > 0) { length = repeatSymbol; remaining--; }
            else
            {
                int symbol = (int)lengthHistogram.ReadSymbol(reader);
                if (symbol < 16)
                {
                    length = symbol;
                    if (symbol != 0) { lastNonzero = symbol; }
                }
                else
                {
                    int repeatBits = symbol == 16 ? 2 : 3;
                    remaining = (int)reader.ReadBits(repeatBits) + 3;
                    if (previous == symbol)
                    {
                        remaining += lastRepeat * ((1 << repeatBits) - 1) - (1 << (repeatBits + 1));
                        lastRepeat += remaining;
                    }
                    else { lastRepeat = remaining; }
                    if (remaining > lengths.Length - i) { JxlThrowHelper.Invalid("prefix length repeat exceeds alphabet."); }
                    length = repeatSymbol = symbol == 16 ? lastNonzero : 0;
                    remaining--;
                }

                previous = symbol;
            }

            lengths[i] = (byte)length;
            if (length == 0) { continue; }
            space += 1 << (15 - length);
            if (space > 32768) { JxlThrowHelper.Invalid("oversubscribed prefix histogram."); }
            if (space == 32768 && remaining == 0) { break; }
        }

        if (space != 32768 || remaining != 0) { JxlThrowHelper.Invalid("invalid prefix histogram."); }
        return new(lengths);
    }
}
