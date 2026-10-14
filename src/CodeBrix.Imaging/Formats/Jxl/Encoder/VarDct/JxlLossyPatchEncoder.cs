// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Text-like background seeds, connected components, perceptual patch keys and
// additive reference organization adapt libjxl lib/jxl/enc_patch_dictionary.cc,
// with atlas precision from enc_modular.cc and enc_quant_weights.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// Bounded queues, hash search, shelf packing and atlas reconstruction are
// original managed adaptations. See THIRD-PARTY-NOTICES.txt.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Modular;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;

internal sealed partial class JxlLossyPatchEncoder
{
    private const int MaximumCandidates = 4096, MaximumSamples = 1 << 20;
    private readonly List<Patch> patches;
    private readonly int width, height;

    private sealed class Patch(int width, int height, sbyte[] key, float[] samples, Point origin)
    {
        internal readonly int Width = width, Height = height;
        internal readonly sbyte[] Key = key;
        internal readonly float[] Samples = samples;
        internal readonly List<Point> Targets = [origin];
        internal int Left, Top;
    }

    private JxlLossyPatchEncoder(List<Patch> patches)
    {
        this.patches = patches; int area = 0, largest = 0;
        foreach (Patch patch in patches) { area += patch.Width * patch.Height; largest = Math.Max(largest, patch.Width); }
        this.width = Math.Max(largest, (int)Math.Ceiling(Math.Sqrt(area)));
        int x = 0, y = 0, rowHeight = 0;
        foreach (Patch patch in patches)
        {
            if (x + patch.Width > this.width) { y += rowHeight; x = rowHeight = 0; }
            patch.Left = x; patch.Top = y; x += patch.Width; rowHeight = Math.Max(rowHeight, patch.Height);
        }
        this.height = y + rowHeight;
    }

