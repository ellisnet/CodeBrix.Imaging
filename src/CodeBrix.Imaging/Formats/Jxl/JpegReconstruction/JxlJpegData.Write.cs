// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Inverse of JPEG reconstruction fields in jxl-oxide crates/jxl-jbr/src/lib.rs
// and huffman.rs, 0.12.6, f8ae722ef2d6b782941c89517d19cfbf605c4a9d
// (Apache-2.0 option). BCL Brotli encoding is specific to CodeBrix.Imaging.

using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using static CodeBrix.Imaging.Formats.Jxl.Bitstream.JxlU32;

namespace CodeBrix.Imaging.Formats.Jxl.JpegReconstruction;

internal sealed partial class JxlJpegData
{
    internal byte[] Write(int brotliEffort, CancellationToken cancellationToken)
    {
        if (brotliEffort is < 0 or > 11) { throw new ArgumentOutOfRangeException(nameof(brotliEffort)); }
        cancellationToken.ThrowIfCancellationRequested();
        this.ValidateForWrite();
        using MemoryStream output = new(); JxlBitWriter writer = new(output);
        writer.WriteBool(this.IsGray);
        foreach (byte marker in this.Markers) { writer.WriteBits(6, (uint)marker - 0xc0); }
        foreach (AppMarker marker in this.AppMarkers)
        {
            writer.WriteU32((uint)marker.Type, 0, 1, U(1, 2), U(2, 4));
            writer.WriteBits(16, (uint)marker.Length - 1);
        }
        foreach (int length in this.CommentLengths) { writer.WriteBits(16, (uint)length - 1); }
        writer.WriteBits(2, (uint)this.QuantTables.Length - 1);
        foreach (QuantTable table in this.QuantTables)
        { writer.WriteBits(1, (uint)table.Precision); writer.WriteBits(2, (uint)table.Index); writer.WriteBool(table.IsLast); }
        uint componentType = this.Components.Length == 1 && this.Components[0].Id == 1 ? 0U :
            this.Components.Length == 3 && this.Components[0].Id == 1 && this.Components[1].Id == 2 && this.Components[2].Id == 3 ? 1U :
            this.Components.Length == 3 && this.Components[0].Id == 'R' && this.Components[1].Id == 'G' && this.Components[2].Id == 'B' ? 2U : 3U;
        writer.WriteBits(2, componentType);
        if (componentType == 3)
        {
            writer.WriteBits(2, (uint)this.Components.Length - 1);
            foreach (Component component in this.Components) { writer.WriteBits(8, component.Id); }
        }
        foreach (Component component in this.Components) { writer.WriteBits(2, (uint)component.QuantIndex); }
        writer.WriteU32((uint)this.HuffmanCodes.Length, 4, U(3, 2), U(4, 10), U(6, 26));
        foreach (HuffmanCode code in this.HuffmanCodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.WriteBool(code.IsAc); writer.WriteBits(2, (uint)code.Id); writer.WriteBool(code.IsLast);
            foreach (byte count in code.Counts) { writer.WriteU32(count, 0, 1, U(3, 2), U(8)); }
            foreach (ushort value in code.Values) { writer.WriteU32(value, U(2), U(2, 4), U(4, 8), U(8, 1)); }
        }
        foreach (Scan scan in this.Scans)
        {
            writer.WriteBits(2, (uint)scan.Components.Length - 1);
            writer.WriteBits(6, (uint)scan.Ss); writer.WriteBits(6, (uint)scan.Se);
            writer.WriteBits(4, (uint)scan.Al); writer.WriteBits(4, (uint)scan.Ah);
            foreach (ScanComponent component in scan.Components)
            { writer.WriteBits(2, (uint)component.Index); writer.WriteBits(2, (uint)component.AcTable); writer.WriteBits(2, (uint)component.DcTable); }
            writer.WriteU32((uint)scan.LastNeededPass, 0, 1, 2, U(3, 3));
        }
        if (Array.IndexOf(this.Markers, (byte)0xdd) >= 0) { writer.WriteBits(16, (uint)this.RestartInterval); }
        foreach (Scan scan in this.Scans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            uint[] points = new uint[scan.ResetPoints.Count]; scan.ResetPoints.CopyTo(points); Array.Sort(points);
            WritePointCount(writer, points.Length); uint previous = 0;
            for (int i = 0; i < points.Length; i++) { WritePoint(writer, points[i], i, previous); previous = points[i]; }
            points = new uint[scan.ExtraZeroRuns.Count]; scan.ExtraZeroRuns.Keys.CopyTo(points, 0); Array.Sort(points);
            WritePointCount(writer, points.Length); previous = 0;
            for (int i = 0; i < points.Length; i++)
            {
                writer.WriteU32(scan.ExtraZeroRuns[points[i]], 1, U(2, 2), U(4, 5), U(8, 20));
                WritePoint(writer, points[i], i, previous); previous = points[i];
            }
        }
        foreach (int length in this.IntermarkerLengths) { writer.WriteBits(16, (uint)length); }
        writer.WriteU32((uint)this.TailLength, 0, U(8, 1), U(16, 257), U(22, 65793));
        writer.WriteBool(this.PaddingBitCount != 0);
        if (this.PaddingBitCount != 0)
        { writer.WriteBits(24, (uint)this.PaddingBitCount); writer.WriteBitstream(this.Padding, this.PaddingBitCount); }
        writer.ZeroPadToByte();
        byte[] compressed = new byte[BrotliEncoder.GetMaxCompressedLength(this.Data.Length)];
        cancellationToken.ThrowIfCancellationRequested();
        if (!BrotliEncoder.TryCompress(this.Data, compressed, out int written, brotliEffort, 22))
        { throw new InvalidOperationException("Failed to compress JPEG reconstruction data."); }
        cancellationToken.ThrowIfCancellationRequested(); writer.WriteBytes(compressed.AsSpan(0, written));
        return output.ToArray();
    }

