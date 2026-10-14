// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-modular/src/ma.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

using System.Collections.Generic;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Coding;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Modular;

internal sealed class JxlMaTree
{
    internal readonly record struct Node(uint Property, int Value, int Left, int Right, int Cluster, int Predictor, int Offset, uint Multiplier);
    internal Node[] Nodes;
    internal JxlEntropyDecoder Distributions;
    internal bool NeedsSelfCorrection;

    internal static JxlMaTree Read(JxlBitReader reader, MemoryAllocator allocator, int nodeLimit)
    {
        using JxlEntropyDecoder treeDecoder = JxlEntropyDecoder.Read(reader, 6, allocator);
        uint? single = treeDecoder.SingleToken(treeDecoder.Clusters[1]);
        if (single.HasValue && single.Value != 0) { JxlThrowHelper.Invalid("infinite Modular decision tree."); }
        List<Node> nodes = [default];
        List<int> depths = [0];
        int leaves = 0;
        bool selfCorrection = false;
        treeDecoder.Begin(reader);
        for (int i = 0; i < nodes.Count; i++)
        {
            uint property = treeDecoder.Read(reader, 1);
            if (property != 0)
            {
                if (nodes.Count > nodeLimit - 2 || depths[i] >= 2048) { JxlThrowHelper.Invalid("Modular decision tree exceeds size or depth limit."); }
                int value = JxlBitReader.UnpackSigned(treeDecoder.Read(reader, 0));
                nodes[i] = new(property - 1, value, nodes.Count, nodes.Count + 1, -1, 0, 0, 0);
                nodes.Add(default); nodes.Add(default);
                depths.Add(depths[i] + 1); depths.Add(depths[i] + 1);
                selfCorrection |= property - 1 == 15;
            }
            else
            {
                uint predictor = treeDecoder.Read(reader, 2);
                if (predictor > 13) { JxlThrowHelper.Invalid("invalid Modular predictor."); }
                int offset = JxlBitReader.UnpackSigned(treeDecoder.Read(reader, 3));
                uint log = treeDecoder.Read(reader, 4);
                if (log > 30) { JxlThrowHelper.Invalid("invalid Modular multiplier exponent."); }
                uint bits = treeDecoder.Read(reader, 5);
                if (bits > (1U << (31 - (int)log)) - 2) { JxlThrowHelper.Invalid("invalid Modular multiplier."); }
                nodes[i] = new(0, 0, 0, 0, leaves++, (int)predictor, offset, (bits + 1) << (int)log);
                selfCorrection |= predictor == 6;
            }
        }
        treeDecoder.Finish();
        JxlEntropyDecoder distributions = JxlEntropyDecoder.Read(reader, leaves, allocator);
        for (int i = 0; i < nodes.Count; i++)
        {
            Node node = nodes[i];
            if (node.Cluster >= 0) { nodes[i] = node with { Cluster = distributions.Clusters[node.Cluster] }; }
        }
        return new() { Nodes = nodes.ToArray(), Distributions = distributions, NeedsSelfCorrection = selfCorrection };
    }

    internal Node FindLeaf(JxlPredictor predictor, int channel, int stream)
    {
        Node node = this.Nodes[0];
        while (node.Cluster < 0)
        {
            int value = node.Property switch { 0 => channel, 1 => stream, _ => predictor.Property(node.Property) };
            node = this.Nodes[value > node.Value ? node.Left : node.Right];
        }
        return node;
    }
}
