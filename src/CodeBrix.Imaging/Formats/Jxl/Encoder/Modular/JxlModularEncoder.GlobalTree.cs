// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Global tree/histogram organization follows libjxl lib/jxl/enc_modular.cc and
// lib/jxl/modular/encoding/enc_encoding.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.IO;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Entropy;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Memory;
using static CodeBrix.Imaging.Formats.Jxl.Bitstream.JxlU32;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Modular;

internal static partial class JxlModularEncoder
{
    internal static byte[][] GlobalPayloads(JxlImageHeader image, JxlFrameHeader frame, JxlModularPlane[] planes, int rct,
        JxlPaletteEncoder palette, bool prefix, int effort, MemoryAllocator allocator, CancellationToken cancellationToken, JxlPatches patches = null,
        JxlModularSearchOptions search = null)
    {
        int groups = (int)frame.GroupCount, lfGroups = (int)frame.LfGroupCount, dimension = (int)frame.GroupDimension;
        int columns = (int)JxlFrameHeader.DivideRoundUp(frame.Width, frame.GroupDimension);
        JxlWeightedPredictorHeader weighted = search is { NoWeighted: true } ? new() : JxlWeightedPredictorEncoder.Select(planes, effort, allocator, cancellationToken);
        JxlMaEncoder tree = JxlMaEncoder.Learn(planes, effort, weighted, allocator, cancellationToken, (int)image.BitDepth.Bits, search);
        JxlToken[][] streams = new JxlToken[groups == 1 ? 1 : groups + 1][];
        uint[] widths = new uint[streams.Length];
        JxlModularPlane[] global = groups == 1 ? planes : palette == null ? [] : planes[..palette.MetaChannels];
        streams[0] = tree.CreateTokens(global, weighted, allocator, cancellationToken);
        foreach (JxlModularPlane plane in global) { widths[0] = Math.Max(widths[0], (uint)plane.Width); }
        if (groups != 1)
        {
            // The learned tree is read-only here; each group fills only its own slot.
            JxlParallel.ForEncoder(groups, cancellationToken, group =>
            {
                int left = group % columns * dimension, top = group / columns * dimension, first = palette?.MetaChannels ?? 0;
                JxlModularPlane[] views = new JxlModularPlane[planes.Length - first];
                for (int c = 0; c < views.Length; c++)
                {
                    JxlModularPlane plane = planes[c + first];
                    views[c] = plane.View(left, top, Math.Min(dimension, plane.Width - left), Math.Min(dimension, plane.Height - top));
                    widths[group + 1] = Math.Max(widths[group + 1], (uint)views[c].Width);
                }
                streams[group + 1] = tree.CreateTokens(views, weighted, allocator, cancellationToken);
            });
        }
        tree.RefineMultipliers(streams, cancellationToken);
        JxlEntropyEncoder encoder = JxlEntropyEncoder.CreateOptimized(new JxlTokenSequence(streams), tree.ContextCount, prefix, effort, false, cancellationToken, search?.MaximumHistograms ?? 256);
        JxlToken[][] matches = new JxlToken[streams.Length][]; bool haveMatch = false;
        JxlToken[][] sources = streams;
        JxlParallel.ForEncoder(streams.Length, cancellationToken, i =>
        {
            matches[i] = JxlLz77.Match(sources[i], tree.ContextCount, widths[i], effort, prefix, cancellationToken);
        });
        for (int i = 0; i < streams.Length; i++)
        {
            if (matches[i].Length == 0) { matches[i] = streams[i]; } else { haveMatch = true; }
        }
        if (haveMatch)
        {
            JxlEntropyEncoder alternative = JxlEntropyEncoder.CreateOptimized(new JxlTokenSequence(matches), tree.ContextCount, prefix, effort, true, cancellationToken, search?.MaximumHistograms ?? 256);
            if (Cost(alternative, matches, cancellationToken) < Cost(encoder, streams, cancellationToken)) { encoder = alternative; streams = matches; }
        }
        byte[][] payloads = new byte[groups == 1 ? 1 : 2 + lfGroups + groups][]; Array.Fill(payloads, Array.Empty<byte>());
        using (MemoryStream stream = new())
        {
            JxlBitWriter writer = new(stream); patches?.Write(writer, image, frame, effort, cancellationToken);
            writer.WriteBool(true); writer.WriteBool(true); // Default LF weights, global MA tree.
            tree.WriteTree(writer, cancellationToken); encoder.WriteHeader(writer);
            Header(writer, rct, palette); encoder.WriteTokens(writer, streams[0], cancellationToken);
            writer.ZeroPadToByte(); payloads[0] = stream.ToArray();
        }
        if (groups != 1)
        {
            JxlToken[][] selected = streams; JxlEntropyEncoder model = encoder;
            JxlParallel.ForEncoder(groups, cancellationToken, group =>
            {
                using MemoryStream stream = new(); JxlBitWriter writer = new(stream);
                Header(writer, 0, null); model.WriteTokens(writer, selected[group + 1], cancellationToken);
                writer.ZeroPadToByte(); payloads[2 + lfGroups + group] = stream.ToArray();
            });
        }
        return payloads;

        void Header(JxlBitWriter writer, int colorTransform, JxlPaletteEncoder paletteTransform)
        {
            writer.WriteBool(true); weighted.Write(writer);
            writer.WriteU32((colorTransform != 0 ? 1U : 0) + (uint)(paletteTransform?.TransformCount ?? 0), 0, 1, U(4, 2), U(8, 18));
            if (colorTransform != 0)
            {
                writer.WriteBits(2, 0); writer.WriteU32(0, U(3), U(6, 8), U(10, 72), U(13, 1096));
                writer.WriteU32((uint)colorTransform, 6, U(2), U(4, 2), U(6, 10));
            }
            paletteTransform?.WriteHeader(writer);
        }
    }

    private static long Cost(JxlEntropyEncoder encoder, JxlToken[][] streams, CancellationToken cancellationToken)
    {
        JxlBitWriter header = new(Stream.Null); encoder.WriteHeader(header);
        long bits = -header.Position * (streams.Length - 1);
        foreach (JxlToken[] stream in streams) { bits += encoder.GetBitCount(stream, cancellationToken); }
        return bits;
    }
}
