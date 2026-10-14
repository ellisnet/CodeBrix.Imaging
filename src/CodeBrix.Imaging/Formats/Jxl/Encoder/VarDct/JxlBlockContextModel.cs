// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from libjxl lib/jxl/enc_heuristics.cc and entropy_coder.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Collections.Generic;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Entropy;
using CodeBrix.Imaging.Formats.Jxl.VarDct;
using CodeBrix.Imaging.Memory;
using static CodeBrix.Imaging.Formats.Jxl.Bitstream.JxlU32;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;

internal sealed class JxlBlockContextModel
{
    internal readonly uint[] QuantThresholds = [];
    internal readonly byte[] Map = [0, 1, 2, 2, 3, 3, 4, 5, 6, 6, 6, 6, 6,
        7, 8, 9, 9, 10, 11, 12, 13, 14, 14, 14, 14, 14, 7, 8, 9, 9, 10, 11, 12, 13, 14, 14, 14, 14, 14];
    internal readonly int Count = 15;
    private readonly bool defaults = true;

    internal JxlBlockContextModel(Buffer2D<JxlVarBlock> blocks, float distance, int effort, CancellationToken cancellationToken, int decodingSpeed = 0)
    {
        if (decodingSpeed > 0)
        {
            this.Map = new byte[39]; Array.Fill(this.Map, (byte)1, 13, 26);
            this.Count = 2; this.defaults = false; return;
        }
        long total = (long)blocks.Width * blocks.Height, modelSize = Math.Max(1, (long)(1024 * distance));
        if (effort <= 3 || total < modelSize) { return; }
        long[] quantCounts = new long[256]; long[,] orderCounts = new long[13, 256];
        for (int y = 0; y < blocks.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (JxlVarBlock block in blocks.DangerousGetRowSpan(y))
            {
                if (block.Multiplier <= 0) { continue; }
                (int w, int h) = block.Transform.Blocks(); int q = block.Multiplier - 1;
                quantCounts[q] += w * h; orderCounts[block.Transform.OrderIndex(), q] += w * h;
            }
        }
        int segments = total < 8192 * distance ? 1 : 2;
        long cumulative = 0, next = 1, cut = total / segments; int lastCut = 256;
        List<uint> thresholds = [];
        for (int q = 0; q < 256; q++)
        {
            cumulative += quantCounts[q];
            if (cumulative > cut)
            {
                if (q != 0) { thresholds.Add((uint)q); } lastCut = q;
                while (cumulative > cut) { cut = total * ++next / segments; }
            }
            else if (next > thresholds.Count + 1 && q - 1 == lastCut && q != 0) { thresholds.Add((uint)q); }
        }
        this.QuantThresholds = thresholds.ToArray(); segments = thresholds.Count + 1;
        long[] counts = new long[13 * segments]; int position = 0;
        for (int q = 0; q < 256; q++)
        {
            if (position < thresholds.Count && q == thresholds[position]) { position++; }
            for (int order = 0; order < 13; order++) { counts[order * segments + position] += orderCounts[order, q]; }
        }
        int wanted = (int)Math.Clamp(total / modelSize / 2, 2, 9), chroma = (int)Math.Clamp(total / modelSize / 3, 1, 5);
        int[] parents = new int[counts.Length]; List<int> clusters = [];
        for (int i = 0; i < parents.Length; i++) { parents[i] = i; clusters.Add(i); }
        while (clusters.Count > wanted)
        {
            clusters.Sort((a, b) => { int result = counts[b].CompareTo(counts[a]); return result != 0 ? result : a.CompareTo(b); });
            int a = clusters[^2], b = clusters[^1]; counts[a] += counts[b]; counts[b] = 0;
            parents[b] = a; clusters.RemoveAt(clusters.Count - 1);
        }
        int[] numbering = new int[counts.Length]; Array.Fill(numbering, -1);
        byte[] remap = new byte[counts.Length]; int count = 0;
        for (int i = 0; i < remap.Length; i++)
        {
            int root = i; while (parents[root] != root) { root = parents[root]; }
            if (numbering[root] == -1) { numbering[root] = count++; } remap[i] = (byte)numbering[root];
        }
        this.Map = new byte[remap.Length * 3]; remap.CopyTo(this.Map, 0);
        for (int i = remap.Length; i < this.Map.Length; i++) { this.Map[i] = (byte)(count + Math.Min(remap[i % remap.Length], chroma - 1)); }
        this.Count = count + Math.Min(count, chroma); this.defaults = false;
    }

    internal int Context(int channelOrder, JxlVarBlock block)
    {
        int quant = 0; foreach (uint threshold in this.QuantThresholds) { if (block.Multiplier > threshold) { quant++; } }
        return this.Map[(channelOrder * 13 + block.Transform.OrderIndex()) * (this.QuantThresholds.Length + 1) + quant];
    }

    internal void Write(JxlBitWriter writer)
    {
        writer.WriteBool(this.defaults); if (this.defaults) { return; }
        for (int c = 0; c < 3; c++) { writer.WriteBits(4, 0); }
        writer.WriteBits(4, (uint)this.QuantThresholds.Length);
        foreach (uint threshold in this.QuantThresholds) { writer.WriteU32(threshold - 1, U(2), U(3, 4), U(5, 12), U(8, 44)); }
        JxlEntropyEncoder.WriteContextMap(writer, this.Map);
    }
}
