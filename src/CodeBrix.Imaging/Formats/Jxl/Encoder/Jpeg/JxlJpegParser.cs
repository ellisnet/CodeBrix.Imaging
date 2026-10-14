// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from libjxl lib/jxl/jpeg/enc_jpeg_data_reader.cc and enc_jpeg_data.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.JpegReconstruction;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.Metadata.Profiles.Exif;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Jpeg;

internal sealed partial class JxlJpegParser : IDisposable
{
    private sealed class Component
    {
        internal byte Id;
        internal int Horizontal, Vertical, Quant;
        internal JxlModularPlane Plane;
    }
    private readonly byte[] source;
    private readonly JxlModularWorkspace workspace;
    private readonly long maxPixels;
    private readonly int maxScans;
    private readonly CancellationToken cancellationToken;
    private readonly List<byte> markers = [];
    private readonly List<byte[]> apps = [], comments = [], intermarkers = [];
    private readonly List<JxlJpegData.QuantTable> quantTables = [];
    private readonly List<int[]> quantValues = [];
    private readonly List<JxlJpegData.HuffmanCode> huffman = [];
    private readonly List<JxlJpegData.Scan> scans = [];
    private readonly JxlJpegInputHuffman[] dcTables = new JxlJpegInputHuffman[4], acTables = new JxlJpegInputHuffman[4];
    private readonly ushort[,] progression = new ushort[3, 64];
    private readonly MemoryStream padding = new();
    private readonly JxlBitWriter paddingWriter;
    private Component[] components;
    private int position, width, height, maxH = 1, maxV = 1, restartInterval;
    private bool progressive, foundRestart, allPaddingOnes = true;

    private JxlJpegParser(byte[] source, MemoryAllocator allocator, long maxPixels, CancellationToken cancellationToken, int maxScans)
    {
        this.source = source; this.maxPixels = maxPixels;
        // MaxPixels applies to visible SOF dimensions, checked before allocation.
        // Coefficient planes additionally contain MCU padding; their aggregate
        // storage is governed by the encoder preflight and allocator byte limit.
        this.workspace = JxlModularWorkspace.ForEncoding(allocator, long.MaxValue);
        this.maxScans = maxScans;
        this.cancellationToken = cancellationToken; this.paddingWriter = new(this.padding);
    }

    internal JxlJpegData Data { get; private set; }
    internal JxlJpegCoefficients Coefficients { get; private set; }

    // Total coefficient-plane samples, including MCU padding and grayscale's zero chroma planes.
    internal long CoefficientSamples { get; private set; }
    internal byte[] Icc { get; private set; } = [];
    internal byte[] Exif { get; private set; } = [];
    internal byte[] Xmp { get; private set; } = [];

