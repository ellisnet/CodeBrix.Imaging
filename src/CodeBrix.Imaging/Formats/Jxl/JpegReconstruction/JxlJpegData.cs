// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// JPEG reconstruction fields adapted from jxl-oxide crates/jxl-jbr/src/lib.rs
// and huffman.rs, 0.12.6, f8ae722ef2d6b782941c89517d19cfbf605c4a9d
// (Apache-2.0 option). Bounded BCL Brotli decoding is specific to CodeBrix.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using static CodeBrix.Imaging.Formats.Jxl.Bitstream.JxlU32;

namespace CodeBrix.Imaging.Formats.Jxl.JpegReconstruction;

internal sealed partial class JxlJpegData
{
    internal bool IsGray;
    internal byte[] Markers;
    internal AppMarker[] AppMarkers;
    internal int[] CommentLengths, IntermarkerLengths;
    internal QuantTable[] QuantTables;
    internal Component[] Components;
    internal HuffmanCode[] HuffmanCodes;
    internal Scan[] Scans;
    internal int RestartInterval, TailLength, PaddingBitCount;
    internal byte[] Padding, Data;

    internal readonly record struct AppMarker(int Type, int Length);
    internal readonly record struct QuantTable(int Precision, int Index, bool IsLast);
    internal readonly record struct Component(byte Id, int QuantIndex);
    internal readonly record struct ScanComponent(int Index, int AcTable, int DcTable);
    internal sealed class Scan
    {
        internal int Ss, Se, Al, Ah, LastNeededPass;
        internal ScanComponent[] Components;
        internal HashSet<uint> ResetPoints;
        internal Dictionary<uint, uint> ExtraZeroRuns;
    }
    internal sealed class HuffmanCode
    {
        internal bool IsAc, IsLast;
        internal int Id;
        internal byte[] Counts;
        internal ushort[] Values;
    }

    internal int AppLength => Sum(this.AppMarkers, a => a.Type == 0 ? a.Length : 0);
    internal int CommentLength => Sum(this.CommentLengths, n => n);
    internal int IntermarkerLength => Sum(this.IntermarkerLengths, n => n);
    internal int IccLength => Sum(this.AppMarkers, a => a.Type == 1 ? a.Length - 17 : 0);
    internal int ExifLength => FirstMetadataLength(this.AppMarkers, 2, 9);
    internal int XmpLength => FirstMetadataLength(this.AppMarkers, 3, 32);

