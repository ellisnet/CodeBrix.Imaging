// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from libjxl lib/jxl/enc_lz77.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Collections.Generic;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Coding;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Entropy;

internal static class JxlLz77
{
    private const int MaximumMatchLength = 65536;
    private static readonly JxlIntegerConfig LiteralConfig = new(4, 1, 0);

    // Contexts are attached to literal positions, but copied values may cross
    // context boundaries. The decoder derives each copied value's context anew.
    internal static JxlToken[] Match(JxlToken[] input, int contexts, uint width, int effort, bool prefix,
        CancellationToken cancellationToken)
    {
        if (input.Length < 4) { return []; }
        if (effort is < 1 or > 11) { throw new ArgumentOutOfRangeException(nameof(effort)); }
        CostModel costs = new(input, prefix);
        JxlToken[] greedy = Greedy(input, contexts, width, effort, costs, cancellationToken);
        if (effort < 8 || greedy.Length == 0) { return greedy; }
        // Re-estimate length/distance prices from the greedy parse, as libjxl
        // does, before finding a cheaper path through the available matches.
        return Refine(input, contexts, width, effort, new(greedy, prefix), cancellationToken);
    }

    private static JxlToken[] Greedy(JxlToken[] input, int contexts, uint width, int effort,
        CostModel costs, CancellationToken cancellationToken)
    {
        double[] sums = new double[input.Length + 1];
        for (int i = 0; i < input.Length; i++) { sums[i + 1] = sums[i] + costs.Token(input[i]); }
        MatchChain chain = effort == 1 ? null : new(input, width, ChainLimit(effort));
        List<JxlToken> result = []; bool matched = false; int runEnd = 0;
        for (int i = 0; i < input.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int length = 1, longest = 1; uint distance = 0; double saving = 0;
            if (effort == 1)
            {
                if (i > 0)
                {
                    if (runEnd <= i)
                    {
                        runEnd = i;
                        while (runEnd < input.Length && input[runEnd].Value == input[i - 1].Value) { runEnd++; }
                    }
                    if (runEnd - i >= 3) { Evaluate(runEnd - i, width == 0 ? 0U : 1U); }
                }
            }
            else { chain.Find(i, input.Length - i, Evaluate); }
            if (length >= 3)
            {
                result.Add(new(input[i].Context, (uint)length - JxlEntropyEncoder.MinLzLength, true));
                result.Add(new(contexts, distance)); matched = true;
            }
            else
            {
                // The native greedy parser emits a rejected match as literals
                // in one step. Searching its interior again can be quadratic
                // when each context already has a zero-cost constant symbol.
                length = longest;
                for (int j = 0; j < length; j++) { result.Add(input[i + j]); }
            }
            if (chain != null) { for (int j = 0; j < length; j++) { chain.Add(i + j); } }
            i += length;

            void Evaluate(int candidate, uint symbol)
            {
                longest = Math.Max(longest, candidate);
                double matchCost = costs.Token(new(input[i].Context, (uint)candidate - JxlEntropyEncoder.MinLzLength, true)) +
                    costs.Token(new(contexts, symbol));
                double gain = sums[i + candidate] - sums[i] - matchCost;
                if (gain > saving || (gain == saving && candidate > length))
                { saving = gain; length = candidate; distance = symbol; }
            }
        }
        return matched ? result.ToArray() : [];
    }

