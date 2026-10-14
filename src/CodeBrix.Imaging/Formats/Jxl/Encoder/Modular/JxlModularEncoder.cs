// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Uses the inverse of the attributed managed Modular decoder's field grammar,
// reversible YCoCg transform and clamped-gradient prediction. Huffman coding is
// provided by the separately attributed libjxl adaptation.

using System;
using System.Collections.Generic;
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
    // Input planes belong to the encoder workspace and may be transformed in place.
    internal static void WriteFrame(JxlBitWriter output, JxlImageHeader image, JxlFrameHeader frame,
        JxlModularPlane[] planes, CancellationToken cancellationToken, bool usePrefix = true, int entropyEffort = 7, MemoryAllocator allocator = null,
        JxlPatches patches = null, int decodingSpeed = 0)
    {
        if (!frame.Modular || frame.PassCount != 1) { throw new ArgumentException("Expected a single-pass Modular frame.", nameof(frame)); }
        if (patches != null) { frame.Flags |= 2; }
        else if ((frame.Flags & 2) != 0) { throw new ArgumentException("Missing JPEG XL patch dictionary.", nameof(patches)); }
        allocator ??= Configuration.Default.MemoryAllocator;
        if (entropyEffort == 11)
        { WriteExpert(output, image, frame, planes, usePrefix, allocator, cancellationToken, patches, decodingSpeed); return; }
        JxlModularSearchOptions search = JxlModularSearchOptions.ForDecodingSpeed(null, decodingSpeed, entropyEffort, frame.GroupSizeShift);
        frame.GroupSizeShift = search?.GroupShift ?? frame.GroupSizeShift;
        WriteFrameCore(output, image, frame, planes, usePrefix, entropyEffort, allocator, cancellationToken, patches, search);
    }

    private static void WriteFrameCore(JxlBitWriter output, JxlImageHeader image, JxlFrameHeader frame, JxlModularPlane[] planes,
        bool usePrefix, int entropyEffort, MemoryAllocator allocator, CancellationToken cancellationToken, JxlPatches patches, JxlModularSearchOptions search = null)
    {
        JxlModularEncodingCache cache = entropyEffort >= 4 ? new(Math.Min(16L << 20, allocator.MemoryGroupAllocationLimitBytes / 16)) : null;
        bool estimatePalettes = entropyEffort is >= 4 and < 10 && frame.GroupCount > 1 && (long)frame.Width * frame.Height > 65536;
        byte[][] beforeColor = null;
        if (entropyEffort >= 2 && image.ColorChannels == 3 && image.BitDepth.ExponentBits == 0 && (search == null || search.PaletteColors != 0))
        {
            // Palette detection precedes the global color transform in libjxl.
            // Preserve those candidates: RCT can hide the implicit RGB cubes.
            int maximum = search?.PaletteColors ?? (entropyEffort <= 3 ? 256 : entropyEffort <= 7 ? 1024 : 4096);
            var selected = estimatePalettes ? SelectGlobalPalette(planes, 3, entropyEffort >= 7 ? 2 : 1, maximum, 0,
                (int)image.BitDepth.Bits, allocator, cancellationToken) : default;
            for (int channels = Math.Min(4, planes.Length); channels >= 3; channels--)
            {
                for (int order = 1; order <= (entropyEffort >= 7 ? 2 : 1); order++)
                {
                    if (estimatePalettes && selected != (channels, order)) { continue; }
                    using JxlModularWorkspace workspace = JxlModularWorkspace.ForEncoding(allocator, Math.Max(262144, (long)frame.Width * frame.Height));
                    JxlPaletteEncoder palette = JxlPaletteEncoder.TryCreate(planes, channels, search?.PaletteColors ?? (entropyEffort <= 3 ? 256 : entropyEffort <= 7 ? 1024 : 4096),
                        workspace, cancellationToken, order: (JxlPaletteOrder)order, bitDepth: (int)image.BitDepth.Bits);
                    if (palette == null) { continue; }
                    byte[][] candidate = LocalPayloads(image, frame, palette.Planes, 0, palette, usePrefix, entropyEffort, allocator, cancellationToken, patches, search, cache);
                    if (beforeColor == null || Size(candidate) < Size(beforeColor)) { beforeColor = candidate; }
                    if (entropyEffort >= 10)
                    {
                        candidate = GlobalPayloads(image, frame, palette.Planes, 0, palette, usePrefix, entropyEffort, allocator, cancellationToken, patches, search);
                        if (Size(candidate) < Size(beforeColor)) { beforeColor = candidate; }
                    }
                }
            }
        }
        int rct = image.ColorChannels == 3 && image.BitDepth.ExponentBits == 0 ? JxlRctEncoder.Select(planes, entropyEffort, cancellationToken) : 0;
        if (rct != 0) { JxlRctEncoder.Apply(planes, rct, cancellationToken); }
        byte[][] payloads = LocalPayloads(image, frame, planes, rct, null, usePrefix, entropyEffort, allocator, cancellationToken, patches, search, cache);
        if (beforeColor != null && Size(beforeColor) < Size(payloads)) { payloads = beforeColor; }
        if (entropyEffort >= 10)
        {
            byte[][] global = GlobalPayloads(image, frame, planes, rct, null, usePrefix, entropyEffort, allocator, cancellationToken, patches, search);
            if (Size(global) < Size(payloads)) { payloads = global; }
        }
        if (entropyEffort >= 2 && frame.GroupCount > 1)
        {
            int maximum = search?.PaletteColors ?? (entropyEffort <= 3 ? 256 : entropyEffort <= 7 ? 1024 : 4096);
            int channelMaximum = search == null || search.GlobalPalettePercent < 0 ? maximum : (int)Math.Min(70911, (long)frame.Width * frame.Height * search.GlobalPalettePercent / 100);
            var selected = estimatePalettes ? SelectGlobalPalette(planes, 0, 0, maximum, channelMaximum,
                (int)image.BitDepth.Bits, allocator, cancellationToken) : default;
            for (int channels = Math.Min(4, planes.Length); channels >= 0; channels--)
            {
                if (estimatePalettes && selected.Channels != channels) { continue; }
                if (search != null && (channels == 0 ? search.GlobalPalettePercent == 0 : maximum == 0)) { continue; }
                using JxlModularWorkspace workspace = JxlModularWorkspace.ForEncoding(allocator, Math.Max(262144, (long)frame.Width * frame.Height));
                JxlPaletteEncoder palette = channels == 0 ? JxlPaletteEncoder.TryIndependent(planes, channelMaximum, workspace, cancellationToken) :
                    JxlPaletteEncoder.TryCreate(planes, channels, maximum, workspace, cancellationToken);
                if (palette == null) { continue; }
                byte[][] candidate = LocalPayloads(image, frame, palette.Planes, rct, palette, usePrefix, entropyEffort, allocator, cancellationToken, patches, search, cache);
                if (Size(candidate) < Size(payloads)) { payloads = candidate; }
                if (entropyEffort >= 10)
                {
                    candidate = GlobalPayloads(image, frame, palette.Planes, rct, palette, usePrefix, entropyEffort, allocator, cancellationToken, patches, search);
                    if (Size(candidate) < Size(payloads)) { payloads = candidate; }
                }
            }
        }
        frame.Write(output, image);
        uint[] sizes = new uint[payloads.Length];
        for (int i = 0; i < sizes.Length; i++) { sizes[i] = checked((uint)payloads[i].Length); }
        JxlFrameToc.WriteOrdered(output, frame, sizes);
        foreach (byte[] payload in payloads) { cancellationToken.ThrowIfCancellationRequested(); output.WriteBytes(payload); }
    }

    private static long Size(byte[][] payloads)
    { long size = 0; foreach (byte[] payload in payloads) { size += payload.Length; } return size; }

    private static void WriteExpert(JxlBitWriter output, JxlImageHeader image, JxlFrameHeader frame, JxlModularPlane[] planes,
        bool prefix, MemoryAllocator allocator, CancellationToken cancellationToken, JxlPatches patches, int decodingSpeed)
    {
        // Candidates are complete, independent encodings of private plane copies
        // and frame headers. They run concurrently in two stages (the second list
        // depends on the probes), and the earliest smallest result in the original
        // evaluation order wins. Each candidate copies the image, so concurrency is
        // capped to bound memory.
        int degree = Math.Min(4, JxlParallel.EncoderParallelism is -1 ? 4 : JxlParallel.EncoderParallelism);
        List<(byte[] Data, uint Shift)> results = [];
        Dictionary<JxlModularSearchOptions, long> measured = [];
        JxlModularSearchOptions[] probes = JxlModularSearchOptions.Probes;
        // Retain ordinary effort 10, so the expert search cannot regress it.
        JxlModularSearchOptions ordinaryProbe = Normalize(probes[0]), paletteProbe = Normalize(probes[1]);
        List<(JxlModularSearchOptions Search, int Effort)> first = [(null, 10)];
        foreach (JxlModularSearchOptions probe in new[] { ordinaryProbe, paletteProbe })
        { if (probe != null && !first.Exists(item => item.Effort == 11 && item.Search == probe)) { first.Add((probe, 11)); } }
        Run(first);
        long ordinary = ordinaryProbe == null ? long.MaxValue : measured[ordinaryProbe];
        long palette = paletteProbe == null ? long.MaxValue : measured[paletteProbe];
        List<(JxlModularSearchOptions Search, int Effort)> second = [];
        foreach (JxlModularSearchOptions candidate in JxlModularSearchOptions.Candidates(palette < ordinary))
        {
            JxlModularSearchOptions search = Normalize(candidate);
            if (search != null && !measured.ContainsKey(search) && !second.Exists(item => item.Search == search)) { second.Add((search, 11)); }
        }
        Run(second);
        (byte[] best, uint selected) = results[0];
        foreach ((byte[] data, uint shift) in results) { if (data.LongLength < best.LongLength) { best = data; selected = shift; } }
        frame.GroupSizeShift = selected; output.ZeroPadToByte(); output.WriteBytes(best);

        void Run(List<(JxlModularSearchOptions Search, int Effort)> items)
        {
            (byte[] Data, uint Shift)[] encoded = new (byte[], uint)[items.Count];
            JxlParallel.For(items.Count, degree, cancellationToken, i => encoded[i] = EncodeCandidate(items[i].Search, items[i].Effort));
            for (int i = 0; i < items.Count; i++)
            {
                results.Add(encoded[i]);
                if (items[i].Effort == 11) { measured.Add(items[i].Search, encoded[i].Data.LongLength); }
            }
        }

        // Returns null when the candidate is not applicable (it was never encoded).
        JxlModularSearchOptions Normalize(JxlModularSearchOptions search)
        {
            search = JxlModularSearchOptions.ForDecodingSpeed(search, decodingSpeed, 11, frame.GroupSizeShift);
            if (patches != null && !search.Patches) { return null; }
            // The outer patch search compares complete unpatched/patched frames.
            // With no dictionary, patch-on and patch-off variants are identical.
            if (patches == null) { search = search with { Patches = false }; }
            // The native sampling multiplier saturates at one. Group sizes
            // giving the same single group also have identical encoder input.
            if (search.Sampling * 1.3f >= 1) { search = search with { Sampling = 1 }; }
            uint shift = search.GroupShift;
            if (frame.Width <= (128U << (int)shift) && frame.Height <= (128U << (int)shift))
            {
                while (shift > 0 && frame.Width <= (128U << (int)(shift - 1)) && frame.Height <= (128U << (int)(shift - 1))) { shift--; }
                search = search with { GroupShift = shift };
            }
            return search;
        }

        (byte[] Data, uint Shift) EncodeCandidate(JxlModularSearchOptions search, int learningEffort)
        {
            cancellationToken.ThrowIfCancellationRequested();
            JxlFrameHeader candidateFrame = frame.Clone();
            search = JxlModularSearchOptions.ForDecodingSpeed(search, decodingSpeed, learningEffort, candidateFrame.GroupSizeShift);
            candidateFrame.GroupSizeShift = search?.GroupShift ?? 1;
            using JxlModularWorkspace workspace = JxlModularWorkspace.ForEncoding(allocator, Math.Max(262144, (long)frame.Width * frame.Height));
            JxlModularPlane[] copy = new JxlModularPlane[planes.Length];
            for (int c = 0; c < copy.Length; c++)
            {
                JxlModularPlane source = planes[c]; copy[c] = workspace.Allocate(source.Width, source.Height, source.HorizontalShift, source.VerticalShift);
                for (int y = 0; y < source.Height; y++) { cancellationToken.ThrowIfCancellationRequested(); source.Row(y).CopyTo(copy[c].Row(y)); }
            }
            using MemoryStream candidate = new(); JxlBitWriter writer = new(candidate);
            WriteFrameCore(writer, image, candidateFrame, copy, prefix, learningEffort, allocator, cancellationToken, patches, search);
            return (candidate.ToArray(), candidateFrame.GroupSizeShift);
        }
    }

    private static byte[][] LocalPayloads(JxlImageHeader image, JxlFrameHeader frame, JxlModularPlane[] planes, int rct,
        JxlPaletteEncoder palette, bool usePrefix, int entropyEffort, MemoryAllocator allocator, CancellationToken cancellationToken, JxlPatches patches,
        JxlModularSearchOptions search = null, JxlModularEncodingCache cache = null)
    {
        int groups = checked((int)frame.GroupCount), lfGroups = checked((int)frame.LfGroupCount);
        byte[][] payloads = new byte[groups == 1 ? 1 : 2 + lfGroups + groups][];
        Array.Fill(payloads, Array.Empty<byte>());
        using (MemoryStream global = new())
        {
            JxlBitWriter writer = new(global);
            patches?.Write(writer, image, frame, entropyEffort, cancellationToken);
            writer.WriteBool(true); // Default LF dequantization (unused for non-XYB lossless).
            writer.WriteBool(false); // The global Modular image owns its local tree.
            if (palette == null)
            { WriteStream(writer, groups == 1 ? planes : [], rct, usePrefix, entropyEffort, image.BitDepth.ExponentBits == 0 && image.BitDepth.Bits <= 16, allocator, cancellationToken, (int)image.BitDepth.Bits, search, cache); }
            else
            { WriteStreamCore(writer, groups == 1 ? planes : planes[..palette.MetaChannels], rct, palette, null, usePrefix, entropyEffort, allocator, cancellationToken, (int)image.BitDepth.Bits, search: search, cache: cache); }
            writer.ZeroPadToByte(); payloads[0] = global.ToArray();
        }
        if (groups != 1)
        {
            int dimension = (int)frame.GroupDimension, columns = (int)JxlFrameHeader.DivideRoundUp(frame.SampleWidth, frame.GroupDimension);
            // Groups are independent streams; each writes only its own payload slot.
            JxlParallel.ForEncoder(groups, cancellationToken, group =>
            {
                int left = group % columns * dimension, top = group / columns * dimension;
                int first = palette?.MetaChannels ?? 0;
                JxlModularPlane[] views = new JxlModularPlane[planes.Length - first];
                for (int c = 0; c < views.Length; c++)
                {
                    JxlModularPlane plane = planes[c + first];
                    views[c] = plane.View(left, top, Math.Min(dimension, plane.Width - left), Math.Min(dimension, plane.Height - top));
                }
                using MemoryStream pass = new(); JxlBitWriter writer = new(pass);
                WriteStream(writer, views, 0, usePrefix, entropyEffort, image.BitDepth.ExponentBits == 0 && image.BitDepth.Bits <= 16, allocator, cancellationToken, (int)image.BitDepth.Bits, search, cache); writer.ZeroPadToByte();
                payloads[2 + lfGroups + group] = pass.ToArray();
            });
        }
        return payloads;
    }

    // Below this effort every transform candidate is fully encoded. From effort 5
    // through 8, candidates are ranked by a fast fixed-tree encoding and only the
    // untransformed stream plus the best ranked alternatives are fully encoded.
    private const int ExhaustiveCandidateEffort = 9, RankedCandidates = 2, RankingEffort = 2;

    private static void WriteStream(JxlBitWriter writer, JxlModularPlane[] planes, int rct, bool usePrefix, int entropyEffort, bool allowSqueeze,
        MemoryAllocator allocator, CancellationToken cancellationToken, int bitDepth, JxlModularSearchOptions search = null, JxlModularEncodingCache cache = null)
    {
        if (planes.Length == 0)
        { WriteStreamCore(writer, planes, rct, null, null, usePrefix, entropyEffort, allocator, cancellationToken, bitDepth, search: search); return; }
        // Every candidate is a complete, independent encoding. They run concurrently
        // and the earliest smallest one wins, exactly as the sequential comparison
        // (strictly smaller replaces the current choice) would select.
        List<Func<bool, List<(byte[] Data, long Bits)>>> candidates = [full => [Candidate(planes, null, null, 0, full)]];
        int alwaysFull = 1; // The untransformed stream and the local RCT stream are always fully encoded.
        JxlModularWorkspace rctWorkspace = null;
        try
        {
            if (entropyEffort >= 5 && allowSqueeze && planes.Length >= 3 &&
                planes[0].Width == planes[1].Width && planes[0].Width == planes[2].Width &&
                planes[0].Height == planes[1].Height && planes[0].Height == planes[2].Height && planes[0].HorizontalShift >= 0)
            {
                int localRct = JxlRctEncoder.Select(planes, entropyEffort, cancellationToken);
                if (localRct != 0)
                {
                    rctWorkspace = JxlModularWorkspace.ForEncoding(allocator, Math.Max(262144, (long)planes[0].Width * planes[0].Height));
                    JxlModularPlane[] transformed = (JxlModularPlane[])planes.Clone();
                    for (int c = 0; c < 3; c++)
                    {
                        JxlModularPlane source = planes[c]; transformed[c] = rctWorkspace.Allocate(source.Width, source.Height, source.HorizontalShift, source.VerticalShift);
                        for (int y = 0; y < source.Height; y++) { source.Row(y).CopyTo(transformed[c].Row(y)); }
                    }
                    JxlRctEncoder.Apply(transformed, localRct, cancellationToken);
                    candidates.Add(full => [Candidate(transformed, null, null, localRct, full)]); alwaysFull = 2;
                    SearchTransforms(transformed, localRct);
                }
            }
            SearchTransforms(planes, 0);
            bool ranked = entropyEffort is >= 5 and < ExhaustiveCandidateEffort && candidates.Count > RankedCandidates + alwaysFull;
            bool[] selected = new bool[candidates.Count];
            if (ranked)
            {
                // Rank by a fast fixed-tree encoding of the same transformed planes.
                long[] estimates = new long[candidates.Count];
                JxlParallel.ForEncoder(candidates.Count - alwaysFull, cancellationToken, i =>
                {
                    long estimate = long.MaxValue;
                    foreach ((_, long bits) in candidates[i + alwaysFull](false)) { estimate = Math.Min(estimate, bits); }
                    estimates[i + alwaysFull] = estimate;
                });
                for (int i = 0; i < alwaysFull; i++) { selected[i] = true; }
                for (int pick = 0; pick < RankedCandidates; pick++)
                {
                    int best = -1;
                    for (int i = alwaysFull; i < candidates.Count; i++)
                    { if (!selected[i] && estimates[i] != long.MaxValue && (best < 0 || estimates[i] < estimates[best])) { best = i; } }
                    if (best >= 0) { selected[best] = true; }
                }
            }
            else { Array.Fill(selected, true); }
            List<(byte[] Data, long Bits)>[] results = new List<(byte[] Data, long Bits)>[candidates.Count];
            JxlParallel.ForEncoder(candidates.Count, cancellationToken, i => results[i] = selected[i] ? candidates[i](true) : []);
            (byte[] data, long bits) = results[0][0];
            foreach (List<(byte[] Data, long Bits)> group in results)
            { foreach ((byte[] alternative, long alternativeBits) in group) { if (alternativeBits < bits) { data = alternative; bits = alternativeBits; } } }
            writer.WriteBitstream(data, bits);
        }
        finally { rctWorkspace?.Dispose(); }

        void SearchTransforms(JxlModularPlane[] input, int localRct)
        {
            int maximum = search?.PaletteColors ?? (entropyEffort <= 3 ? 256 : entropyEffort <= 7 ? 1024 : 4096);
            // Compare full encoded streams, including the palette, tree, histograms
            // and entropy payload. A palette is never selected on cardinality alone.
            for (int channels = input.Length; channels >= 0; channels--)
            {
                if (channels > 4 || (entropyEffort == 1 && channels != input.Length) || (channels != input.Length && channels != 0 && entropyEffort < 5)) { continue; }
                if (search != null && (channels == 0 ? search.LocalPalettePercent == 0 : maximum == 0)) { continue; }
                int orderCount = channels >= 3 && entropyEffort >= 2 && allowSqueeze ? entropyEffort >= 7 ? 3 : 2 : 1;
                for (int order = 0; order < orderCount; order++)
                {
                    int paletteChannels = channels, paletteOrder = order;
                    candidates.Add(full =>
                    {
                        List<(byte[] Data, long Bits)> found = [];
                        using JxlModularWorkspace workspace = JxlModularWorkspace.ForEncoding(allocator, Math.Max(262144, (long)input[0].Width * input[0].Height));
                        int channelMaximum = search == null || search.LocalPalettePercent < 0 ? maximum : (int)Math.Min(70911, (long)input[0].Width * input[0].Height * search.LocalPalettePercent / 100);
                        JxlPaletteEncoder palette = paletteChannels == 0 ? JxlPaletteEncoder.TryIndependent(input, channelMaximum, workspace, cancellationToken) :
                            JxlPaletteEncoder.TryCreate(input, paletteChannels, maximum, workspace, cancellationToken, order: (JxlPaletteOrder)paletteOrder, bitDepth: bitDepth);
                        if (palette == null) { return found; }
                        found.Add(Candidate(palette.Planes, palette, null, localRct, full));
                        if (allowSqueeze && entropyEffort >= 8 &&
                            (palette.Planes[palette.MetaChannels].Width > 8 || palette.Planes[palette.MetaChannels].Height > 8))
                        {
                            JxlSqueezeEncoder squeeze = JxlSqueezeEncoder.Create(palette.Planes, entropyEffort == 8 ? 2 : 10, workspace,
                                cancellationToken, palette.MetaChannels);
                            found.Add(Candidate(squeeze.Planes, palette, squeeze, localRct, full));
                        }
                        return found;
                    });
                }
            }
            if (allowSqueeze && entropyEffort >= 5 && input.Length <= 19 && (input[0].Width > 8 || input[0].Height > 8))
            {
                candidates.Add(full =>
                {
                    using JxlModularWorkspace workspace = JxlModularWorkspace.ForEncoding(allocator, (long)input[0].Width * input[0].Height);
                    JxlSqueezeEncoder squeeze = JxlSqueezeEncoder.Create(input, entropyEffort < 8 ? 1 : entropyEffort == 8 ? 2 : 10, workspace, cancellationToken);
                    return [Candidate(squeeze.Planes, null, squeeze, localRct, full)];
                });
            }
        }

        // A full candidate uses the requested effort and the shared cache; a ranking
        // estimate uses the fixed effort-2 tree and entropy coding without the cache.
        (byte[] Data, long Bits) Candidate(JxlModularPlane[] candidate, JxlPaletteEncoder palette, JxlSqueezeEncoder squeeze, int localRct, bool full)
        {
            using MemoryStream stream = new(); JxlBitWriter target = new(stream);
            WriteStreamCore(target, candidate, rct, palette, squeeze, usePrefix, full ? entropyEffort : RankingEffort, allocator, cancellationToken, bitDepth, localRct,
                full ? search : null, full ? cache : null);
            long length = target.Position; target.ZeroPadToByte(); return (stream.ToArray(), length);
        }
    }

    internal static void WriteStreamCore(JxlBitWriter writer, JxlModularPlane[] planes, int rct, JxlPaletteEncoder palette, JxlSqueezeEncoder squeeze,
        bool usePrefix, int entropyEffort, MemoryAllocator allocator, CancellationToken cancellationToken, int bitDepth = 8, int localRct = 0,
        JxlModularSearchOptions search = null, JxlModularEncodingCache cache = null)
    {
        bool cacheable = cache?.CanStore(planes) == true;
        JxlModularEncodingCache.Key key = cacheable ? JxlModularEncodingCache.GetKey(planes, entropyEffort, usePrefix, bitDepth, search, cancellationToken) : default;
        JxlModularEncodingCache.Entry cached = cacheable ? cache.Find(key, planes, cancellationToken) : null;
        JxlWeightedPredictorHeader weighted = cached?.Weighted ?? (search is { NoWeighted: true } ? new() : JxlWeightedPredictorEncoder.Select(planes, entropyEffort, allocator, cancellationToken));
        writer.WriteBool(false); weighted.Write(writer); // Local tree and selected weighted predictor.
        writer.WriteU32((rct != 0 ? 1U : 0) + (localRct != 0 ? 1U : 0) + (uint)(palette?.TransformCount ?? 0) + (squeeze != null ? 1U : 0), 0, 1, U(4, 2), U(8, 18));
        foreach (int colorTransform in new[] { rct, localRct })
        {
            if (colorTransform == 0) { continue; }
            writer.WriteBits(2, 0); // RCT, starting with channel zero.
            writer.WriteU32(0, U(3), U(6, 8), U(10, 72), U(13, 1096));
            writer.WriteU32((uint)colorTransform, 6, U(2), U(4, 2), U(6, 10));
        }
        palette?.WriteHeader(writer);
        squeeze?.WriteHeader(writer);
        if (cached != null) { writer.WriteBitstream(cached.Data, cached.Bits); return; }
        JxlMaEncoder tree = JxlMaEncoder.Learn(planes, entropyEffort, weighted, allocator, cancellationToken, bitDepth, search);
        if (cacheable)
        {
            using MemoryStream stream = new(); JxlBitWriter body = new(stream);
            tree.Write(body, planes, weighted, allocator, usePrefix, entropyEffort, cancellationToken, search?.MaximumHistograms ?? 256);
            long bits = body.Position; body.ZeroPadToByte(); byte[] data = stream.ToArray();
            writer.WriteBitstream(data, bits); cache.Add(key, planes, weighted, data, bits, cancellationToken);
        }
        else { tree.Write(writer, planes, weighted, allocator, usePrefix, entropyEffort, cancellationToken, search?.MaximumHistograms ?? 256); }
    }

}
