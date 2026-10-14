// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from libjxl lib/jxl/enc_cluster.cc and enc_ans.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Coding;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Entropy;

internal static class JxlHistogramOptimizer
{
    internal const int MaximumDistinctValues = 65536;
    private readonly record struct Pair(int A, int B, int VersionA, int VersionB);
    private readonly record struct Value(uint Sample, bool Length, bool Distance);
    private static readonly JxlIntegerConfig DefaultConfig = new(4, 1, 0);
    private static readonly JxlIntegerConfig[] Candidates =
    [
        new(4, 1, 0), new(4, 2, 0), new(4, 1, 2), new(0, 0, 0), new(2, 0, 1),
        new(4, 2, 1), new(4, 2, 2), new(5, 2, 0), new(5, 1, 0), new(5, 2, 1), new(5, 2, 2), new(5, 1, 2),
        new(3, 2, 0), new(3, 1, 0), new(3, 2, 1), new(3, 1, 2), new(4, 1, 3), new(5, 1, 4), new(5, 2, 3),
        new(6, 1, 5), new(6, 2, 4), new(6, 0, 0), new(7, 0, 0), new(8, 0, 0), new(9, 0, 0), new(10, 0, 0), new(11, 0, 0), new(12, 0, 0),
    ];

    internal static byte[] Cluster(JxlToken[] tokens, int contexts, bool prefix, CancellationToken cancellationToken, int effort = 7, int maximum = 256)
        => Cluster(new JxlTokenSequence(tokens), contexts, prefix, cancellationToken, effort, maximum);

    internal static byte[] Cluster(JxlTokenSequence tokens, int contexts, bool prefix, CancellationToken cancellationToken, int effort = 7, int maximum = 256)
    {
        if (maximum is < 1 or > 256) { throw new ArgumentOutOfRangeException(nameof(maximum)); }
        uint[][] histograms; int[] inputMap;
        if (contexts > maximum)
        { (histograms, inputMap) = FastCluster(tokens, contexts, prefix, effort <= 2 ? Math.Min(4, maximum) : maximum, cancellationToken); }
        else
        {
            histograms = new uint[contexts][]; inputMap = new int[contexts];
            for (int i = 0; i < contexts; i++) { histograms[i] = new uint[256]; inputMap[i] = i; }
            foreach (JxlToken token in tokens)
            {
                uint symbol = (token.IsLzLength ? JxlEntropyEncoder.LengthConfig : DefaultConfig).Encode(token.Value).Token;
                if (token.IsLzLength) { symbol += JxlEntropyEncoder.MinLzSymbol; }
                histograms[token.Context][symbol]++;
            }
        }
        int count = histograms.Length;
        int[] version = new int[count], parent = new int[count];
        for (int i = 0; i < count; i++) { version[i] = 1; parent[i] = i; }
        PriorityQueue<Pair, (double Cost, int A, int B)> queue = new();
        for (int a = 0; a < count; a++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int b = a + 1; b < count; b++) { Enqueue(a, b); }
        }
        while (queue.TryDequeue(out Pair pair, out _))
        {
            cancellationToken.ThrowIfCancellationRequested();
            int a = pair.A, b = pair.B;
            if (version[a] != pair.VersionA || version[b] != pair.VersionB || version[a] == 0 || version[b] == 0) { continue; }
            for (int s = 0; s < 256; s++) { histograms[a][s] += histograms[b][s]; }
            parent[b] = a; version[b] = 0; version[a]++;
            for (int i = 0; i < count; i++) { if (i != a && version[i] != 0) { Enqueue(Math.Min(a, i), Math.Max(a, i)); } }
        }
        byte[] map = new byte[contexts]; int[] numbering = new int[count]; Array.Fill(numbering, -1); int next = 0;
        for (int i = 0; i < map.Length; i++)
        {
            int root = inputMap[i]; while (parent[root] != root) { root = parent[root]; }
            if (numbering[root] == -1) { numbering[root] = next++; }
            map[i] = (byte)numbering[root];
        }
        return map;

