// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from libjxl lib/jxl/enc_coeff_order.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Collections.Generic;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Entropy;
using CodeBrix.Imaging.Formats.Jxl.VarDct;
using static CodeBrix.Imaging.Formats.Jxl.Bitstream.JxlU32;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;

internal sealed class JxlCoefficientOrder
{
    private readonly JxlHfPass natural = new();
    private readonly (ushort X, ushort Y)[][][] orders = new (ushort, ushort)[7][][];
    private readonly int[][][] permutations = new int[7][][];
    internal uint Used { get; private set; }

    internal JxlCoefficientOrder(JxlVarDctPreparation prepared, int effort, CancellationToken cancellationToken)
    {
        if (effort < 4 || (prepared.Blocks.Width < 5 && prepared.Blocks.Height < 5)) { return; }
        long[][][] zeros = new long[7][][];
        for (int by = 0; by < prepared.Blocks.Height; by++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int bx = 0; bx < prepared.Blocks.Width; bx++)
            {
                JxlVarBlock block = prepared.Blocks.DangerousGetRowSpan(by)[bx];
                int index = block.Transform.OrderIndex();
                if (block.Multiplier <= 0 || index > 6) { continue; }
                (ushort X, ushort Y)[] order = this.natural.Order(index, 0);
                zeros[index] ??= [new long[order.Length], new long[order.Length], new long[order.Length]];
                for (int c = 0; c < 3; c++)
                {
                    for (int i = order.Length / 64; i < order.Length; i++)
                    {
                        (int dx, int dy) = order[i]; if (block.Transform.Transpose()) { (dx, dy) = (dy, dx); }
                        if (prepared.Coefficients[c].Get(bx * 8 + dx, by * 8 + dy) == 0) { zeros[index][c][i]++; }
                    }
                }
            }
        }
        for (int index = 0; index < zeros.Length; index++)
        {
            if (zeros[index] == null) { continue; }
            (ushort X, ushort Y)[] order = this.natural.Order(index, 0);
            int skip = order.Length / 64; bool changed = false;
            this.orders[index] = new (ushort, ushort)[3][]; this.permutations[index] = new int[3][];
            for (int c = 0; c < 3; c++)
            {
                int[] permutation = new int[order.Length]; long[] counts = zeros[index][c];
                float scale = 1 / MathF.Sqrt(order.Length);
                for (int i = 0; i < permutation.Length; i++) { permutation[i] = i; counts[i] = (long)(counts[i] * scale + .1f); }
                Array.Sort(permutation, skip, permutation.Length - skip, Comparer<int>.Create((a, b) =>
                { int comparison = counts[a].CompareTo(counts[b]); return comparison == 0 ? a.CompareTo(b) : comparison; }));
                (ushort X, ushort Y)[] selected = new (ushort, ushort)[order.Length];
                for (int i = 0; i < selected.Length; i++) { selected[i] = order[permutation[i]]; changed |= permutation[i] != i; }
                this.orders[index][c] = selected; this.permutations[index][c] = permutation;
            }
            if (changed) { this.Used |= 1U << index; }
        }
    }

    internal (ushort X, ushort Y)[] Order(int index, int channel) => index < this.orders.Length && (this.Used & (1U << index)) != 0
        ? this.orders[index][channel] : this.natural.Order(index, channel);

    internal void Write(JxlBitWriter writer, int effort, CancellationToken cancellationToken)
    {
        writer.WriteU32(this.Used, 0x5f, 0x13, 0, U(13));
        if (this.Used == 0) { return; }
        List<JxlToken> tokens = [];
        for (int index = 0; index < this.permutations.Length; index++)
        {
            if ((this.Used & (1U << index)) == 0) { continue; }
            foreach (int[] permutation in this.permutations[index]) { tokens.AddRange(JxlPermutationEncoder.Tokenize(permutation, permutation.Length / 64)); }
        }
        JxlToken[] values = tokens.ToArray();
        JxlEntropyEncoder entropy = JxlEntropyEncoder.CreateOptimized(values, 8, false, effort, false, cancellationToken);
        entropy.WriteHeader(writer); entropy.WriteTokens(writer, values, cancellationToken);
    }
}
