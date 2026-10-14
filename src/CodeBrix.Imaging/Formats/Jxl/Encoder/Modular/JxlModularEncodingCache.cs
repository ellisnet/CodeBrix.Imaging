// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Modular;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Modular;

// Per-frame, bounded reuse of exact Modular tree/entropy encodings. The hash
// selects a candidate; geometry and every sample are compared before reuse.
internal sealed class JxlModularEncodingCache(long maximumBytes)
{
    internal readonly record struct Key(ulong Hash, int Effort, bool Prefix, int BitDepth, JxlModularSearchOptions Search);
    internal sealed record Entry(Key Key, JxlModularPlane[] Planes, int[][] Samples,
        JxlWeightedPredictorHeader Weighted, byte[] Data, long Bits, long Bytes);

    // Parallel group encoders share one cache. A hit reproduces exactly the bytes
    // a fresh encode would write, so lookup order never changes the output.
    private readonly object gate = new();
    private readonly Dictionary<Key, LinkedListNode<Entry>> entries = [];
    private readonly LinkedList<Entry> recent = new();
    internal long RetainedBytes { get; private set; }
    internal int Hits { get; private set; }

    internal bool CanStore(JxlModularPlane[] planes)
        => planes.Length != 0 && StorageBytes(planes) <= maximumBytes / 2;

    internal static Key GetKey(JxlModularPlane[] planes, int effort, bool prefix, int bitDepth,
        JxlModularSearchOptions search, CancellationToken cancellationToken)
    {
        ulong hash = 14695981039346656037;
        foreach (JxlModularPlane plane in planes)
        {
            Add(plane.Width); Add(plane.Height); Add(plane.HorizontalShift); Add(plane.VerticalShift);
            for (int y = 0; y < plane.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (int sample in plane.Row(y)) { Add(sample); }
            }
        }
        return new(hash, effort, prefix, bitDepth, search);
        void Add(int value) => hash = unchecked((hash ^ (uint)value) * 1099511628211);
    }

    internal Entry Find(Key key, JxlModularPlane[] planes, CancellationToken cancellationToken)
    {
        lock (this.gate) { return this.FindLocked(key, planes, cancellationToken); }
    }

    private Entry FindLocked(Key key, JxlModularPlane[] planes, CancellationToken cancellationToken)
    {
        if (!this.entries.TryGetValue(key, out LinkedListNode<Entry> node)) { return null; }
        Entry entry = node.Value;
        if (planes.Length != entry.Planes.Length) { return null; }
        for (int c = 0; c < planes.Length; c++)
        {
            JxlModularPlane plane = planes[c], saved = entry.Planes[c];
            if (plane.Width != saved.Width || plane.Height != saved.Height ||
                plane.HorizontalShift != saved.HorizontalShift || plane.VerticalShift != saved.VerticalShift) { return null; }
            for (int y = 0; y < plane.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!plane.Row(y).SequenceEqual(entry.Samples[c].AsSpan(y * plane.Width, plane.Width))) { return null; }
            }
        }
        this.recent.Remove(node); this.recent.AddLast(node); this.Hits++;
        return entry;
    }

    internal void Add(Key key, JxlModularPlane[] planes, JxlWeightedPredictorHeader weighted, byte[] data, long bits,
        CancellationToken cancellationToken)
    {
        lock (this.gate) { this.AddLocked(key, planes, weighted, data, bits, cancellationToken); }
    }

    private void AddLocked(Key key, JxlModularPlane[] planes, JxlWeightedPredictorHeader weighted, byte[] data, long bits,
        CancellationToken cancellationToken)
    {
        long bytes = StorageBytes(planes) + data.LongLength;
        if (bytes > maximumBytes) { return; }
        if (this.entries.TryGetValue(key, out LinkedListNode<Entry> old)) { this.Remove(old); }
        while (this.RetainedBytes > maximumBytes - bytes || this.entries.Count >= 128) { this.Remove(this.recent.First); }
        JxlModularPlane[] geometry = new JxlModularPlane[planes.Length];
        int[][] samples = new int[planes.Length][];
        for (int c = 0; c < planes.Length; c++)
        {
            JxlModularPlane plane = planes[c];
            geometry[c] = new() { Width = plane.Width, Height = plane.Height, HorizontalShift = plane.HorizontalShift, VerticalShift = plane.VerticalShift };
            samples[c] = new int[checked(plane.Width * plane.Height)];
            for (int y = 0; y < plane.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                plane.Row(y).CopyTo(samples[c].AsSpan(y * plane.Width, plane.Width));
            }
        }
        Entry entry = new(key, geometry, samples, weighted, data, bits, bytes);
        this.entries.Add(key, this.recent.AddLast(entry)); this.RetainedBytes += bytes;
    }

    private void Remove(LinkedListNode<Entry> node)
    { this.entries.Remove(node.Value.Key); this.recent.Remove(node); this.RetainedBytes -= node.Value.Bytes; }

    private static long StorageBytes(JxlModularPlane[] planes)
    {
        // Include a conservative allowance for snapshots, lists and dictionary
        // entries. Source planes/buffers are never retained by the cache.
        long bytes = 512 + planes.LongLength * 128;
        foreach (JxlModularPlane plane in planes) { bytes += (long)plane.Width * plane.Height * sizeof(int); }
        return bytes;
    }
}
