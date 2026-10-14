// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Reference-frame atlas organization follows libjxl lib/jxl/enc_patch_dictionary.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Exact tile hashing, collision verification and complete-stream selection are
// specific to CodeBrix.Imaging; perceptual/text-component search is separate.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Modular;

internal static class JxlLosslessPatchEncoder
{
    private const int TileSize = 8;
    private sealed class Tile(Point origin)
    {
        internal readonly Point Origin = origin;
        internal readonly List<Point> Targets = [origin];
    }

    internal static bool TryWrite(JxlBitWriter output, JxlImageHeader image, JxlFrameHeader frame, JxlModularPlane[] source,
        int effort, MemoryAllocator allocator, CancellationToken cancellationToken, int decodingSpeed = 0)
    {
        if (effort < 5 || image.BitDepth.ExponentBits != 0 || image.BitDepth.Bits > 16 || frame.Flags != 0 ||
            !frame.IsNormal || frame.HaveCrop || frame.Width < TileSize || frame.Height < TileSize || (ulong)frame.Width * frame.Height < 1024)
        { return false; }
        List<Tile> tiles = Find(source, cancellationToken);
        if (tiles.Count == 0) { return false; }
        int columns = Math.Min((int)frame.Width / TileSize, (int)Math.Ceiling(Math.Sqrt(tiles.Count)));
        int rows = (tiles.Count + columns - 1) / columns;
        if (rows * TileSize > frame.Height) { columns = (int)frame.Width / TileSize; rows = (tiles.Count + columns - 1) / columns; }
        using JxlModularWorkspace workspace = JxlModularWorkspace.ForEncoding(allocator, (long)frame.Width * frame.Height);
        JxlModularPlane[] atlas = new JxlModularPlane[source.Length], residual = new JxlModularPlane[source.Length];
        for (int c = 0; c < source.Length; c++)
        {
            atlas[c] = workspace.Allocate(columns * TileSize, rows * TileSize);
            residual[c] = workspace.Allocate(source[c].Width, source[c].Height);
            for (int y = 0; y < source[c].Height; y++) { cancellationToken.ThrowIfCancellationRequested(); source[c].Row(y).CopyTo(residual[c].Row(y)); }
        }
        JxlPatches.Patch[] entries = new JxlPatches.Patch[tiles.Count];
        JxlBlendingInfo[] blending = new JxlBlendingInfo[image.ExtraChannels.Length + 1]; Array.Fill(blending, new(1, 0, false, 3));
        for (int i = 0; i < tiles.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested(); Tile tile = tiles[i];
            int left = (i % columns) * TileSize, top = (i / columns) * TileSize;
            for (int c = 0; c < source.Length; c++)
            {
                for (int y = 0; y < TileSize; y++) { source[c].Row(tile.Origin.Y + y).Slice(tile.Origin.X, TileSize).CopyTo(atlas[c].Row(top + y).Slice(left, TileSize)); }
            }
            JxlPatches.Target[] targets = new JxlPatches.Target[tile.Targets.Count];
            for (int t = 0; t < targets.Length; t++)
            {
                Point point = tile.Targets[t]; targets[t] = new(point.X, point.Y, blending);
                for (int c = 0; c < source.Length; c++)
                { for (int y = 0; y < TileSize; y++) { residual[c].Row(point.Y + y).Slice(point.X, TileSize).Clear(); } }
            }
            entries[i] = new(3, (uint)left, (uint)top, TileSize, TileSize, targets);
        }
        JxlFrameHeader reference = new()
        {
            Modular = true, FrameType = 2, Width = (uint)(columns * TileSize), Height = (uint)(rows * TileSize), HaveCrop = true,
            IsLast = false, SaveAsReference = 3, SaveBeforeColorTransform = true, ExtraUpsampling = frame.ExtraUpsampling,
            ExtraBlending = new JxlBlendingInfo[image.ExtraChannels.Length], Restoration = new() { GaborEnabled = false, EpfIterations = 0 },
        };
        // The patched (atlas + residual copies) and original (source) encodings
        // share no planes or headers, so they run concurrently.
        using MemoryStream alternative = new(), original = new();
        JxlFrameHeader patchedFrame = frame.Clone(), originalFrame = frame.Clone();
        JxlParallel.ForEncoder(2, cancellationToken, candidate =>
        {
            if (candidate == 0)
            {
                JxlBitWriter patchedWriter = new(alternative);
                JxlModularEncoder.WriteFrame(patchedWriter, image, reference, atlas, cancellationToken, false, effort, allocator, decodingSpeed: decodingSpeed);
                JxlModularEncoder.WriteFrame(patchedWriter, image, patchedFrame, residual, cancellationToken, false, effort, allocator, new(entries), decodingSpeed);
            }
            else
            {
                JxlBitWriter originalWriter = new(original);
                JxlModularEncoder.WriteFrame(originalWriter, image, originalFrame, source, cancellationToken, false, effort, allocator, decodingSpeed: decodingSpeed);
            }
        });
        bool patchesWin = alternative.Length < original.Length;
        MemoryStream selected = patchesWin ? alternative : original;
        frame.Flags = patchesWin ? patchedFrame.Flags : originalFrame.Flags;
        frame.GroupSizeShift = patchesWin ? patchedFrame.GroupSizeShift : originalFrame.GroupSizeShift;
        cancellationToken.ThrowIfCancellationRequested(); output.ZeroPadToByte();
        output.WriteBytes(selected.GetBuffer().AsSpan(0, checked((int)selected.Length)));
        return true;
    }

    private static List<Tile> Find(JxlModularPlane[] planes, CancellationToken cancellationToken)
    {
        Dictionary<ulong, List<Tile>> buckets = []; List<Tile> unique = [];
        for (int y = 0; y <= planes[0].Height - TileSize; y += TileSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = 0; x <= planes[0].Width - TileSize; x += TileSize)
            {
                ulong hash = 14695981039346656037; bool varying = false;
                foreach (JxlModularPlane plane in planes)
                {
                    int first = plane.Get(x, y);
                    for (int dy = 0; dy < TileSize; dy++)
                    {
                        ReadOnlySpan<int> row = plane.Row(y + dy).Slice(x, TileSize);
                        foreach (int value in row) { varying |= value != first; hash = unchecked((hash ^ (uint)value) * 1099511628211); }
                    }
                }
                // Flat regions already compress well with prediction and RLE.
                if (!varying) { continue; }
                if (!buckets.TryGetValue(hash, out List<Tile> bucket))
                {
                    if (unique.Count == 65536) { continue; }
                    bucket = []; buckets.Add(hash, bucket);
                }
                Tile matching = null;
                foreach (Tile candidate in bucket)
                {
                    if (Equal(candidate.Origin, new(x, y))) { matching = candidate; break; }
                }
                if (matching != null) { matching.Targets.Add(new(x, y)); }
                else
                {
                    // Bound the search table independently of image dimensions.
                    if (unique.Count == 65536) { continue; }
                    Tile tile = new(new(x, y)); bucket.Add(tile); unique.Add(tile);
                }
            }
        }
        unique.RemoveAll(tile => tile.Targets.Count < 3);
        return unique;

        bool Equal(Point a, Point b)
        {
            foreach (JxlModularPlane plane in planes)
            {
                for (int y = 0; y < TileSize; y++)
                { if (!plane.Row(a.Y + y).Slice(a.X, TileSize).SequenceEqual(plane.Row(b.Y + y).Slice(b.X, TileSize))) { return false; } }
            }
            return true;
        }
    }
}
