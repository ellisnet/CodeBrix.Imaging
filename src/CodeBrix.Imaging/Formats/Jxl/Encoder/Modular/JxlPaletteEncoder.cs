// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Inverse of jxl-oxide crates/jxl-modular/src/transform/palette.rs,
// 0.12.6, f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).
// Implicit-color selection and frequency/luma ordering adapted from libjxl
// lib/jxl/modular/transform/enc_palette.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Copyright (c) the JPEG XL Project Authors. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using static CodeBrix.Imaging.Formats.Jxl.Bitstream.JxlU32;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Modular;

internal enum JxlPaletteOrder { Lexicographic, ImplicitLuma, ImplicitImageOrder }

internal sealed class JxlPaletteEncoder
{
    internal JxlModularPlane[] Planes;
    private int colors, channels, begin;
    private JxlPaletteEncoder previous;
    internal int TransformCount => 1 + (this.previous?.TransformCount ?? 0);
    internal int MetaChannels => this.TransformCount;
    internal int ExplicitColors => this.colors;

    internal static JxlPaletteEncoder TryCreate(JxlModularPlane[] input, int channelCount, int maxColors,
        JxlModularWorkspace workspace, CancellationToken cancellationToken, int begin = 0, JxlPaletteEncoder previous = null,
        JxlPaletteOrder order = JxlPaletteOrder.Lexicographic, int bitDepth = 8)
    {
        if (channelCount is < 1 or > 4 || begin < 0 || begin > input.Length - channelCount) { throw new ArgumentOutOfRangeException(nameof(channelCount)); }
        JxlModularPlane first = input[begin];
        int width = first.Width, height = first.Height;
        for (int c = 1; c < channelCount; c++)
        {
            if (input[begin + c].Width != width || input[begin + c].Height != height || input[begin + c].HorizontalShift != first.HorizontalShift || input[begin + c].VerticalShift != first.VerticalShift)
            { return null; }
        }
        maxColors = (int)Math.Min(maxColors, (long)width * height / 2);
        if (maxColors < 1) { return null; }
        if (!Enum.IsDefined(order)) { throw new ArgumentOutOfRangeException(nameof(order)); }
        bool useImplicit = order != JxlPaletteOrder.Lexicographic && channelCount >= 3 && bitDepth is >= 1 and <= 31;
        Dictionary<(int, int, int, int), int> implicitColors = useImplicit ? ImplicitColors(channelCount, bitDepth) : [];
        Dictionary<(int, int, int, int), int> lookup = [];
        int explicitCount = 0;
        for (int y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++)
            {
                var value = Value(x, y);
                if (lookup.TryGetValue(value, out int frequency)) { lookup[value] = frequency + 1; continue; }
                if (!implicitColors.ContainsKey(value) && ++explicitCount > maxColors) { return null; }
                lookup.Add(value, 1);
            }
        }
        List<(int A, int B, int C, int D)> entries = [];
        foreach (var pair in lookup)
        { if (!implicitColors.ContainsKey(pair.Key) || pair.Value > 10) { entries.Add(pair.Key); } }
        if (entries.Count > 70911) { return null; }
        if (order == JxlPaletteOrder.Lexicographic || channelCount < 3) { entries.Sort(); }
        else if (order == JxlPaletteOrder.ImplicitLuma)
        {
            entries.Sort((a, b) =>
            {
                float ay = (.299f * a.A) + (.587f * a.B) + (.114f * a.C) + .1f;
                float by = (.299f * b.A) + (.587f * b.B) + (.114f * b.C) + .1f;
                if (channelCount == 4) { ay *= 1f + a.D; by *= 1f + b.D; }
                if (lookup[a] > 4) { ay = -ay; } if (lookup[b] > 4) { by = -by; }
                int compared = ay.CompareTo(by); return compared != 0 ? compared : a.CompareTo(b);
            });
        }
        foreach (var pair in implicitColors) { if (lookup.ContainsKey(pair.Key)) { lookup[pair.Key] = entries.Count + pair.Value; } }
        JxlModularPlane palette = workspace.Allocate(entries.Count, channelCount, -1, -1), indices = workspace.Allocate(width, height, first.HorizontalShift, first.VerticalShift);
        for (int i = 0; i < entries.Count; i++)
        {
            var value = entries[i]; lookup[value] = i;
            palette.Set(i, 0, value.A);
            if (channelCount >= 2) { palette.Set(i, 1, value.B); }
            if (channelCount >= 3) { palette.Set(i, 2, value.C); }
            if (channelCount == 4) { palette.Set(i, 3, value.D); }
        }
        for (int y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested(); Span<int> row = indices.Row(y);
            for (int x = 0; x < width; x++) { row[x] = lookup[Value(x, y)]; }
        }
        JxlModularPlane[] planes = new JxlModularPlane[input.Length - channelCount + 2];
        planes[0] = palette; planes[begin + 1] = indices;
        input.AsSpan(0, begin).CopyTo(planes.AsSpan(1));
        input.AsSpan(begin + channelCount).CopyTo(planes.AsSpan(begin + 2));
        return new() { Planes = planes, colors = entries.Count, channels = channelCount, begin = begin, previous = previous };

        (int, int, int, int) Value(int x, int y) =>
            (input[begin].Get(x, y), channelCount >= 2 ? input[begin + 1].Get(x, y) : 0,
                channelCount >= 3 ? input[begin + 2].Get(x, y) : 0, channelCount == 4 ? input[begin + 3].Get(x, y) : 0);
    }

    private static Dictionary<(int, int, int, int), int> ImplicitColors(int channels, int bitDepth)
    {
        Dictionary<(int, int, int, int), int> result = []; long maximum = (1L << bitDepth) - 1;
        Span<int> values = stackalloc int[4];
        for (int i = 0; i < 189; i++)
        {
            values.Clear(); int index = i < 64 ? i : i - 64;
            for (int c = 0; c < Math.Min(channels, 3); c++)
            {
                int digit = i < 64 ? index & 3 : index % 5; index = i < 64 ? index >> 2 : index / 5;
                values[c] = (int)((digit * maximum / 4) + (i < 64 ? 1L << Math.Max(0, bitDepth - 3) : 0));
            }
            result[(values[0], values[1], values[2], values[3])] = i;
        }
        return result;
    }

    internal static JxlPaletteEncoder TryIndependent(JxlModularPlane[] input, int maxColors, JxlModularWorkspace workspace, CancellationToken cancellationToken)
    {
        JxlPaletteEncoder result = null; int metadata = 0;
        for (int c = 0; c < Math.Min(input.Length, 16); c++)
        {
            if (input[c].HorizontalShift < 0 || input[c].VerticalShift < 0) { continue; }
            JxlPaletteEncoder candidate = TryCreate(result?.Planes ?? input, 1, maxColors, workspace, cancellationToken, c + metadata, result);
            if (candidate != null) { result = candidate; metadata++; }
        }
        return result;
    }

    internal void WriteHeader(JxlBitWriter writer)
    {
        this.previous?.WriteHeader(writer);
        writer.WriteBits(2, 1);
        writer.WriteU32((uint)this.begin, U(3), U(6, 8), U(10, 72), U(13, 1096));
        writer.WriteU32((uint)this.channels, 1, 3, 4, U(13, 1));
        writer.WriteU32((uint)this.colors, U(8), U(10, 256), U(12, 1280), U(16, 5376));
        writer.WriteU32(0, 0, U(8, 1), U(10, 257), U(16, 1281)); // No delta entries.
        writer.WriteBits(4, 0);
    }
}
