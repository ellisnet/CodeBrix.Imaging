// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from libjxl lib/jxl/jpeg/enc_jpeg_data_reader.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Buffers.Binary;
using CodeBrix.Imaging.Formats.Jxl.JpegReconstruction;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Jpeg;

internal sealed partial class JxlJpegParser
{
    private void ReadFrame(ReadOnlySpan<byte> segment, bool progressive)
    {
        if (this.components != null || segment.Length < 6) { JxlThrowHelper.Invalid("invalid or duplicate JPEG frame header."); }
        int count = segment[5];
        if (segment[0] != 8 || count is not (1 or 3)) { throw new NotSupportedException("JPEG XL recompression requires an 8-bit JPEG with one or three components."); }
        this.height = BinaryPrimitives.ReadUInt16BigEndian(segment[1..]); this.width = BinaryPrimitives.ReadUInt16BigEndian(segment[3..]);
        if (this.width == 0 || this.height == 0 || (long)this.width * this.height > this.maxPixels || segment.Length != 6 + (3 * count))
        { JxlThrowHelper.Invalid("invalid JPEG dimensions or frame header length."); }
        this.progressive = progressive; this.components = new Component[count]; Span<bool> used = stackalloc bool[256]; used.Clear();
        for (int c = 0; c < count; c++)
        {
            int p = 6 + (3 * c); byte id = segment[p]; int sampling = segment[p + 1], horizontal = sampling >> 4, vertical = sampling & 15;
            if (used[id] || horizontal == 0 || vertical == 0 || segment[p + 2] > 3) { JxlThrowHelper.Invalid("invalid JPEG component definition."); }
            if (horizontal > 2 || vertical > 2) { throw new NotSupportedException("JPEG XL reconstruction supports JPEG sampling factors of one or two."); }
            used[id] = true; this.maxH = Math.Max(this.maxH, horizontal); this.maxV = Math.Max(this.maxV, vertical);
            this.components[c] = new() { Id = id, Horizontal = horizontal, Vertical = vertical, Quant = segment[p + 2] };
        }
        int columns = (this.width + (this.maxH * 8) - 1) / (this.maxH * 8), rows = (this.height + (this.maxV * 8) - 1) / (this.maxV * 8);
        // Every block of a component that has a DC-first scan costs at least one
        // entropy-coded bit (Huffman codes are never shorter than one bit, and DC
        // refinement stores one raw bit per block). A JPEG with image data has at
        // least one such scan, so the input must hold at least one bit per block
        // of its smallest component. Reject a tiny input describing a large frame
        // before allocating its coefficient planes.
        long fewestBlocks = long.MaxValue, samples = 0;
        foreach (Component component in this.components)
        {
            long blocks = (long)columns * component.Horizontal * rows * component.Vertical;
            fewestBlocks = Math.Min(fewestBlocks, blocks); samples += blocks * 64;
        }
        if (fewestBlocks > this.source.LongLength * 8) { JxlThrowHelper.Invalid("JPEG data is too short for its frame dimensions."); }
        if (count == 1) { samples *= 3; } // Zero chroma planes are added for grayscale.
        this.CoefficientSamples = samples;
        // Reject the coefficient storage at SOF; content-dependent token storage
        // is added by the encoder as groups are tokenized, before any output.
        JxlEncoderPreflight.Require(JxlEncoderPreflight.JpegBytes(samples, this.source.LongLength, 0), this.workspace.Allocator);
        foreach (Component component in this.components)
        { component.Plane = this.workspace.Allocate(columns * component.Horizontal * 8, rows * component.Vertical * 8); }
    }

    private void ReadQuantization(ReadOnlySpan<byte> segment)
    {
        if (segment.IsEmpty) { JxlThrowHelper.Invalid("empty JPEG quantization segment."); }
        while (!segment.IsEmpty)
        {
            int precision = segment[0] >> 4, index = segment[0] & 15; segment = segment[1..];
            if (precision > 1 || index > 3 || segment.Length < 64 * (precision + 1))
            { JxlThrowHelper.Invalid("invalid JPEG quantization table."); }
            if (this.quantTables.Count == 4) { throw new NotSupportedException("JPEG reconstruction supports at most four quantization table definitions."); }
            int[] values = new int[64];
            for (int i = 0; i < 64; i++)
            {
                int value = precision == 0 ? segment[i] : BinaryPrimitives.ReadUInt16BigEndian(segment[(i * 2)..]);
                values[JxlJpegScanWriter.NaturalOrder[i]] = value;
            }
            segment = segment[(64 * (precision + 1))..]; this.quantTables.Add(new(precision, index, segment.IsEmpty)); this.quantValues.Add(values);
        }
    }