    internal static JxlLossyPatchEncoder Find(JxlFloatFrame source, CancellationToken cancellationToken)
    {
        int width = source.Planes[0].Width, height = source.Planes[0].Height;
        // This is a search-work bound, not a format or encoding limit.
        if (width < 16 || height < 16 || (long)width * height > 16_777_216) { return null; }
        int pixels = width * height, columns = width / 4, rows = height / 4, tail = 0;
        using IMemoryOwner<int> queueOwner = source.Allocator.Allocate<int>(pixels);
        using Buffer2D<int> background = source.Allocator.Allocate2D<int>(width, height, AllocationOptions.Clean);
        Span<int> queue = queueOwner.Memory.Span[..pixels];
        // Classify background cells concurrently (pure reads), then seed the
        // flood fill sequentially in the original scan order.
        using Buffer2D<byte> backgroundCells = source.Allocator.Allocate2D<byte>(Math.Max(1, columns), Math.Max(1, rows), AllocationOptions.Clean);
        JxlParallel.ForEncoderRows(Math.Max(0, rows - 2), 8, cancellationToken, (rowStart, rowEnd) =>
        {
            for (int py = rowStart + 1; py < rowEnd + 1; py++)
            {
                Span<byte> cells = backgroundCells.DangerousGetRowSpan(py);
                for (int px = 1; px < columns - 1; px++)
                {
                    int x = px * 4, y = py * 4, origin = y * width + x; bool flat = true;
                    for (int dy = 0; dy < 4 && flat; dy++)
                    { for (int dx = 0; dx < 4 && flat; dx++) { flat = Same(source, origin, (y + dy) * width + x + dx, width); } }
                    if (!flat) { continue; }
                    int same = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    { for (int dx = -1; dx <= 1; dx++) { if (Same(source, origin, (y + 4 * dy) * width + x + 4 * dx, width)) { same++; } } }
                    if (same >= 8) { cells[px] = 1; }
                }
            }
        });
        for (int py = 1; py < rows - 1; py++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Span<byte> cells = backgroundCells.DangerousGetRowSpan(py);
            for (int px = 1; px < columns - 1; px++)
            {
                if (cells[px] == 0) { continue; }
                int x = px * 4, y = py * 4;
                for (int dy = 0; dy < 4; dy++)
                {
                    Span<int> row = background.DangerousGetRowSpan(y + dy);
                    for (int dx = 0; dx < 4; dx++)
                    { int position = (y + dy) * width + x + dx; row[x + dx] = position + 1; queue[tail++] = position; }
                }
            }
        }
        if (tail == 0) { return null; }
        for (int head = 0; head < tail; head++)
        {
            if ((head & 1023) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
            int current = queue[head], cx = current % width, cy = current / width;
            int seed = background.DangerousGetRowSpan(cy)[cx] - 1;
            for (int y = Math.Max(0, cy - 1); y <= Math.Min(height - 1, cy + 1); y++)
            {
                Span<int> row = background.DangerousGetRowSpan(y);
                for (int x = Math.Max(0, cx - 1); x <= Math.Min(width - 1, cx + 1); x++)
                {
                    if (row[x] != 0 || Math.Abs(x - seed % width) + Math.Abs(y - seed / width) > 50) { continue; }
                    int next = y * width + x;
                    if (Similar(source, seed, next, width, .8f)) { row[x] = seed + 1; queue[tail++] = next; }
                }
            }
        }
        using Buffer2D<byte> visited = source.Allocator.Allocate2D<byte>(width, height, AllocationOptions.Clean);
        List<Patch> unique = []; Dictionary<ulong, List<Patch>> buckets = []; int retainedSamples = 0;
        for (int y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++)
            {
                if (background.DangerousGetRowSpan(y)[x] != 0 || visited.DangerousGetRowSpan(y)[x] != 0) { continue; }
                int left = x, top = y, right = x, bottom = y, border = -1;
                bool similar = true; tail = 1; queue[0] = y * width + x; visited.DangerousGetRowSpan(y)[x] = 1;
                for (int head = 0; head < tail; head++)
                {
                    if ((head & 1023) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
                    int current = queue[head], cx = current % width, cy = current / width;
                    left = Math.Min(left, cx); right = Math.Max(right, cx); top = Math.Min(top, cy); bottom = Math.Max(bottom, cy);
                    for (int ny = Math.Max(0, cy - 1); ny <= Math.Min(height - 1, cy + 1); ny++)
                    {
                        Span<int> bg = background.DangerousGetRowSpan(ny); Span<byte> seen = visited.DangerousGetRowSpan(ny);
                        for (int nx = Math.Max(0, cx - 1); nx <= Math.Min(width - 1, cx + 1); nx++)
                        {
                            if (bg[nx] != 0)
                            {
                                int seed = bg[nx] - 1;
                                if (border < 0) { border = seed; }
                                else { similar &= Similar(source, border, seed, width, .03f); }
                            }
                            else if (seen[nx] == 0) { seen[nx] = 1; queue[tail++] = ny * width + nx; }
                        }
                    }
                }
                if (!similar || border < 0 || right - left >= 32 || bottom - top >= 32) { continue; }
                bool nearby = false;
                for (int ny = Math.Max(0, top - 2); ny <= Math.Min(height - 1, bottom + 2) && !nearby; ny++)
                { for (int nx = Math.Max(0, left - 2); nx <= Math.Min(width - 1, right + 2) && !nearby; nx++) { nearby = Similar(source, border, ny * width + nx, width, .03f); } }
                if (!nearby) { continue; }
                int w = right - left + 1, h = bottom - top + 1, count = w * h;
                sbyte[] key = new sbyte[count * 3]; float[] values = new float[key.Length];
                bool tooLarge = false, substantial = false; ulong hash = (ulong)(w * 33 + h);
                for (int c = 0; c < 3; c++)
                {
                    float basis = source.Planes[c].DangerousGetRowSpan(border / width)[border % width];
                    float scale = c == 0 ? .01615f : c == 1 ? .08875f : .1922f;
                    for (int dy = 0; dy < h; dy++)
                    {
                        Span<float> row = source.Planes[c].DangerousGetRowSpan(top + dy);
                        for (int dx = 0; dx < w; dx++)
                        {
                            int index = c * count + dy * w + dx; float value = row[left + dx] - basis;
                            if (!float.IsFinite(value)) { tooLarge = true; continue; }
                            int quant = (int)Math.Clamp(value / scale, -32768, 32767);
                            tooLarge |= quant is < -128 or > 127; substantial |= Math.Abs(quant) >= 2;
                            key[index] = (sbyte)quant; values[index] = value; hash = unchecked((hash ^ (byte)quant) * 1099511628211);
                        }
                    }
                }
                if (tooLarge || !substantial) { continue; }
                if (!buckets.TryGetValue(hash, out List<Patch> bucket)) { bucket = []; }
                Patch match = null;
                foreach (Patch candidate in bucket)
                { if (candidate.Width == w && candidate.Height == h && candidate.Key.AsSpan().SequenceEqual(key)) { match = candidate; break; } }
                if (match != null) { match.Targets.Add(new(left, top)); }
                else if (unique.Count < MaximumCandidates && retainedSamples + count <= MaximumSamples && bucket.Count < 8)
                {
                    Patch patch = new(w, h, key, values, new(left, top)); bucket.Add(patch); unique.Add(patch);
                    buckets[hash] = bucket; retainedSamples += count;
                }
            }
        }
        unique.RemoveAll(patch => patch.Targets.Count < 2);
        long savings = 0; int largestArea = 0;
        foreach (Patch patch in unique)
        { int area = patch.Width * patch.Height; largestArea = Math.Max(largestArea, area); savings += (long)area * (patch.Targets.Count - 1); }
        if (largestArea < 20 || savings < 1024) { return null; }
        unique.Sort((a, b) => (b.Width * b.Height).CompareTo(a.Width * a.Height));
        return new(unique);
    }

    internal JxlFrameFeatures WriteReference(JxlBitWriter writer, JxlImageHeader image, JxlFrameHeader frame, float distance, int effort,
        MemoryAllocator allocator, CancellationToken cancellationToken)
    {
        // Match the non-progressive Modular XYB precision used for native
        // patch references. Round the dequantizers through their wire format
        // before quantizing samples or subtracting the reference.
        float xScale = (float)(Half)((1 + 23 * distance) / 512) / 128;
        float yScale = (float)(Half)((1 + 14 * distance) / 32) / 128;
        using JxlModularWorkspace workspace = JxlModularWorkspace.ForEncoding(allocator, Math.Max(262144, (long)this.width * this.height));
        JxlModularPlane[] planes = new JxlModularPlane[3 + image.ExtraChannels.Length];
        for (int c = 0; c < 3; c++) { planes[c] = workspace.Allocate(this.width, this.height); }
        JxlPatch[] dictionary = new JxlPatch[this.patches.Count];
        for (int i = 0; i < dictionary.Length; i++)
        {
            Patch patch = this.patches[i]; int count = patch.Width * patch.Height;
            for (int y = 0; y < patch.Height; y++)
            {
                for (int x = 0; x < patch.Width; x++)
                {
                    int index = y * patch.Width + x, xx = (int)MathF.Round(patch.Samples[index] / xScale),
                        yy = (int)MathF.Round(patch.Samples[count + index] / yScale), bb = (int)MathF.Round(patch.Samples[2 * count + index] / yScale) - yy;
                    planes[0].Set(patch.Left + x, patch.Top + y, yy); planes[1].Set(patch.Left + x, patch.Top + y, xx); planes[2].Set(patch.Left + x, patch.Top + y, bb);
                    patch.Samples[index] = xx * xScale; patch.Samples[count + index] = yy * yScale;
                    patch.Samples[2 * count + index] = (bb + yy) * yScale;
                }
            }
            JxlPatchTarget[] targets = new JxlPatchTarget[patch.Targets.Count];
            for (int t = 0; t < targets.Length; t++) { targets[t] = new() { Position = patch.Targets[t], Blending = new(JxlPatchBlendMode.Add) }; }
            dictionary[i] = new() { Reference = 3, Source = new(patch.Left, patch.Top, patch.Width, patch.Height), Targets = targets };
        }
        for (int c = 0; c < image.ExtraChannels.Length; c++)
        {
            int shift = (int)image.ExtraChannels[c].DimShift;
            planes[3 + c] = workspace.Allocate((this.width + (1 << shift) - 1) >> shift, (this.height + (1 << shift) - 1) >> shift, shift, shift);
        }
        JxlFrameHeader reference = new()
        {
            FrameType = 2, Modular = true, IsLast = false, Width = (uint)this.width, Height = (uint)this.height, HaveCrop = true,
            SaveAsReference = 3, SaveBeforeColorTransform = true, ExtraUpsampling = new uint[image.ExtraChannels.Length],
            ExtraBlending = new JxlBlendingInfo[image.ExtraChannels.Length], Restoration = new() { GaborEnabled = false, EpfIterations = 0 },
        };
        Array.Fill(reference.ExtraUpsampling, 1U);
        JxlLossyModularEncoder.WriteChannelsFrame(writer, image, reference, planes, null, [xScale, yScale, yScale], effort,
            new(PaletteColors: 0, LocalPalettePercent: 0, GlobalPalettePercent: 0, Sampling: -1, Predictor: 5, NoWeighted: true), 0, allocator, cancellationToken);
        return JxlFrameFeatures.Create(new() { Patches = dictionary }, image, frame, effort, cancellationToken);
    }

    internal void Apply(JxlFloatFrame image, bool subtract, CancellationToken cancellationToken)
    {
        foreach (Patch patch in this.patches)
        {
            int count = patch.Width * patch.Height;
            foreach (Point target in patch.Targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int c = 0; c < 3; c++)
                {
                    for (int y = 0; y < patch.Height; y++)
                    {
                        Span<float> row = image.Planes[c].DangerousGetRowSpan(target.Y + y).Slice(target.X, patch.Width);
                        for (int x = 0; x < row.Length; x++)
                        { float value = patch.Samples[c * count + y * patch.Width + x]; row[x] += subtract ? -value : value; }
                    }
                }
            }
        }
    }

    private static bool Same(JxlFloatFrame image, int a, int b, int width)
    {
        for (int c = 0; c < 3; c++)
        { if (Math.Abs(image.Planes[c].DangerousGetRowSpan(a / width)[a % width] - image.Planes[c].DangerousGetRowSpan(b / width)[b % width]) > 1e-4f) { return false; } }
        return true;
    }

    private static bool Similar(JxlFloatFrame image, int a, int b, int width, float threshold)
    {
        float distance = 0;
        for (int c = 0; c < 3; c++)
        { distance += Math.Abs(image.Planes[c].DangerousGetRowSpan(a / width)[a % width] - image.Planes[c].DangerousGetRowSpan(b / width)[b % width]) * (c == 0 ? 30 : c == 1 ? 3 : 1); }
        return distance <= threshold;
    }
}