        void Enqueue(int a, int b)
        {
            double cost = MergeCost(histograms[a], histograms[b], prefix);
            if (cost < 0) { queue.Enqueue(new(a, b, version[a], version[b]), (cost, a, b)); }
        }
    }

    // Pick distant seeds, then assign remaining input contexts by their actual
    // distributions. Context numbers have no relationship to symbol content.
    private static (uint[][] Histograms, int[] Map) FastCluster(JxlTokenSequence tokens, int contexts, bool prefix, int maximum,
        CancellationToken cancellationToken)
    {
        uint[][] input = new uint[contexts][]; long[] totals = new long[contexts];
        foreach (JxlToken token in tokens)
        {
            uint symbol = (token.IsLzLength ? JxlEntropyEncoder.LengthConfig : DefaultConfig).Encode(token.Value).Token;
            if (token.IsLzLength) { symbol += JxlEntropyEncoder.MinLzSymbol; }
            input[token.Context] ??= new uint[256]; input[token.Context][symbol]++; totals[token.Context]++;
        }
        int largest = 0; int[] map = new int[contexts]; Array.Fill(map, -1);
        double[] distances = new double[contexts], entropies = new double[contexts];
        int[] symbols = new int[contexts], lengths = new int[contexts];
        for (int i = 0; i < contexts; i++)
        {
            if (totals[i] == 0) { map[i] = 0; continue; }
            if (totals[i] > totals[largest]) { largest = i; }
            distances[i] = double.PositiveInfinity;
            for (int s = 0; s < 256; s++) { if (input[i][s] != 0) { symbols[i]++; lengths[i] = s + 1; entropies[i] -= Term(input[i][s]); } }
            entropies[i] += Term(totals[i]);
        }
        List<uint[]> output = []; List<long> outputTotals = []; List<double> outputEntropies = [];
        List<int> outputSymbols = [], outputLengths = [];
        while (output.Count < maximum)
        {
            cancellationToken.ThrowIfCancellationRequested();
            map[largest] = output.Count; output.Add(input[largest] == null ? new uint[256] : (uint[])input[largest].Clone());
            outputTotals.Add(totals[largest]); outputEntropies.Add(entropies[largest]); outputSymbols.Add(symbols[largest]); outputLengths.Add(lengths[largest]);
            distances[largest] = 0; largest = 0;
            for (int i = 0; i < contexts; i++)
            {
                if (distances[i] == 0) { continue; }
                distances[i] = Math.Min(distances[i], Distance(i, output.Count - 1));
                if (distances[i] > distances[largest]) { largest = i; }
            }
            if (distances[largest] < 48) { break; }
        }
        for (int i = 0; i < contexts; i++)
        {
            if (map[i] != -1) { continue; }
            cancellationToken.ThrowIfCancellationRequested(); int best = 0; double distance = double.PositiveInfinity;
            for (int j = 0; j < output.Count; j++) { double d = Distance(i, j); if (d < distance) { best = j; distance = d; } }
            map[i] = best; outputTotals[best] += totals[i]; outputLengths[best] = Math.Max(outputLengths[best], lengths[i]);
            double entropy = Term(outputTotals[best]); int present = 0;
            for (int s = 0; s < outputLengths[best]; s++)
            { uint count = output[best][s] += input[i][s]; if (count != 0) { present++; entropy -= Term(count); } }
            outputEntropies[best] = entropy; outputSymbols[best] = present;
        }
        return (output.ToArray(), map);

        double Distance(int i, int j)
        {
            if (totals[i] == 0 || outputTotals[j] == 0) { return 0; }
            double merged = Term(totals[i] + outputTotals[j]); int present = 0;
            for (int s = 0; s < Math.Max(lengths[i], outputLengths[j]); s++)
            { long count = (long)input[i][s] + output[j][s]; if (count != 0) { present++; merged -= Term(count); } }
            double result = merged - entropies[i] - outputEntropies[j];
            if (prefix && present > 1) { result = Math.Max(result, (symbols[i] == 1 ? totals[i] : 0) + (outputSymbols[j] == 1 ? outputTotals[j] : 0)); }
            return result;
        }
    }

    internal static JxlIntegerConfig[] ChooseConfigs(JxlToken[] tokens, byte[] clusters, bool prefix, bool lz77, int effort,
        CancellationToken cancellationToken)
        => ChooseConfigs(new JxlTokenSequence(tokens), clusters, prefix, lz77, effort, cancellationToken);

    internal static JxlIntegerConfig[] ChooseConfigs(JxlTokenSequence tokens, byte[] clusters, bool prefix, bool lz77, int effort,
        CancellationToken cancellationToken, int maximumDistinctValues = MaximumDistinctValues)
    {
        if (maximumDistinctValues < 0) { throw new ArgumentOutOfRangeException(nameof(maximumDistinctValues)); }
        int count = 1; foreach (byte cluster in clusters) { count = Math.Max(count, cluster + 1); }
        Dictionary<Value, uint>[] samples = new Dictionary<Value, uint>[count];
        for (int i = 0; i < count; i++) { samples[i] = []; }
        JxlIntegerConfig[] candidates = effort < 7 ? Candidates[..5] : Candidates;
        ConfigHistograms collected = null;
        int distinct = 0; uint position = 0;
        foreach (JxlToken token in tokens)
        {
            if ((position++ & 4095) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
            int cluster = clusters[token.Context];
            Value value = new(token.Value, token.IsLzLength, lz77 && token.Context == clusters.Length - 1);
            if (collected != null) { collected.Add(cluster, value, 1); continue; }
            if (samples[cluster].TryGetValue(value, out uint frequency)) { samples[cluster][value] = frequency + 1; continue; }
            if (distinct < maximumDistinctValues) { samples[cluster].Add(value, 1); distinct++; continue; }
            // Repeated values are cheap to aggregate, but random 32-bit inputs
            // must not grow a dictionary entry for every pixel in the frame.
            // Switch exactly (without sampling) to bounded symbol histograms.
            collected = new(count, candidates, prefix, lz77, effort);
            for (int c = 0; c < count; c++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var entry in samples[c]) { collected.Add(c, entry.Key, entry.Value); }
            }
            samples = null;
            collected.Add(cluster, value, 1);
        }
        JxlIntegerConfig[] configs = new JxlIntegerConfig[count]; Array.Fill(configs, DefaultConfig);
        for (int c = 0; c < count; c++)
        {
            cancellationToken.ThrowIfCancellationRequested(); double best = double.PositiveInfinity;
            for (int candidate = 0; candidate < candidates.Length; candidate++)
            {
                JxlIntegerConfig config = candidates[candidate];
                if (!prefix && config.SplitExponent > 8) { continue; }
                uint[] histogram; long extra; bool valid; int length;
                if (collected != null)
                {
                    ConfigHistogram cell = collected.Cells[c][candidate];
                    histogram = cell.Counts; extra = cell.Extra; valid = cell.Valid; length = cell.Length;
                }
                else
                {
                    histogram = new uint[HistogramCapacity(config, prefix, lz77, effort)]; extra = 0; valid = true; length = 1;
                    foreach (var entry in samples[c])
                    {
                        (uint symbol, int bits, _) = (entry.Key.Length ? JxlEntropyEncoder.LengthConfig : config).Encode(entry.Key.Sample);
                        if (entry.Key.Length) { symbol += JxlEntropyEncoder.MinLzSymbol; }
                        if (symbol >= histogram.Length || (lz77 && !entry.Key.Length && !entry.Key.Distance && symbol >= JxlEntropyEncoder.MinLzSymbol))
                        { valid = false; break; }
                        histogram[symbol] += entry.Value; extra += (long)bits * entry.Value; length = Math.Max(length, (int)symbol + 1);
                    }
                }
                if (!valid) { continue; }
                JxlBitWriter counter = new(Stream.Null); config.Write(counter, prefix ? 15 : 8);
                double cost = extra + counter.Position;
                if (prefix)
                {
                    JxlPrefixEncoder encoder = new(histogram.AsSpan(0, length)); encoder.Write(counter); cost = extra + counter.Position;
                    for (int i = 0; i < length; i++) { cost += (long)histogram[i] * encoder.Lengths[i]; }
                    cost += length == 1 ? 1 : 5 + JxlIntegerConfig.ContextBits((uint)length - 1);
                }
                else { cost += new JxlAnsEncoder(histogram.AsSpan(0, length), 8, Math.Min(effort, 5)).Cost; }
                if (cost < best) { best = cost; configs[c] = config; }
            }
        }
        return configs;
    }

    private static int HistogramCapacity(JxlIntegerConfig config, bool prefix, bool lz77, int effort)
    {
        uint largest = config.Encode(uint.MaxValue).Token;
        if (lz77) { largest = Math.Max(largest, JxlEntropyEncoder.LengthConfig.Encode(uint.MaxValue).Token + JxlEntropyEncoder.MinLzSymbol); }
        return (int)Math.Min(largest + 1, prefix && effort >= 10 ? 32768U : 256U);
    }

    private sealed class ConfigHistogram(int capacity)
    {
        internal readonly uint[] Counts = new uint[capacity];
        internal long Extra;
        internal int Length = 1;
        internal bool Valid = true;
    }

    private sealed class ConfigHistograms
    {
        internal readonly ConfigHistogram[][] Cells;
        private readonly JxlIntegerConfig[] candidates;
        private readonly bool lz77;

        internal ConfigHistograms(int count, JxlIntegerConfig[] candidates, bool prefix, bool lz77, int effort)
        {
            this.candidates = candidates; this.lz77 = lz77; this.Cells = new ConfigHistogram[count][];
            for (int c = 0; c < count; c++)
            {
                this.Cells[c] = new ConfigHistogram[candidates.Length];
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (!prefix && candidates[i].SplitExponent > 8) { continue; }
                    this.Cells[c][i] = new(HistogramCapacity(candidates[i], prefix, lz77, effort));
                }
            }
        }

        internal void Add(int cluster, Value value, uint frequency)
        {
            for (int i = 0; i < this.candidates.Length; i++)
            {
                ConfigHistogram cell = this.Cells[cluster][i];
                if (cell == null || !cell.Valid) { continue; }
                (uint symbol, int bits, _) = (value.Length ? JxlEntropyEncoder.LengthConfig : this.candidates[i]).Encode(value.Sample);
                if (value.Length) { symbol += JxlEntropyEncoder.MinLzSymbol; }
                if (symbol >= cell.Counts.Length || (this.lz77 && !value.Length && !value.Distance && symbol >= JxlEntropyEncoder.MinLzSymbol))
                { cell.Valid = false; continue; }
                cell.Counts[symbol] += frequency; cell.Extra += (long)bits * frequency; cell.Length = Math.Max(cell.Length, (int)symbol + 1);
            }
        }
    }

    private static double MergeCost(uint[] a, uint[] b, bool prefix)
    {
        long totalA = 0, totalB = 0; int symbolsA = 0, symbolsB = 0, symbols = 0;
        double sumA = 0, sumB = 0, sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            totalA += a[i]; totalB += b[i];
            if (a[i] != 0) { symbolsA++; sumA += Term(a[i]); }
            if (b[i] != 0) { symbolsB++; sumB += Term(b[i]); }
            if (a[i] != 0 || b[i] != 0) { symbols++; sum += Term((long)a[i] + b[i]); }
        }
        double entropy = Term(totalA + totalB) - sum - (Term(totalA) - sumA) - (Term(totalB) - sumB);
        if (prefix && symbols > 1)
        { entropy = Math.Max(entropy, (symbolsA == 1 ? totalA : 0) + (symbolsB == 1 ? totalB : 0)); }
        // Cheap header estimate ranks merges. The caller compares the resulting
        // full bitstreams, so estimates cannot increase the chosen stream size.
        return entropy - 16 + ((symbols - symbolsA - symbolsB) * 3);
    }

    private static double Term(long count) => count <= 1 ? 0 : count * Math.Log2(count);
}