    internal void ValidateForWrite()
    {
        if (this.Markers is not { Length: > 0 } || this.Markers[^1] != 0xd9 || this.AppMarkers == null || this.CommentLengths == null ||
            this.IntermarkerLengths == null || this.QuantTables == null || this.Components == null || this.HuffmanCodes == null || this.Scans == null || this.Data == null)
        { throw new ArgumentException("Incomplete JPEG reconstruction data."); }
        int apps = 0, comments = 0, scans = 0, intermarkers = 0;
        for (int i = 0; i < this.Markers.Length; i++)
        {
            byte marker = this.Markers[i];
            if (marker < 0xc0 || (marker == 0xd9 && i != this.Markers.Length - 1)) { throw new ArgumentException("Invalid JPEG marker sequence."); }
            if (marker is >= 0xe0 and <= 0xef) { apps++; }
            else if (marker == 0xfe) { comments++; }
            else if (marker == 0xda) { scans++; }
            else if (marker == 0xff) { intermarkers++; }
        }
        if (apps != this.AppMarkers.Length || comments != this.CommentLengths.Length || scans != this.Scans.Length || intermarkers != this.IntermarkerLengths.Length ||
            this.QuantTables.Length is < 1 or > 4 || this.Components.Length is < 1 or > 4 || this.HuffmanCodes.Length is < 2 or > 89 ||
            this.TailLength < 0 || this.PaddingBitCount is < 0 or >= 1 << 24 || this.RestartInterval is < 0 or > 65535)
        { throw new ArgumentException("Invalid JPEG reconstruction table sizes."); }
        foreach (AppMarker marker in this.AppMarkers)
        {
            if (marker.Type is < 0 or > 3 || marker.Length > 65536 || marker.Length < (marker.Type == 1 ? 17 : marker.Type == 2 ? 9 : marker.Type == 3 ? 32 : 3))
            { throw new ArgumentException("Invalid JPEG APP reconstruction field."); }
        }
        foreach (HuffmanCode code in this.HuffmanCodes)
        {
            if (code.Counts is not { Length: 17 } || code.Values == null || code.Counts[0] != 0 || code.Id is < 0 or > 3)
            { throw new ArgumentException("Invalid JPEG Huffman reconstruction table."); }
            int count = 0; foreach (byte n in code.Counts) { count += n; }
            if (count > 257 || count != code.Values.Length || (count == 0 ? !code.IsLast : code.Values[^1] != 256))
            { throw new ArgumentException("JPEG Huffman table size or sentinel mismatch."); }
        }
        foreach (Scan scan in this.Scans)
        {
            if (scan.Components is not { Length: >= 1 and <= 4 } || scan.ResetPoints == null || scan.ExtraZeroRuns == null)
            { throw new ArgumentException("Incomplete JPEG scan reconstruction data."); }
            if (scan.ResetPoints.Count > 65555 || scan.ExtraZeroRuns.Count > 65555)
            { throw new NotSupportedException("JPEG reconstruction supports at most 65555 reset or extra-zero-run points per scan."); }
            foreach (uint point in scan.ResetPoints)
            { if (point > 3U << 26) { throw new NotSupportedException("JPEG reconstruction block index exceeds the format limit."); } }
            foreach (var point in scan.ExtraZeroRuns)
            {
                if (point.Key > 3U << 26 || point.Value is < 1 or > 275)
                { throw new NotSupportedException("JPEG reconstruction extra-zero-run field exceeds the format limit."); }
            }
        }
        if (this.TailLength > 65793 + ((1 << 22) - 1))
        { throw new NotSupportedException("JPEG trailer exceeds the reconstruction format limit."); }
        if (this.PaddingBitCount != 0 && (this.Padding == null || this.Padding.Length < (this.PaddingBitCount + 7) / 8))
        { throw new ArgumentException("JPEG padding bit count exceeds available data."); }
        long expected = (long)this.AppLength + this.CommentLength + this.IntermarkerLength + this.TailLength;
        if (expected != this.Data.Length) { throw new ArgumentException("JPEG reconstruction byte payload size mismatch."); }
    }

    private static void WritePointCount(JxlBitWriter writer, int count) => writer.WriteU32((uint)count, 0, U(2, 1), U(4, 4), U(16, 20));
    private static void WritePoint(JxlBitWriter writer, uint index, int entry, uint previous)
    {
        if (index > 3U << 26 || (entry != 0 && index <= previous)) { throw new ArgumentException("Invalid JPEG reconstruction block index."); }
        writer.WriteU32(entry == 0 ? index : index - previous - 1, 0, U(3, 1), U(5, 9), U(28, 41));
    }
}
