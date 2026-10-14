// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-modular/src/lib.rs and image.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

using System;
using System.Collections.Generic;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Coding;
using static CodeBrix.Imaging.Formats.Jxl.Bitstream.JxlU32;

namespace CodeBrix.Imaging.Formats.Jxl.Modular;

internal sealed class JxlModularImage
{
    private JxlWeightedPredictorHeader weighted;
    private JxlModularTransform[] transforms;
    private JxlMaTree tree;
    internal List<JxlModularPlane> Channels;
    internal int MetaChannels;
    internal int GlobalChannelCount;

    internal static JxlModularImage Read(JxlBitReader reader, List<JxlModularPlane> planes, JxlMaTree globalTree, JxlModularWorkspace workspace)
    {
        JxlModularImage image = new() { Channels = new(planes) };
        bool useGlobal = reader.ReadBool();
        image.weighted = JxlWeightedPredictorHeader.Read(reader);
        int count = (int)reader.ReadU32(0, 1, U(4, 2), U(8, 18));
        image.transforms = new JxlModularTransform[count];
        for (int i = 0; i < count; i++) { image.transforms[i] = JxlModularTransform.Read(reader, image.weighted); }
        foreach (JxlModularTransform transform in image.transforms) { transform.Prepare(image.Channels, ref image.MetaChannels, workspace); }
        if (image.Channels.Count > 65536) { JxlThrowHelper.Invalid("too many transformed Modular channels."); }
        if (useGlobal)
        {
            image.tree = globalTree;
            if (image.tree == null) { JxlThrowHelper.Invalid("global Modular tree is not available."); }
        }
        else
        {
            long samples = 1024;
            foreach (JxlModularPlane plane in image.Channels) { samples += (long)plane.Width * plane.Height; }
            image.tree = JxlMaTree.Read(reader, workspace.Allocator, (int)Math.Min(samples, 1 << 20));
        }
        return image;
    }

    internal void DecodeGlobal(JxlBitReader reader, int groupDimension, JxlModularWorkspace workspace)
    {
        int count = 0;
        foreach (JxlModularPlane channel in this.Channels)
        {
            if (count >= this.MetaChannels && (channel.Width > groupDimension || channel.Height > groupDimension)) { break; }
            count++;
        }
        this.GlobalChannelCount = count;
        this.DecodeChannels(reader, this.Channels.GetRange(0, count), 0, workspace);
    }

    internal void DecodeAll(JxlBitReader reader, int streamIndex, JxlModularWorkspace workspace) =>
        this.DecodeChannels(reader, this.Channels, streamIndex, workspace);

    private void DecodeChannels(JxlBitReader reader, List<JxlModularPlane> channels, int streamIndex, JxlModularWorkspace workspace)
    {
        uint distance = 0;
        foreach (JxlModularPlane channel in channels) { distance = Math.Max(distance, (uint)channel.Width); }
        using JxlEntropyDecoder decoder = this.tree.Distributions.CreateStream();
        decoder.Begin(reader);
        bool needsPrevious = Array.Exists(this.tree.Nodes, node => node.Cluster < 0 && node.Property >= 16);
        Dictionary<(int Width, int Height, int HShift, int VShift), List<JxlModularPlane>> matching = needsPrevious ? new() : null;
        for (int c = 0; c < channels.Count; c++)
        {
            JxlModularPlane channel = channels[c];
            if (channel.Width == 0 || channel.Height == 0) { continue; }
            // Keep each geometry's channels once, in decode order. The predictor
            // indexes from the end, avoiding a copied reverse list per channel.
            List<JxlModularPlane> previous = null;
            if (matching != null)
            {
                var geometry = (channel.Width, channel.Height, channel.HorizontalShift, channel.VerticalShift);
                if (!matching.TryGetValue(geometry, out previous)) { matching.Add(geometry, previous = []); }
            }
            using JxlPredictor predictor = new(channel, previous ?? (IReadOnlyList<JxlModularPlane>)Array.Empty<JxlModularPlane>(),
                this.tree.NeedsSelfCorrection ? this.weighted : null, workspace.Allocator, previousNewestFirst: false);
            for (int y = 0; y < channel.Height; y++)
            {
                reader.CheckCancellation();
                Span<int> row = channel.Row(y);
                ReadOnlySpan<int> above = y == 0 ? default : channel.Row(y - 1);
                ReadOnlySpan<int> above2 = y < 2 ? default : channel.Row(y - 2);
                for (int x = 0; x < channel.Width; x++)
                {
                    predictor.Prepare(x, y, row, above, above2);
                    JxlMaTree.Node leaf = this.tree.FindLeaf(predictor, c, streamIndex);
                    int residual = JxlBitReader.UnpackSigned(decoder.ReadClustered(reader, leaf.Cluster, distance));
                    int sample = unchecked((residual * (int)leaf.Multiplier) + leaf.Offset + predictor.Predict(leaf.Predictor));
                    predictor.Record(sample, row);
                }
            }
            previous?.Add(channel);
        }
        decoder.Finish();
    }

    internal void Finish(int bitDepth, JxlModularWorkspace workspace)
    {
        for (int i = this.transforms.Length - 1; i >= 0; i--) { this.transforms[i].Undo(this.Channels, bitDepth, workspace); }
    }
}
