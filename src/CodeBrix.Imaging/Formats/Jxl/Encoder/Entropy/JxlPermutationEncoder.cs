// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Tokenization adapted from libjxl lib/jxl/enc_coeff_order.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Coding;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Entropy;

internal static class JxlPermutationEncoder
{
    internal static JxlToken[] Tokenize(ReadOnlySpan<int> order, int skip = 0)
    {
        if (order.Length > 65536 || (uint)skip > order.Length) { throw new ArgumentOutOfRangeException(nameof(skip)); }
        for (int i = 0; i < skip; i++)
        { if (order[i] != i) { throw new ArgumentException("Skipped permutation entries must remain in order.", nameof(order)); } }
        int size = order.Length - skip;
        int[] tree = new int[size + 1]; bool[] seen = new bool[size]; uint[] ranks = new uint[size];
        for (int i = 1; i <= size; i++) { tree[i] = i & -i; }
        for (int i = 0; i < size; i++)
        {
            int value = order[i + skip] - skip;
            if ((uint)value >= size || seen[value]) { throw new ArgumentException("Invalid JPEG XL permutation.", nameof(order)); }
            seen[value] = true; uint rank = 0;
            // Number of remaining entries strictly below this value.
            for (int j = value; j > 0; j -= j & -j) { rank += (uint)tree[j]; }
            ranks[i] = rank;
            for (int j = value + 1; j <= size; j += j & -j) { tree[j]--; }
        }
        int end = size;
        while (end > 0 && ranks[end - 1] == 0) { end--; }
        JxlToken[] tokens = new JxlToken[end + 1];
        tokens[0] = new(Math.Min(JxlIntegerConfig.ContextBits((uint)order.Length), 7), (uint)end);
        uint previous = 0;
        for (int i = 0; i < end; i++)
        { tokens[i + 1] = new(Math.Min(JxlIntegerConfig.ContextBits(previous), 7), ranks[i]); previous = ranks[i]; }
        return tokens;
    }

    internal static void Write(JxlBitWriter writer, ReadOnlySpan<int> order, int skip = 0, int effort = 7, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        JxlToken[] tokens = Tokenize(order, skip);
        JxlEntropyEncoder model = JxlEntropyEncoder.CreateOptimized(tokens, 8, false, effort, false, cancellationToken);
        model.WriteHeader(writer); model.WriteTokens(writer, tokens, cancellationToken);
    }
}