    internal static JxlJpegData Read(byte[] encoded, int maxBytes, CancellationToken cancellationToken, int maxScans = 500)
    {
        using MemoryStream input = new(encoded, false);
        JxlBitReader reader = new(input, cancellationToken);
        long budget = maxBytes;
        void Reserve(long size)
        {
            if (size < 0 || size > budget) { JxlThrowHelper.Invalid("JPEG reconstruction tables exceed the metadata budget."); }
            budget -= size;
        }
        JxlJpegData result = new() { IsGray = reader.ReadBool() };
        List<byte> markers = [];
        int apps = 0, comments = 0, scans = 0, intermarkers = 0;
        bool dri = false;
        do
        {
            if (markers.Count >= maxBytes / 32) { JxlThrowHelper.Invalid("JPEG marker table exceeds the metadata budget."); }
            byte marker = (byte)(reader.ReadBits(6) + 0xc0);
            Reserve(64);
            if (marker is >= 0xe0 and <= 0xef) { apps++; }
            else if (marker == 0xfe) { comments++; }
            else if (marker == 0xda)
            {
                if (scans >= maxScans) { JxlThrowHelper.Invalid("JPEG reconstruction exceeds MaxScans."); }
                scans++;
            }
            else if (marker == 0xff) { intermarkers++; }
            else if (marker == 0xdd) { dri = true; }
            markers.Add(marker);
        } while (markers[^1] != 0xd9);
        result.Markers = markers.ToArray();
        result.AppMarkers = new AppMarker[apps];
        for (int i = 0; i < apps; i++)
        {
            int type = (int)reader.ReadU32(0, 1, U(1, 2), U(2, 4));
            int length = (int)reader.ReadBits(16) + 1;
            if (type > 3 || length < (type == 1 ? 17 : type == 2 ? 9 : type == 3 ? 32 : 3))
            { JxlThrowHelper.Invalid("invalid reconstructed JPEG APP marker."); }
            result.AppMarkers[i] = new(type, length);
        }
        result.CommentLengths = ReadLengths(reader, comments, 1);
        result.QuantTables = new QuantTable[reader.ReadBits(2) + 1];
        for (int i = 0; i < result.QuantTables.Length; i++)
        { result.QuantTables[i] = new((int)reader.ReadBits(1), (int)reader.ReadBits(2), reader.ReadBool()); }
        uint componentType = reader.ReadBits(2);
        int componentCount = componentType == 0 ? 1 : componentType == 3 ? (int)reader.ReadBits(2) + 1 : 3;
        byte[] ids = new byte[componentCount];
        for (int i = 0; i < componentCount; i++)
        { ids[i] = componentType == 3 ? (byte)reader.ReadBits(8) : componentType == 2 ? "RGB"u8[i] : (byte)(i + 1); }
        result.Components = new Component[componentCount];
        for (int i = 0; i < componentCount; i++) { result.Components[i] = new(ids[i], (int)reader.ReadBits(2)); }
        result.HuffmanCodes = new HuffmanCode[reader.ReadU32(4, U(3, 2), U(4, 10), U(6, 26))];
        for (int i = 0; i < result.HuffmanCodes.Length; i++)
        {
            HuffmanCode code = new() { IsAc = reader.ReadBool(), Id = (int)reader.ReadBits(2), IsLast = reader.ReadBool(), Counts = new byte[17] };
            int count = 0;
            for (int depth = 0; depth < 17; depth++)
            {
                code.Counts[depth] = (byte)reader.ReadU32(0, 1, U(3, 2), U(8));
                count += code.Counts[depth];
            }
            // The last value is a sentinel reserving the all-ones JPEG code.
            if (count > 257 || code.Counts[0] != 0 || (count == 0 && !code.IsLast)) { JxlThrowHelper.Invalid("invalid reconstructed JPEG Huffman counts."); }
            code.Values = new ushort[count];
            Reserve(count + 128);
            for (int value = 0; value < count; value++) { code.Values[value] = (ushort)reader.ReadU32(U(2), U(2, 4), U(4, 8), U(8, 1)); }
            if (count != 0 && code.Values[^1] != 256) { JxlThrowHelper.Invalid("missing reconstructed JPEG Huffman sentinel."); }
            result.HuffmanCodes[i] = code;
        }
        result.Scans = new Scan[scans];
        for (int i = 0; i < scans; i++)
        {
            Scan scan = new() { Components = new ScanComponent[reader.ReadBits(2) + 1] };
            scan.Ss = (int)reader.ReadBits(6); scan.Se = (int)reader.ReadBits(6);
            scan.Al = (int)reader.ReadBits(4); scan.Ah = (int)reader.ReadBits(4);
            for (int c = 0; c < scan.Components.Length; c++)
            { scan.Components[c] = new((int)reader.ReadBits(2), (int)reader.ReadBits(2), (int)reader.ReadBits(2)); }
            scan.LastNeededPass = (int)reader.ReadU32(0, 1, 2, U(3, 3));
            result.Scans[i] = scan;
        }
        result.RestartInterval = dri ? (int)reader.ReadBits(16) : 0;
        foreach (Scan scan in result.Scans)
        {
            scan.ResetPoints = [];
            uint count = ReadPointCount(reader), previous = 0;
            Reserve(count * 48L);
            for (uint i = 0; i < count; i++)
            {
                uint index = NextBlock(reader, i, previous);
                scan.ResetPoints.Add(index); previous = index;
            }
            scan.ExtraZeroRuns = [];
            count = ReadPointCount(reader); previous = 0;
            Reserve(count * 48L);
            for (uint i = 0; i < count; i++)
            {
                uint runs = reader.ReadU32(1, U(2, 2), U(4, 5), U(8, 20));
                uint index = NextBlock(reader, i, previous);
                scan.ExtraZeroRuns.Add(index, runs); previous = index;
            }
        }
        result.IntermarkerLengths = ReadLengths(reader, intermarkers, 0);
        result.TailLength = (int)reader.ReadU32(0, U(8, 1), U(16, 257), U(22, 65793));
        if (reader.ReadBool())
        {
            result.PaddingBitCount = (int)reader.ReadBits(24);
            Reserve((result.PaddingBitCount + 7) / 8);
            result.Padding = new byte[(result.PaddingBitCount + 7) / 8];
            if (result.Padding.Length > maxBytes) { JxlThrowHelper.Invalid("JPEG padding exceeds the metadata limit."); }
            for (int i = 0; i < result.Padding.Length; i++)
            { result.Padding[i] = (byte)reader.ReadBits(Math.Min(8, result.PaddingBitCount - (i * 8))); }
        }
        reader.ZeroPadToByte();
        long expected = (long)result.AppLength + result.CommentLength + result.IntermarkerLength + result.TailLength;
        if (expected > maxBytes) { JxlThrowHelper.Invalid("JPEG reconstruction data exceeds the metadata limit."); }
        Reserve(expected);
        int offset = checked((int)(reader.Position / 8));
        using BrotliDecoder decoder = new();
        using MemoryStream expanded = new();
        Span<byte> scratch = stackalloc byte[8192];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OperationStatus status = decoder.Decompress(encoded.AsSpan(offset), scratch, out int consumed, out int written);
            offset += consumed;
            if (written > expected - expanded.Length) { JxlThrowHelper.Invalid("JPEG reconstruction payload exceeds its declared length."); }
            expanded.Write(scratch[..written]);
            if (status == OperationStatus.Done)
            {
                if (offset != encoded.Length || expanded.Length != expected) { JxlThrowHelper.Invalid("invalid Brotli JPEG reconstruction payload length."); }
                break;
            }
            if (status != OperationStatus.DestinationTooSmall || (consumed == 0 && written == 0))
            { JxlThrowHelper.Invalid("invalid Brotli JPEG reconstruction payload."); }
        }
        result.Data = expanded.ToArray();
        return result;
    }

    private static uint ReadPointCount(JxlBitReader reader) => reader.ReadU32(0, U(2, 1), U(4, 4), U(16, 20));
    private static uint NextBlock(JxlBitReader reader, uint entry, uint previous)
    {
        ulong difference = reader.ReadU32(0, U(3, 1), U(5, 9), U(28, 41));
        ulong index = entry == 0 ? difference : previous + difference + 1;
        if (index > 3UL << 26) { JxlThrowHelper.Invalid("JPEG reconstruction block index is too large."); }
        return (uint)index;
    }
    private static int[] ReadLengths(JxlBitReader reader, int count, int increment)
    {
        int[] lengths = new int[count];
        for (int i = 0; i < count; i++) { lengths[i] = (int)reader.ReadBits(16) + increment; }
        return lengths;
    }
    private static int Sum<T>(T[] values, Func<T, int> size)
    {
        long sum = 0;
        foreach (T value in values) { sum += size(value); }
        if (sum > int.MaxValue) { JxlThrowHelper.Invalid("JPEG reconstruction size is too large."); }
        return (int)sum;
    }
    private static int FirstMetadataLength(AppMarker[] markers, int type, int overhead)
    {
        foreach (AppMarker marker in markers) { if (marker.Type == type) { return marker.Length - overhead; } }
        return 0;
    }
}
