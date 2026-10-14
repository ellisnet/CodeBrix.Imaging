// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from libjxl lib/jxl/enc_fast_lossless.cc (PrefixCode and LLPrepare),
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Collections.Generic;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Entropy;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Modular.FastLossless;

internal static class JxlFastPrefixCode
{
    internal const int RawSymbols = 19, RunSymbols = 33;
    private static ReadOnlySpan<ulong> RawPrior => [3843, 852, 1270, 1214, 1014, 727, 481, 300, 159, 51, 5, 1, 1, 1, 1, 1, 1, 1, 1];
    private static ReadOnlySpan<ulong> RunPrior => [29, 27, 25, 23, 21, 21, 19, 18, 21, 17, 16, 15, 15, 14, 13, 13, 137, 98, 61, 34, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0];

    internal static JxlPrefixEncoder Create(ulong[] raw, ulong[] runs, int bitDepth, bool color)
    {
        int count = bitDepth > 14 ? 19 : bitDepth == 14 ? 17 : bitDepth + (color ? 3 : 2);
        ulong[] first = new ulong[count + 1];
        byte[] min = new byte[count + 1], max = new byte[count + 1];
        Array.Fill(max, bitDepth is >= 9 and <= 13 ? (byte)8 : (byte)7); max[count] = 10;
        if (bitDepth >= 14)
        {
            int start = bitDepth == 14 ? 15 : 13;
            for (int i = start; i < count; i++) { min[i] = max[i] = 8; }
            min[count] = 7;
        }
        for (int i = 0; i < count; i++) { first[i] = (raw[i] << 8) + RawPrior[i]; }
        ulong[] second = new ulong[RunSymbols];
        for (int i = 0; i < second.Length; i++) { second[i] = (runs[i] << 8) + RunPrior[i]; first[count] += second[i]; }
        byte[] level1 = ComputeLengths(first, min, max);
        byte[] upper = new byte[RunSymbols]; Array.Fill(upper, (byte)(15 - level1[count]));
        byte[] level2 = ComputeLengths(second, new byte[RunSymbols], upper);
        byte[] lengths = new byte[512];
        level1.AsSpan(0, count).CopyTo(lengths);
        for (int i = 0; i < second.Length; i++) { if (level2[i] != 0) { lengths[224 + i] = (byte)(level1[count] + level2[i]); } }
        return new(lengths);
    }

    // Exact minimum-cost length assignment with per-symbol limits and Kraft
    // equality. Raw symbols form the first tree; all runs share its last leaf.
    private static byte[] ComputeLengths(ulong[] frequencies, byte[] lower, byte[] upper)
    {
        List<int> symbols = []; int precision = 0;
        for (int i = 0; i < frequencies.Length; i++)
        { if (frequencies[i] != 0) { symbols.Add(i); precision = Math.Max(precision, upper[i]); } }
        int slots = 1 << precision, stride = slots + 1;
        ulong[] costs = new ulong[(symbols.Count + 1) * stride]; Array.Fill(costs, ulong.MaxValue / 2); costs[0] = 0;
        for (int s = 0; s < symbols.Count; s++)
        {
            int symbol = symbols[s];
            for (int bits = Math.Max(1, (int)lower[symbol]); bits <= upper[symbol]; bits++)
            {
                int step = 1 << (precision - bits); ulong cost = frequencies[symbol] * (uint)bits;
                for (int offset = 0; offset + step <= slots; offset++)
                {
                    int target = (s + 1) * stride + offset + step;
                    costs[target] = Math.Min(costs[target], costs[s * stride + offset] + cost);
                }
            }
        }
        if (costs[symbols.Count * stride + slots] == ulong.MaxValue / 2) { throw new InvalidOperationException("Fast-lossless prefix limits are infeasible."); }
        byte[] result = new byte[frequencies.Length]; int remaining = slots;
        for (int s = symbols.Count - 1; s >= 0; s--)
        {
            int symbol = symbols[s];
            for (int bits = Math.Max(1, (int)lower[symbol]); bits <= upper[symbol]; bits++)
            {
                int step = 1 << (precision - bits);
                if (step <= remaining && costs[(s + 1) * stride + remaining] == costs[s * stride + remaining - step] + frequencies[symbol] * (uint)bits)
                { result[symbol] = (byte)bits; remaining -= step; break; }
            }
        }
        return result;
    }
}