    private void ReadHuffman(ReadOnlySpan<byte> segment)
    {
        if (this.huffman.Count >= 89) { throw new NotSupportedException("JPEG reconstruction supports at most 89 Huffman table definitions."); }
        if (segment.IsEmpty)
        { this.huffman.Add(new() { IsLast = true, Counts = new byte[17], Values = [] }); return; }
        Span<bool> used = stackalloc bool[256];
        while (!segment.IsEmpty)
        {
            if (segment.Length < 17) { JxlThrowHelper.Invalid("invalid JPEG Huffman table segment."); }
            if (this.huffman.Count >= 89) { throw new NotSupportedException("JPEG reconstruction supports at most 89 Huffman table definitions."); }
            int selector = segment[0], slot = selector & 15; bool ac = (selector & 16) != 0;
            if ((selector & 0xe0) != 0 || slot > 3) { JxlThrowHelper.Invalid("invalid JPEG Huffman table selector."); }
            byte[] counts = new byte[17]; segment.Slice(1, 16).CopyTo(counts.AsSpan(1)); segment = segment[17..];
            int total = 0, maximumDepth = 1;
            for (int d = 1; d <= 16; d++) { total += counts[d]; if (counts[d] != 0) { maximumDepth = d; } }
            if (total > (ac ? 256 : 12) || total > segment.Length)
            { JxlThrowHelper.Invalid("invalid JPEG Huffman table size."); }
            int unused = 1;
            for (int d = 1; d <= 16; d++)
            {
                unused = (unused << 1) - counts[d];
                if (unused < 0) { JxlThrowHelper.Invalid("oversubscribed JPEG Huffman table."); }
            }
            if (unused == 0) { JxlThrowHelper.Invalid("JPEG Huffman table assigns the forbidden all-ones code."); }
            if (counts[maximumDepth] == 255)
            { throw new NotSupportedException("JPEG Huffman table has no representable reconstruction sentinel."); }
            ushort[] values = new ushort[total + 1];
            for (int i = 0; i < total; i++) { values[i] = segment[i]; }
            values[total] = 256; segment = segment[total..];
            used.Clear();
            for (int i = 0; i < total; i++)
            {
                if (used[values[i]] || (!ac && values[i] > 11)) { JxlThrowHelper.Invalid("duplicate or invalid JPEG Huffman value."); }
                used[values[i]] = true;
            }
            counts[maximumDepth]++; // Last value reserves the invalid all-ones code.
            JxlJpegData.HuffmanCode definition = new() { IsAc = ac, Id = slot, IsLast = segment.IsEmpty, Counts = counts, Values = values };
            (ac ? this.acTables : this.dcTables)[slot] = new(definition); this.huffman.Add(definition);
        }
    }

    private void ReadScan(ReadOnlySpan<byte> segment)
    {
        if (this.scans.Count >= this.maxScans) { JxlThrowHelper.Invalid("JPEG input exceeds MaxScans."); }
        if (this.components == null || segment.Length < 4) { JxlThrowHelper.Invalid("JPEG scan precedes its frame header."); }
        int count = segment[0];
        if (count < 1 || count > this.components.Length || segment.Length != 4 + (2 * count)) { JxlThrowHelper.Invalid("invalid JPEG scan header."); }
        JxlJpegData.Scan scan = new() { Components = new JxlJpegData.ScanComponent[count], ResetPoints = [], ExtraZeroRuns = [],
            Ss = segment[^3], Se = segment[^2], Ah = segment[^1] >> 4, Al = segment[^1] & 15 };
        if (scan.Ss > scan.Se || scan.Se > 63 || scan.Al > 10 || scan.Ah > 13 ||
            (!this.progressive && (scan.Ss != 0 || scan.Se != 63 || scan.Al != 0 || scan.Ah != 0)))
        { JxlThrowHelper.Invalid("invalid JPEG spectral or refinement fields."); }
        Span<bool> used = stackalloc bool[3]; used.Clear();
        for (int c = 0; c < count; c++)
        {
            byte id = segment[1 + (2 * c)]; int selector = segment[2 + (2 * c)], dc = selector >> 4, ac = selector & 15;
            int component = Array.FindIndex(this.components, value => value.Id == id);
            if (component < 0 || used[component] || dc > 3 || ac > 3) { JxlThrowHelper.Invalid("invalid JPEG scan component."); }
            used[component] = true;
            if ((scan.Ss == 0 && scan.Ah == 0 && this.dcTables[dc] == null) || (scan.Se > 0 && this.acTables[ac] == null))
            { JxlThrowHelper.Invalid("JPEG scan uses an undefined Huffman table."); }
            scan.Components[c] = new(component, ac, dc);
            ushort mask = (ushort)(scan.Ah == 0 ? 0xffff << scan.Al : 1 << scan.Al);
            for (int k = scan.Ss; k <= scan.Se; k++)
            {
                if ((this.progression[component, k] & (mask | ((1 << scan.Al) - 1))) != 0)
                { JxlThrowHelper.Invalid("overlapping or out-of-order JPEG coefficient scans."); }
                this.progression[component, k] |= mask;
            }
        }
        this.DecodeScan(scan); this.scans.Add(scan);
    }
}
