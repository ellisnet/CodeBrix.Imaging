// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from libjxl lib/jxl/modular/encoding/{enc_ma,enc_encoding}.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Coding;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Entropy;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Modular;

internal sealed class JxlMaEncoder
{
    private readonly record struct Leaf(int Predictor, int Offset, double Cost);
    private readonly record struct Split(int Property, int Value, double Cost);
    private static readonly JxlIntegerConfig IntegerConfig = new(4, 1, 0);
    private readonly JxlMaTree tree;
    private readonly int contexts;
    internal int ContextCount => this.contexts;

    private JxlMaEncoder(JxlMaTree tree, int contexts) { this.tree = tree; this.contexts = contexts; }

    internal static JxlMaEncoder Learn(JxlModularPlane[] planes, int effort, JxlWeightedPredictorHeader weighted,
        MemoryAllocator allocator, CancellationToken cancellationToken, int bitDepth = 8, JxlModularSearchOptions search = null)
    {
        long total = 0; foreach (JxlModularPlane plane in planes) { total += (long)plane.Width * plane.Height; }
        if (search is { Sampling: 0 })
        { return new(new() { Nodes = [new(0, 0, 0, 0, 0, search.Predictor <= 13 ? search.Predictor : 5, 0, 1)], NeedsSelfCorrection = search.Predictor == 6 }, 1); }
        if (effort is 2 or 3 && search is not { NoWeighted: true }) { return FixedTree(effort == 3, total, bitDepth); }
        if (effort == 1 || total == 0 || (total < 16 && search == null))
        { return new(new() { Nodes = [new(0, 0, 0, 0, 0, 5, 0, 1)] }, 1); }
        int[] predictors = effort < 10 ? [5, 6] : [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13];
        if (search != null)
        {
            predictors = search.Predictor switch { 15 => [6, 5, 2, 3, 4, 1, 0, 7, 8, 9, 10, 11, 12, 13], 14 => [6, 5], _ => [search.Predictor] };
            if (search.NoWeighted) { predictors = Array.FindAll(predictors, predictor => predictor != 6); }
        }
        int[] properties = effort < 6 ? [0, 2, 3, 4, 5, 9, 15] :
            [0, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23];
        if (effort >= 11)
        {
            int previous = Math.Min(Math.Max(0, planes.Length - 1), 4);
            Array.Resize(ref properties, 15 + previous * 4);
            for (int i = 0; i < previous * 4; i++) { properties[15 + i] = 16 + i; }
        }
        int target = effort <= 4 ? 1024 : effort <= 7 ? 4096 : 8192 << (effort - 8);
        if (search != null)
        {
            if (search.Sampling >= 0) { target = Math.Max(1, (int)Math.Min(262144, total * Math.Min(1, search.Sampling * 1.3f))); }
            if (search.NoWeighted) { properties = Array.FindAll(properties, property => property != 15); }
        }
        int stride = (int)Math.Max(1, (total + target - 1) / target), count = (int)((total + stride - 1) / stride);
        int[][] residuals = new int[predictors.Length][], features = new int[properties.Length][];
        for (int i = 0; i < residuals.Length; i++) { residuals[i] = new int[count]; }
        for (int i = 0; i < features.Length; i++) { features[i] = new int[count]; }
        long position = 0; int sampled = 0;
        for (int c = 0; c < planes.Length; c++)
        {
            JxlModularPlane plane = planes[c];
            using JxlPredictor predictor = new(plane, Previous(planes, c), search is { NoWeighted: true } ? null : weighted, allocator);
            for (int y = 0; y < plane.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested(); Span<int> row = plane.Row(y);
                ReadOnlySpan<int> above = y == 0 ? default : plane.Row(y - 1), above2 = y < 2 ? default : plane.Row(y - 2);
                for (int x = 0; x < row.Length; x++, position++)
                {
                    predictor.Prepare(x, y, row, above, above2); int value = row[x];
                    if (position % stride == 0)
                    {
                        for (int p = 0; p < predictors.Length; p++) { residuals[p][sampled] = unchecked(value - predictor.Predict(predictors[p])); }
                        for (int p = 0; p < properties.Length; p++) { features[p][sampled] = properties[p] == 0 ? c : predictor.Property((uint)properties[p]); }
                        sampled++;
                    }
                    predictor.Record(value, row);
                }
            }
        }
        int[] indices = new int[count]; for (int i = 0; i < count; i++) { indices[i] = i; }
        List<JxlMaTree.Node> nodes = [default];
        Queue<(int Node, int[] Samples, int Depth)> queue = new(); queue.Enqueue((0, indices, 0));
        int maxLeaves = effort <= 4 ? 16 : effort <= 7 ? 64 : 256, leaves = 1;
        while (queue.TryDequeue(out var work))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Leaf[] choices = new Leaf[predictors.Length];
            Leaf best = new(0, 0, double.PositiveInfinity);
            for (int p = 0; p < predictors.Length; p++)
            {
                choices[p] = ChooseLeaf(residuals[p], work.Samples, p, effort);
                if (choices[p].Cost < best.Cost) { best = choices[p]; }
            }
            Split split = new(-1, 0, double.PositiveInfinity);
            double overhead = 32 + (work.Samples.Length * .005);
            // Split costs are nonnegative. A leaf below the split overhead
            // cannot improve, which is common for constant/palette residuals.
            if (best.Cost > overhead && work.Samples.Length >= 16 && leaves < maxLeaves && work.Depth < 12)
            {
                for (int p = 0; p < properties.Length; p++)
                {
                    Split candidate = FindSplit(features[p], residuals, work.Samples, choices, p, effort);
                    if (candidate.Cost < split.Cost) { split = candidate; }
                }
            }
            if (split.Property >= 0 && split.Cost + overhead < best.Cost)
            {
                List<int> high = [], low = [];
                foreach (int i in work.Samples) { (features[split.Property][i] > split.Value ? high : low).Add(i); }
                int left = nodes.Count; nodes.Add(default); nodes.Add(default);
                nodes[work.Node] = new((uint)properties[split.Property], split.Value, left, left + 1, -1, 0, 0, 0);
                queue.Enqueue((left, high.ToArray(), work.Depth + 1)); queue.Enqueue((left + 1, low.ToArray(), work.Depth + 1)); leaves++;
            }
            else { nodes[work.Node] = new(0, 0, 0, 0, 0, predictors[best.Predictor], best.Offset, 1); }
        }
        int context = 0; bool selfCorrection = false;
        for (int i = 0; i < nodes.Count; i++)
        {
            JxlMaTree.Node node = nodes[i];
            if (node.Cluster >= 0) { nodes[i] = node with { Cluster = context++ }; selfCorrection |= node.Predictor == 6; }
            else { selfCorrection |= node.Property == 15; }
        }
        return new(new() { Nodes = nodes.ToArray(), NeedsSelfCorrection = selfCorrection }, context);
    }

    private static JxlMaEncoder FixedTree(bool weighted, long total, int bitDepth)
    {
        int logPixels = total <= 1 ? 0 : BitOperations.Log2((ulong)total - 1) + 1;
        int minimumGap = logPixels < 14 ? 8 * (14 - logPixels) : 0, shift = Math.Clamp(bitDepth - 11, 0, 4);
        ReadOnlySpan<int> cutoffs = JxlWeightedPredictorEncoder.Cutoffs;
        JxlMaTree.Node leaf = new(0, 0, 0, 0, 0, weighted ? 6 : 5, 0, 1);
        List<JxlMaTree.Node> nodes = [leaf];
        Queue<(int Begin, int End, int Node)> queue = new(); queue.Enqueue((0, cutoffs.Length, 0));
        while (queue.TryDequeue(out var work))
        {
            if (work.Begin + minimumGap >= work.End) { continue; }
            int middle = (work.Begin + work.End) / 2, left = nodes.Count;
            nodes[work.Node] = new(weighted ? 15U : 9U, cutoffs[middle] << shift, left, left + 1, -1, 0, 0, 0);
            queue.Enqueue((middle + 1, work.End, left)); nodes.Add(leaf);
            queue.Enqueue((work.Begin, middle, left + 1)); nodes.Add(leaf);
        }
        int count = 0;
        for (int i = 0; i < nodes.Count; i++) { if (nodes[i].Cluster >= 0) { nodes[i] = nodes[i] with { Cluster = count++ }; } }
        return new(new() { Nodes = nodes.ToArray(), NeedsSelfCorrection = weighted }, count);
    }

    internal void Write(JxlBitWriter writer, JxlModularPlane[] planes, JxlWeightedPredictorHeader weighted,
        MemoryAllocator allocator, bool prefix, int effort, CancellationToken cancellationToken, int maximumHistograms = 256)
    {
        JxlToken[] tokens = this.CreateTokens(planes, weighted, allocator, cancellationToken);
        if (effort >= 4) { this.RefineMultipliers([tokens], cancellationToken); }
        this.WriteTree(writer, cancellationToken);
        uint width = 0; foreach (JxlModularPlane plane in planes) { width = Math.Max(width, (uint)plane.Width); }
        JxlEntropyEncoder.WriteCompressed(writer, tokens, this.contexts, width, prefix, effort, cancellationToken, maximumHistograms);
    }

    internal void WriteTree(JxlBitWriter writer, CancellationToken cancellationToken)
    {
        List<JxlToken> treeTokens = [];
        foreach (JxlMaTree.Node node in this.tree.Nodes)
        {
            treeTokens.Add(new(1, node.Cluster >= 0 ? 0 : node.Property + 1));
            if (node.Cluster < 0) { treeTokens.Add(new(0, JxlBitWriter.PackSigned(node.Value))); }
            else
            {
                treeTokens.Add(new(2, (uint)node.Predictor)); treeTokens.Add(new(3, JxlBitWriter.PackSigned(node.Offset)));
                int log = BitOperations.TrailingZeroCount(node.Multiplier);
                treeTokens.Add(new(4, (uint)log)); treeTokens.Add(new(5, (node.Multiplier >> log) - 1));
            }
        }
        JxlToken[] serialized = treeTokens.ToArray(); JxlEntropyEncoder treeEncoder = new(serialized, 6);
        treeEncoder.WriteHeader(writer); treeEncoder.WriteTokens(writer, serialized, cancellationToken);
    }

    // Validate divisibility over every real stream, including group boundaries;
    // sampled MA learning alone cannot safely establish an exact multiplier.
    internal void RefineMultipliers(JxlToken[][] streams, CancellationToken cancellationToken)
    {
        uint[] divisors = new uint[this.contexts], factors = new uint[this.contexts]; Array.Fill(factors, 1U);
        foreach (JxlToken[] stream in streams)
        {
            for (int i = 0; i < stream.Length; i++)
            {
                if ((i & 4095) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
                JxlToken token = stream[i]; uint current = divisors[token.Context];
                if (current == 1) { continue; }
                uint value = (uint)Math.Abs((long)JxlBitReader.UnpackSigned(token.Value));
                while (value != 0) { (current, value) = (value, current % value); }
                divisors[token.Context] = current;
            }
        }
        for (int i = 0; i < this.tree.Nodes.Length; i++)
        {
            JxlMaTree.Node node = this.tree.Nodes[i];
            if (node.Cluster < 0) { continue; }
            uint factor = divisors[node.Cluster];
            // The wire multiplier is a positive signed-31-bit value.
            while (factor > 1 && factor * (ulong)node.Multiplier > int.MaxValue)
            { if ((factor & 1) != 0) { factor = 1; break; } factor >>= 1; }
            if (factor <= 1) { continue; }
            factors[node.Cluster] = factor;
            this.tree.Nodes[i] = node with { Multiplier = node.Multiplier * factor };
        }
        foreach (JxlToken[] stream in streams)
        {
            for (int i = 0; i < stream.Length; i++)
            {
                if ((i & 4095) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
                JxlToken token = stream[i]; uint factor = factors[token.Context];
                if (factor > 1)
                { stream[i] = new(token.Context, JxlBitWriter.PackSigned((int)(JxlBitReader.UnpackSigned(token.Value) / (long)factor))); }
            }
        }
    }

    internal JxlToken[] CreateTokens(JxlModularPlane[] planes, JxlWeightedPredictorHeader weighted, MemoryAllocator allocator, CancellationToken cancellationToken)
    {
        long count = 0;
        foreach (JxlModularPlane plane in planes) { count += (long)plane.Width * plane.Height; }
        if (count > int.MaxValue) { throw new ArgumentException("JPEG XL Modular group is too large.", nameof(planes)); }
        JxlToken[] tokens = new JxlToken[(int)count]; int index = 0;
        // Fixed gradient substreams use one clamped-gradient context. The source is
        // already complete, so no predictor history or previous-channel views
        // need to be allocated or updated in this common stream shape.
        if (this.tree.Nodes.Length == 1 && this.tree.Nodes[0] is { Predictor: 5, Offset: 0, Multiplier: 1 } fixedLeaf)
        {
            foreach (JxlModularPlane plane in planes)
            {
                for (int y = 0; y < plane.Height; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested(); ReadOnlySpan<int> row = plane.Row(y);
                    ReadOnlySpan<int> above = y == 0 ? default : plane.Row(y - 1);
                    int west = y == 0 || row.IsEmpty ? 0 : above[0];
                    for (int x = 0; x < row.Length; x++)
                    {
                        int north = y == 0 ? west : above[x], northwest = y == 0 ? west : x == 0 ? north : above[x - 1];
                        int value = row[x];
                        tokens[index++] = new(fixedLeaf.Cluster, JxlBitWriter.PackSigned(unchecked(value - JxlPredictor.Gradient(north, west, northwest))));
                        west = value;
                    }
                }
            }
            return tokens;
        }
        for (int c = 0; c < planes.Length; c++)
        {
            JxlModularPlane plane = planes[c];
            using JxlPredictor predictor = new(plane, Previous(planes, c), this.tree.NeedsSelfCorrection ? weighted : null, allocator);
            for (int y = 0; y < plane.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested(); Span<int> row = plane.Row(y);
                ReadOnlySpan<int> above = y == 0 ? default : plane.Row(y - 1), above2 = y < 2 ? default : plane.Row(y - 2);
                for (int x = 0; x < row.Length; x++)
                {
                    predictor.Prepare(x, y, row, above, above2); int value = row[x];
                    JxlMaTree.Node leaf = this.tree.FindLeaf(predictor, c, 0);
                    int residual = unchecked(value - predictor.Predict(leaf.Predictor) - leaf.Offset);
                    if (residual % (long)leaf.Multiplier != 0) { throw new InvalidOperationException("A Modular residual is not divisible by its learned multiplier."); }
                    tokens[index++] = new(leaf.Cluster, JxlBitWriter.PackSigned((int)(residual / (long)leaf.Multiplier)));
                    predictor.Record(value, row);
                }
            }
        }
        return tokens;
    }

    private static JxlModularPlane[] Previous(JxlModularPlane[] planes, int current)
    {
        List<JxlModularPlane> previous = []; JxlModularPlane channel = planes[current];
        for (int p = current - 1; p >= 0; p--)
        {
            JxlModularPlane other = planes[p];
            if (channel.Width == other.Width && channel.Height == other.Height && channel.HorizontalShift == other.HorizontalShift && channel.VerticalShift == other.VerticalShift)
            { previous.Add(other); }
        }
        return previous.ToArray();
    }

    private static Leaf ChooseLeaf(int[] residuals, int[] indices, int predictor, int effort)
    {
        Histogram plain = new(); foreach (int i in indices) { plain.Add(residuals[i]); }
        Leaf best = new(predictor, 0, plain.Cost);
        if (effort < 6) { return best; }
        int[] ordered = new int[indices.Length]; for (int i = 0; i < indices.Length; i++) { ordered[i] = residuals[indices[i]]; }
        Array.Sort(ordered); int offset = ordered[ordered.Length / 2];
        if (offset == 0) { return best; }
        Histogram shifted = new(); foreach (int i in indices) { shifted.Add(unchecked(residuals[i] - offset)); }
        double cost = shifted.Cost + 8 + IntegerConfig.Encode(JxlBitWriter.PackSigned(offset)).ExtraBits;
        return cost < best.Cost ? new(predictor, offset, cost) : best;
    }

    private static Split FindSplit(int[] feature, int[][] residuals, int[] indices, Leaf[] choices, int property, int effort)
    {
        int first = feature[indices[0]];
        bool constant = true;
        foreach (int i in indices) { if (feature[i] != first) { constant = false; break; } }
        if (constant) { return new(-1, 0, double.PositiveInfinity); }
        int[] order = (int[])indices.Clone(); Array.Sort(order, (a, b) => feature[a].CompareTo(feature[b]));
        Histogram[] below = new Histogram[residuals.Length], above = new Histogram[residuals.Length];
        for (int p = 0; p < residuals.Length; p++)
        {
            below[p] = new(); above[p] = new();
            foreach (int i in indices) { above[p].Add(unchecked(residuals[p][i] - choices[p].Offset)); }
        }
        Split best = new(-1, 0, double.PositiveInfinity);
        int step = Math.Max(1, indices.Length / (effort >= 8 ? 64 : 16)), next = step;
        for (int i = 0; i < order.Length - 1; i++)
        {
            for (int p = 0; p < residuals.Length; p++)
            {
                int value = unchecked(residuals[p][order[i]] - choices[p].Offset);
                below[p].Add(value); above[p].Add(value, -1);
            }
            if (i + 1 < next || i < 3 || i + 4 >= order.Length || feature[order[i]] == feature[order[i + 1]]) { continue; }
            next = i + 1 + step;
            double left = double.PositiveInfinity, right = double.PositiveInfinity;
            for (int p = 0; p < residuals.Length; p++)
            { left = Math.Min(left, below[p].Cost); right = Math.Min(right, above[p].Cost); }
            if (left + right < best.Cost) { best = new(property, feature[order[i]], left + right); }
        }
        return best;
    }

    private sealed class Histogram
    {
        // Split searches repeatedly visit the same small integer counts.
        // Cache the exact expression, preserving costs and candidate ordering.
        private static readonly double[] entropyTerms = CreateEntropyTerms();
        private readonly int[] counts = new int[80];
        private int count, extra, symbols;
        private double sum;
        internal double Cost => EntropyTerm(this.count) - this.sum + this.extra + (this.symbols * 3);

        internal void Add(int residual, int delta = 1)
        {
            (uint symbol, int bits, _) = IntegerConfig.Encode(JxlBitWriter.PackSigned(residual));
            int before = this.counts[symbol], after = before + delta;
            this.counts[symbol] = after; this.count += delta; this.extra += bits * delta;
            this.sum += EntropyTerm(after) - EntropyTerm(before);
            if (before == 0) { this.symbols++; } else if (after == 0) { this.symbols--; }
        }
        private static double EntropyTerm(int count) => count < entropyTerms.Length ? entropyTerms[count] : count * Math.Log2(count);

        private static double[] CreateEntropyTerms()
        {
            double[] values = new double[65537];
            for (int i = 2; i < values.Length; i++) { values[i] = i * Math.Log2(i); }
            return values;
        }
    }
}