    private static JxlToken[] Refine(JxlToken[] input, int contexts, uint width, int effort,
        CostModel costs, CancellationToken cancellationToken)
    {
        MatchChain chain = new(input, width, ChainLimit(effort));
        double[] total = new double[input.Length + 1]; Array.Fill(total, double.PositiveInfinity); total[0] = 0;
        int[] lengths = new int[total.Length]; uint[] distances = new uint[total.Length];
        uint[] candidates = new uint[Math.Min(input.Length + 1, 65537)];
        int skip = 0, successiveLong = 0;
        for (int i = 0; i < input.Length; i++)
        {
            if ((i & 1023) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
            double literal = total[i] + costs.Token(input[i]);
            if (literal < total[i + 1]) { total[i + 1] = literal; lengths[i + 1] = 1; distances[i + 1] = 0; }
            if (skip != 0) { skip--; chain.Add(i); continue; }
            int maximum = 0;
            chain.Find(i, Math.Min(candidates.Length - 1, input.Length - i), (length, distance) =>
            {
                if (length > maximum)
                { candidates.AsSpan(maximum + 1, length - maximum).Fill(distance); maximum = length; }
                candidates[length] = Math.Min(candidates[length], distance);
            });
            chain.Add(i);
            uint cheapest = maximum == 0 ? 0 : candidates[maximum];
            for (int j = maximum; j >= 3; j--)
            {
                cheapest = Math.Min(cheapest, candidates[j]);
                double candidate = total[i] + costs.Token(new(input[i].Context, (uint)j - JxlEntropyEncoder.MinLzLength, true)) +
                    costs.Token(new(contexts, cheapest));
                if (candidate < total[i + j])
                { total[i + j] = candidate; lengths[i + j] = j; distances[i + j] = cheapest + 1; }
            }
            // libjxl skips the middle of long RLE runs. Extend that bound to
            // long periodic runs, so repeated tiles cannot cause quadratic work.
            bool longRun = maximum > 4096 || (maximum > 9 && candidates[maximum] == (width == 0 ? 0U : 1U));
            successiveLong = longRun ? successiveLong + 1 : 0;
            if (successiveLong == 8) { skip = maximum - 9; successiveLong = 0; }
        }
        List<JxlToken> result = [];
        for (int end = input.Length; end != 0;)
        {
            int length = lengths[end];
            if (distances[end] != 0)
            {
                result.Add(new(contexts, distances[end] - 1));
                result.Add(new(input[end - length].Context, (uint)length - JxlEntropyEncoder.MinLzLength, true));
            }
            else { result.Add(input[end - 1]); }
            end -= length;
        }
        result.Reverse(); return result.ToArray();
    }

    private static int ChainLimit(int effort) => effort switch
    { <= 2 => 1, 3 => 3, 4 => 7, 5 => 15, 6 => 31, 7 => 64, 8 => 128, 9 => 256, 10 => 512, _ => 1024 };

    private sealed class CostModel
    {
        private readonly Dictionary<int, double[]> bits = [];

        internal CostModel(JxlToken[] tokens, bool prefix)
        {
            Dictionary<int, uint[]> histograms = [];
            foreach (JxlToken token in tokens)
            {
                if (!histograms.TryGetValue(token.Context, out uint[] histogram))
                { histogram = new uint[256]; histograms.Add(token.Context, histogram); }
                histogram[Symbol(token).Symbol]++;
            }
            foreach (var entry in histograms)
            {
                long count = 0; foreach (uint value in entry.Value) { count += value; }
                double[] prices = new double[256];
                for (int i = 0; i < prices.Length; i++)
                {
                    double price = entry.Value[i] == 0 ? 12 : Math.Log2((double)count / entry.Value[i]);
                    prices[i] = prefix ? Math.Ceiling(price) : price;
                }
                this.bits.Add(entry.Key, prices);
            }
        }

        internal double Token(JxlToken token)
        {
            (uint symbol, int extra) = Symbol(token);
            return extra + (this.bits.TryGetValue(token.Context, out double[] prices) ? prices[symbol] : 12);
        }

        private static (uint Symbol, int Extra) Symbol(JxlToken token)
        {
            (uint symbol, int extra, _) = (token.IsLzLength ? JxlEntropyEncoder.LengthConfig : LiteralConfig).Encode(token.Value);
            return (symbol + (token.IsLzLength ? JxlEntropyEncoder.MinLzSymbol : 0), extra);
        }
    }

    private sealed class MatchChain
    {
        private readonly JxlToken[] input;
        private readonly int[] heads = new int[65536], previous, runEnds;
        private readonly Dictionary<int, uint> special = [];
        private readonly int limit, mask;
        private readonly uint genericOffset;

        internal MatchChain(JxlToken[] input, uint width, int limit)
        {
            this.input = input; this.limit = limit;
            this.runEnds = new int[input.Length];
            for (int i = input.Length - 1; i >= 0; i--)
            { this.runEnds[i] = i + 1 < input.Length && input[i].Value == input[i + 1].Value ? this.runEnds[i + 1] : i + 1; }
            int window = 1; while (window < Math.Min(input.Length, 1 << 20)) { window <<= 1; }
            this.previous = new int[window]; this.mask = window - 1;
            Array.Fill(this.heads, -1); Array.Fill(this.previous, -1);
            if (width == 0) { return; }
            this.genericOffset = 120;
            ReadOnlySpan<sbyte> table = JxlEntropyDecoder.SpecialDistances;
            for (int i = 119; i >= 0; i--)
            {
                long distance = table[i * 2] + ((long)width * table[(i * 2) + 1]);
                if (distance is > 0 and <= 1 << 20) { this.special[(int)distance] = (uint)i; }
            }
        }

        internal void Add(int position)
        {
            if (position + 2 >= this.input.Length) { return; }
            int hash = this.Hash(position);
            this.previous[position & this.mask] = this.heads[hash]; this.heads[hash] = position;
        }

        internal void Find(int position, int maximum, Action<int, uint> found)
        {
            maximum = Math.Min(maximum, MaximumMatchLength);
            if (maximum < 3) { return; }
            int previous = this.heads[this.Hash(position)], visited = 0, best = 0;
            int minimum = Math.Max(0, position - this.previous.Length);
            while (previous >= minimum && previous < position && visited++ < this.limit)
            {
                int length = this.input[previous].Value == this.input[position].Value ?
                    Math.Min(maximum, Math.Min(this.runEnds[previous] - previous, this.runEnds[position] - position)) : 0;
                while (length < maximum && this.input[previous + length].Value == this.input[position + length].Value) { length++; }
                if (length >= 3 && length + 2 >= best)
                {
                    int distance = position - previous;
                    uint symbol = this.special.TryGetValue(distance, out uint compact) ? compact : (uint)(distance - 1) + this.genericOffset;
                    found(length, symbol); best = Math.Max(best, length);
                    if (length == maximum) { break; }
                }
                int next = this.previous[previous & this.mask];
                if (next >= previous) { break; }
                previous = next;
            }
        }

        private int Hash(int position)
        {
            unchecked
            {
                uint hash = 0;
                for (int i = 0; i < 3; i++) { hash ^= this.input[position + i].Value + 0x9e3779b9U + (hash << 6) + (hash >> 2); }
                hash ^= hash >> 16; hash *= 0x85ebca6bU; hash ^= hash >> 13; hash *= 0xc2b2ae35U; hash ^= hash >> 16;
                return (int)(hash & 65535);
            }
        }
    }
}
