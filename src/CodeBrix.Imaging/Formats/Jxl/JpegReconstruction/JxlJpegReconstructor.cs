// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Marker reconstruction adapted from jxl-oxide crates/jxl-jbr/src/
// reconstruct.rs, 0.12.6, f8ae722ef2d6b782941c89517d19cfbf605c4a9d
// (Apache-2.0 option).

using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;

namespace CodeBrix.Imaging.Formats.Jxl.JpegReconstruction;

internal static class JxlJpegReconstructor
{
    internal static void Write(Stream output, JxlJpegData data, JxlJpegCoefficients coefficients,
        ReadOnlySpan<byte> icc, ReadOnlySpan<byte> exif, ReadOnlySpan<byte> xmp, CancellationToken cancellationToken,
        long maxScanBlocks = 100_000_000)
    {
        if (coefficients.Image.Size.Width > 65535 || coefficients.Image.Size.Height > 65535 || coefficients.Image.BitDepth.Bits != 8 ||
            coefficients.Image.BitDepth.ExponentBits != 0 || data.Components.Length != (data.IsGray ? 1 : 3) ||
            data.IsGray != (coefficients.Image.Color.ColorSpace == 1))
        { JxlThrowHelper.Invalid("image dimensions, precision or channels are incompatible with JPEG reconstruction."); }
        bool hasIcc = false;
        foreach (JxlJpegData.AppMarker marker in data.AppMarkers)
        {
            hasIcc |= marker.Type == 1;
            if ((marker.Type == 2 && marker.Length - 9 != exif.Length) || (marker.Type == 3 && marker.Length - 32 != xmp.Length))
            { JxlThrowHelper.Invalid("JPEG reconstruction metadata lengths do not match."); }
        }
        if (hasIcc && data.IccLength != icc.Length) { JxlThrowHelper.Invalid("JPEG reconstruction ICC length does not match."); }
        JxlJpegHuffman[] dcTables = new JxlJpegHuffman[4], acTables = new JxlJpegHuffman[4];
        int app = 0, comment = 0, intermarker = 0, huffman = 0, quant = 0, scan = 0;
        int appOffset = 0, commentOffset = data.AppLength, intermarkerOffset = commentOffset + data.CommentLength;
        int tailOffset = intermarkerOffset + data.IntermarkerLength;
        int iccCount = 0, iccIndex = 0, iccOffset = 0, restart = 0, paddingRemaining = data.PaddingBitCount;
        foreach (JxlJpegData.AppMarker marker in data.AppMarkers) { if (marker.Type == 1) { iccCount++; } }
        if (iccCount > 255) { JxlThrowHelper.Invalid("too many reconstructed JPEG ICC segments."); }
        using MemoryStream paddingStream = data.Padding == null ? null : new(data.Padding, false);
        JxlBitReader padding = paddingStream == null ? null : new(paddingStream, cancellationToken);
        int[] lastQuant = null;
        bool progressive = false;
        Marker(output, 0xd8);
        foreach (byte marker in data.Markers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (marker)
            {
                case 0xc0:
                case 0xc1:
                case 0xc2:
                    progressive = marker == 0xc2;
                    Marker(output, marker); U16(output, 8 + (data.Components.Length * 3)); output.WriteByte(8);
                    U16(output, (int)coefficients.Image.Size.Height); U16(output, (int)coefficients.Image.Size.Width);
                    output.WriteByte((byte)data.Components.Length);
                    for (int c = 0; c < data.Components.Length; c++)
                    {
                        uint sampling = coefficients.Frame.JpegUpsampling[c < 2 ? 1 - c : c];
                        output.WriteByte(data.Components[c].Id);
                        output.WriteByte(sampling switch { 1 => 0x22, 2 => 0x21, 3 => 0x12, _ => 0x11 });
                        int qi = data.Components[c].QuantIndex;
                        if ((uint)qi >= data.QuantTables.Length) { JxlThrowHelper.Invalid("invalid reconstructed JPEG quantization index."); }
                        output.WriteByte((byte)data.QuantTables[qi].Index);
                    }
                    break;
                case 0xc4:
                    int lastHuffman = huffman, huffmanLength = 2;
                    do
                    {
                        if (lastHuffman >= data.HuffmanCodes.Length) { JxlThrowHelper.Invalid("unterminated reconstructed JPEG Huffman segment."); }
                        if (data.HuffmanCodes[lastHuffman].Values.Length != 0) { huffmanLength += 16 + data.HuffmanCodes[lastHuffman].Values.Length; }
                    } while (!data.HuffmanCodes[lastHuffman++].IsLast);
                    Marker(output, marker); U16(output, huffmanLength);
                    for (; huffman < lastHuffman; huffman++)
                    {
                        JxlJpegData.HuffmanCode definition = data.HuffmanCodes[huffman];
                        if (definition.Values.Length == 0) { continue; } // Empty DHT marker preserves existing tables.
                        JxlJpegHuffman table = JxlJpegHuffman.Build(definition);
                        (definition.IsAc ? acTables : dcTables)[definition.Id] = table;
                        output.WriteByte((byte)(definition.Id | (definition.IsAc ? 16 : 0)));
                        int lastDepth = 16;
                        while (lastDepth > 0 && definition.Counts[lastDepth] == 0) { lastDepth--; }
                        for (int depth = 1; depth <= 16; depth++)
                        { output.WriteByte((byte)(definition.Counts[depth] - (depth == lastDepth ? 1 : 0))); }
                        foreach (ushort value in definition.Values.AsSpan(0, definition.Values.Length - 1)) { output.WriteByte((byte)value); }
                    }
                    break;
                case 0xdb:
                    int lastTable = quant, quantLength = 2;
                    do
                    {
                        if (lastTable >= data.QuantTables.Length) { JxlThrowHelper.Invalid("unterminated reconstructed JPEG quantization segment."); }
                        quantLength += data.QuantTables[lastTable].Precision == 0 ? 65 : 129;
                    } while (!data.QuantTables[lastTable++].IsLast);
                    Marker(output, marker); U16(output, quantLength);
                    for (; quant < lastTable; quant++)
                    {
                        JxlJpegData.QuantTable table = data.QuantTables[quant];
                        int channel = Array.FindIndex(data.Components, component => component.QuantIndex == quant);
                        if (channel >= 0) { lastQuant = coefficients.Quantization[coefficients.ComponentPlane(channel)]; }
                        if (lastQuant == null) { JxlThrowHelper.Invalid("missing reconstructed JPEG quantization values."); }
                        output.WriteByte((byte)(table.Index | (table.Precision << 4)));
                        foreach (byte position in JxlJpegScanWriter.NaturalOrder)
                        {
                            // Raw JPEG XL dequantization matrices are transposed.
                            int value = lastQuant[((position & 7) * 8) + (position / 8)];
                            if (table.Precision == 0)
                            {
                                if (value > 255) { JxlThrowHelper.Invalid("JPEG quantization exceeds its declared precision."); }
                                output.WriteByte((byte)value);
                            }
                            else { U16(output, value); }
                        }
                    }
                    break;
                case 0xdd:
                    restart = data.RestartInterval;
                    Marker(output, marker); U16(output, 4); U16(output, restart);
                    break;
                case 0xda:
                    if (scan >= data.Scans.Length) { JxlThrowHelper.Invalid("missing reconstructed JPEG scan."); }
                    JxlJpegData.Scan description = data.Scans[scan++];
                    Marker(output, marker); U16(output, 6 + (2 * description.Components.Length));
                    output.WriteByte((byte)description.Components.Length);
                    foreach (JxlJpegData.ScanComponent component in description.Components)
                    {
                        if ((uint)component.Index >= data.Components.Length) { JxlThrowHelper.Invalid("invalid reconstructed JPEG component."); }
                        output.WriteByte(data.Components[component.Index].Id);
                        output.WriteByte((byte)((component.DcTable << 4) | component.AcTable));
                    }
                    output.WriteByte((byte)description.Ss); output.WriteByte((byte)description.Se);
                    output.WriteByte((byte)((description.Ah << 4) | description.Al));
                    JxlJpegScanWriter.Write(output, coefficients, description, progressive, dcTables, acTables, restart,
                        padding, ref paddingRemaining, ref maxScanBlocks, cancellationToken);
                    break;
                case >= 0xd0 and <= 0xd7:
                    Marker(output, marker);
                    break;
                case 0xd9:
                    Marker(output, marker); output.Write(data.Data.AsSpan(tailOffset, data.TailLength));
                    break;
                case >= 0xe0 and <= 0xef:
                    JxlJpegData.AppMarker appMarker = data.AppMarkers[app++];
                    if (appMarker.Type == 0)
                    {
                        output.WriteByte(255); output.Write(data.Data.AsSpan(appOffset, appMarker.Length)); appOffset += appMarker.Length;
                        break;
                    }
                    Marker(output, appMarker.Type == 1 ? 0xe2 : 0xe1); U16(output, appMarker.Length - 1);
                    if (appMarker.Type == 1)
                    {
                        output.Write("ICC_PROFILE\0"u8); output.WriteByte((byte)++iccIndex); output.WriteByte((byte)iccCount);
                        int size = appMarker.Length - 17; output.Write(icc.Slice(iccOffset, size)); iccOffset += size;
                    }
                    else if (appMarker.Type == 2) { output.Write("Exif\0\0"u8); output.Write(exif); }
                    else { output.Write("http://ns.adobe.com/xap/1.0/\0"u8); output.Write(xmp); }
                    break;
                case 0xfe:
                    output.WriteByte(255);
                    int commentLength = data.CommentLengths[comment++];
                    output.Write(data.Data.AsSpan(commentOffset, commentLength)); commentOffset += commentLength;
                    break;
                case 0xff:
                    int intermarkerLength = data.IntermarkerLengths[intermarker++];
                    output.Write(data.Data.AsSpan(intermarkerOffset, intermarkerLength)); intermarkerOffset += intermarkerLength;
                    break;
                default:
                    JxlThrowHelper.Invalid("unsupported marker in JPEG reconstruction data.");
                    break;
            }
        }
        if (huffman != data.HuffmanCodes.Length || quant != data.QuantTables.Length || paddingRemaining != 0)
        { JxlThrowHelper.Invalid("unused JPEG reconstruction tables or padding."); }
    }
    private static void Marker(Stream output, int value) { output.WriteByte(255); output.WriteByte((byte)value); }
    private static void U16(Stream output, int value)
    {
        if ((uint)value > 65535) { JxlThrowHelper.Invalid("reconstructed JPEG field exceeds sixteen bits."); }
        Span<byte> bytes = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value); output.Write(bytes);
    }
}