    internal static JxlJpegParser Read(byte[] source, MemoryAllocator allocator, long maxPixels, CancellationToken cancellationToken, int maxScans = 500)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(allocator);
        if (maxPixels <= 0) { throw new ArgumentOutOfRangeException(nameof(maxPixels)); }
        if (maxScans <= 0) { throw new ArgumentOutOfRangeException(nameof(maxScans)); }
        cancellationToken.ThrowIfCancellationRequested();
        JxlJpegParser parser = new(source, allocator, maxPixels, cancellationToken, maxScans);
        try { parser.Parse(); return parser; }
        catch { parser.Dispose(); throw; }
    }

    private void Parse()
    {
        if (this.source.Length < 4 || this.source[0] != 255 || this.source[1] != 0xd8) { JxlThrowHelper.Invalid("missing JPEG start marker."); }
        this.position = 2;
        while (true)
        {
            this.cancellationToken.ThrowIfCancellationRequested(); int skipped = this.position;
            while (this.position + 1 < this.source.Length && (this.source[this.position] != 255 || !SupportedMarker(this.source[this.position + 1]))) { this.position++; }
            while (skipped < this.position)
            {
                int count = Math.Min(65535, this.position - skipped); this.markers.Add(255);
                this.intermarkers.Add(this.source.AsSpan(skipped, count).ToArray()); skipped += count;
            }
            if (this.position + 1 >= this.source.Length) { JxlThrowHelper.Invalid("missing JPEG end marker or unsupported JPEG coding process."); }
            byte marker = this.source[this.position + 1]; this.position += 2; this.markers.Add(marker);
            if (marker == 0xd9) { break; }
            if (marker is >= 0xd0 and <= 0xd7) { continue; }
            ReadOnlySpan<byte> segment = this.Segment();
            switch (marker)
            {
                case 0xc0: case 0xc1: case 0xc2: this.ReadFrame(segment, marker == 0xc2); break;
                case 0xc4: this.ReadHuffman(segment); break;
                case 0xdb: this.ReadQuantization(segment); break;
                case 0xda: this.ReadScan(segment); break;
                case 0xdd:
                    if (this.foundRestart || segment.Length != 2) { JxlThrowHelper.Invalid("invalid or duplicate JPEG restart interval."); }
                    this.foundRestart = true; this.restartInterval = BinaryPrimitives.ReadUInt16BigEndian(segment); break;
                case 0xfe: this.comments.Add(this.source.AsSpan(this.position - segment.Length - 3, segment.Length + 3).ToArray()); break;
                default:
                    if (marker is < 0xe0 or > 0xef) { throw new NotSupportedException("This JPEG coding process cannot be losslessly recompressed into JPEG XL."); }
                    this.apps.Add(this.source.AsSpan(this.position - segment.Length - 3, segment.Length + 3).ToArray()); break;
            }
        }
        if (this.components == null || this.scans.Count == 0 || this.huffman.Count is < 2 or > 89)
        { JxlThrowHelper.Invalid("incomplete JPEG image."); }
        bool rgb = this.IsRgb();
        if (rgb && Array.Exists(this.components, c => c.Horizontal != 1 || c.Vertical != 1))
        { throw new NotSupportedException("JPEG XL reconstruction cannot retain sampling factors on an RGB JPEG."); }
        JxlImageHeader image = new() { Size = new((uint)this.width, (uint)this.height), XybEncoded = false,
            Color = new() { ColorSpace = this.components.Length == 1 ? 1U : 0 } };
        JxlFrameHeader frame = new() { Width = (uint)this.width, Height = (uint)this.height, YCbCr = !rgb, ExtraUpsampling = [], ExtraBlending = [] };
        JxlModularPlane[] planes = new JxlModularPlane[3]; int[][] quant = new int[3][];
        JxlJpegData.Component[] components = new JxlJpegData.Component[this.components.Length];
        for (int c = 0; c < this.components.Length; c++)
        {
            Component component = this.components[c]; int p = !rgb && c < 2 ? 1 - c : c;
            // Check final values: progressive refinement may legitimately turn
            // an intermediate -2048 into a representable -2047.
            for (int y = 0; y < component.Plane.Height; y += 8)
            {
                this.cancellationToken.ThrowIfCancellationRequested();
                Span<int> row = component.Plane.Row(y);
                for (int x = 0; x < row.Length; x += 8) { CheckedDcCoefficient(row[x]); }
            }
            planes[p] = component.Plane;
            frame.JpegUpsampling[c < 2 ? 1 - c : c] = component.Horizontal == 2 ? component.Vertical == 2 ? 1U : 2U : component.Vertical == 2 ? 3U : 0;
            int table = this.quantTables.FindIndex(q => q.Index == component.Quant);
            if (table < 0) { JxlThrowHelper.Invalid("JPEG component uses an undefined quantization table."); }
            components[c] = new(component.Id, table);
            quant[p] = new int[64]; int[] source = this.quantValues[table];
            if (Array.IndexOf(source, 0) >= 0) { JxlThrowHelper.Invalid("zero entry in a used JPEG quantization table."); }
            for (int i = 0; i < 64; i++) { quant[p][((i & 7) * 8) + (i / 8)] = source[i]; }
        }
        for (int q = 0; q < this.quantTables.Count; q++)
        {
            if (Array.Exists(components, c => c.QuantIndex == q)) { continue; }
            if (q == 0 || !this.quantValues[q].AsSpan().SequenceEqual(this.quantValues[q - 1]))
            { throw new NotSupportedException("JPEG XL reconstruction cannot preserve an unrelated unused JPEG quantization table."); }
        }
        if (components.Length == 1)
        {
            for (int c = 0; c < 3; c++)
            { planes[c] ??= this.workspace.Allocate(planes[1].Width, planes[1].Height); quant[c] ??= quant[1]; }
        }
        this.Coefficients = new() { Image = image, Frame = frame, Planes = planes, Quantization = quant };
        JxlJpegData.AppMarker[] apps = this.ClassifyMetadata();
        image.Color.WantIcc = this.Icc.Length != 0;
        if (this.Exif.Length != 0)
        {
            IExifValue<ushort> orientation = new ExifProfile(this.Exif).GetValue(ExifTag.Orientation);
            if (orientation?.Value is >= 1 and <= 8) { image.Orientation = orientation.Value; }
        }
        using MemoryStream payload = new();
        for (int i = 0; i < apps.Length; i++) { if (apps[i].Type == 0) { payload.Write(this.apps[i]); } }
        foreach (byte[] comment in this.comments) { payload.Write(comment); }
        foreach (byte[] intermarker in this.intermarkers) { payload.Write(intermarker); }
        int tail = this.source.Length - this.position; payload.Write(this.source.AsSpan(this.position));
        int paddingBits = this.allPaddingOnes ? 0 : checked((int)this.paddingWriter.Position); this.paddingWriter.ZeroPadToByte();
        this.Data = new()
        {
            IsGray = components.Length == 1, Markers = this.markers.ToArray(), AppMarkers = apps,
            CommentLengths = this.comments.ConvertAll(value => value.Length).ToArray(),
            IntermarkerLengths = this.intermarkers.ConvertAll(value => value.Length).ToArray(),
            QuantTables = this.quantTables.ToArray(), Components = components, HuffmanCodes = this.huffman.ToArray(), Scans = this.scans.ToArray(),
            RestartInterval = this.restartInterval, TailLength = tail, PaddingBitCount = paddingBits,
            Padding = paddingBits == 0 ? null : this.padding.ToArray(), Data = payload.ToArray(),
        };
        this.Data.ValidateForWrite();
    }

    private ReadOnlySpan<byte> Segment()
    {
        if (this.position > this.source.Length - 2) { JxlThrowHelper.Invalid("truncated JPEG marker length."); }
        int length = BinaryPrimitives.ReadUInt16BigEndian(this.source.AsSpan(this.position));
        if (length < 2 || length > this.source.Length - this.position) { JxlThrowHelper.Invalid("invalid JPEG marker length."); }
        ReadOnlySpan<byte> segment = this.source.AsSpan(this.position + 2, length - 2); this.position += length; return segment;
    }

    private bool IsRgb()
    {
        if (this.components.Length != 3 || this.markers.Contains(0xe0)) { return false; }
        foreach (byte[] marker in this.apps)
        { if (marker.Length == 15 && marker[0] == 0xee && marker.AsSpan(3, 5).SequenceEqual("Adobe"u8)) { return marker[14] == 0; } }
        return this.components[0].Id == 'R' && this.components[1].Id == 'G' && this.components[2].Id == 'B';
    }

    private JxlJpegData.AppMarker[] ClassifyMetadata()
    {
        JxlJpegData.AppMarker[] result = new JxlJpegData.AppMarker[this.apps.Count];
        List<int> icc = []; int expectedCount = 0; bool canonicalIcc = true;
        for (int i = 0; i < this.apps.Count; i++)
        {
            byte[] app = this.apps[i]; int type = 0;
            if (app[0] == 0xe1 && this.Exif.Length == 0 && app.Length > 9 && app.AsSpan(3, 6).SequenceEqual("Exif\0\0"u8))
            { type = 2; this.Exif = app.AsSpan(9).ToArray(); }
            else if (app[0] == 0xe1 && this.Xmp.Length == 0 && app.Length > 32 && app.AsSpan(3, 29).SequenceEqual("http://ns.adobe.com/xap/1.0/\0"u8))
            { type = 3; this.Xmp = app.AsSpan(32).ToArray(); }
            if (app[0] == 0xe2 && app.Length >= 17 && app.AsSpan(3, 12).SequenceEqual("ICC_PROFILE\0"u8))
            {
                icc.Add(i); if (expectedCount == 0) { expectedCount = app[16]; }
                canonicalIcc &= app[15] == icc.Count && app[16] == expectedCount;
            }
            result[i] = new(type, app.Length);
        }
        if (expectedCount != 0 && icc.Count == expectedCount)
        {
            int[] ordered = new int[expectedCount]; Array.Fill(ordered, -1); bool complete = true;
            foreach (int i in icc)
            {
                byte[] app = this.apps[i]; int index = app[15] - 1;
                if ((uint)index >= expectedCount || app[16] != expectedCount || ordered[index] != -1) { complete = false; break; }
                ordered[index] = i;
            }
            if (complete)
            {
                using MemoryStream profile = new();
                foreach (int i in ordered) { profile.Write(this.apps[i].AsSpan(17)); if (canonicalIcc) { result[i] = result[i] with { Type = 1 }; } }
                this.Icc = profile.ToArray();
            }
        }
        return result;
    }

    private static bool SupportedMarker(byte marker) => marker is >= 0xc0 and <= 0xcf or >= 0xd0 and <= 0xd7 or 0xd9 or 0xda or 0xdb or 0xdd or >= 0xe0 and <= 0xef or 0xfe;

    public void Dispose() { this.workspace.Dispose(); this.padding.Dispose(); }
}
