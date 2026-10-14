// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from libjxl lib/jxl/enc_fast_lossless.cc (LLPrepare, fixed tree,
// ChannelRowProcessor, gradient predictor and run encoding),
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.IO;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Coding;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Entropy;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Modular.FastLossless;

internal static class JxlFastLosslessEncoder
{
    private static readonly JxlIntegerConfig RawConfig = new(0, 0, 0), RunConfig = new(4, 0, 0);

    internal static bool CanEncode(JxlImageHeader image, JxlFrameHeader frame) => !image.XybEncoded && frame.Modular
        && image.BitDepth.ExponentBits == 0 && image.BitDepth.Bits is >= 1 and <= 16
        && image.ExtraChannels.Length <= 1 && (image.ExtraChannels.Length == 0 ||
            (image.ExtraChannels[0].Type == 0 && image.ExtraChannels[0].DimShift == 0 && image.ExtraChannels[0].BitDepth == image.BitDepth))
        && frame.Flags == 0 && frame.PassCount == 1 && frame.LfLevel == 0 && frame.Upsampling == 1 && frame.GroupSizeShift == 1
        && !Array.Exists(frame.ExtraUpsampling, value => value != 1);

    internal static void WriteFrame(JxlBitWriter output, JxlImageHeader image, JxlFrameHeader frame,
        Action<Rectangle, JxlModularPlane[]> import, MemoryAllocator allocator, CancellationToken cancellationToken)
    {
        if (!CanEncode(image, frame)) { throw new ArgumentException("Frame is not eligible for fast lossless coding.", nameof(frame)); }
        int width = (int)frame.SampleWidth, height = (int)frame.SampleHeight, channels = image.ColorChannels + image.ExtraChannels.Length;
        int columns = (width + 255) / 256, rows = (height + 255) / 256;
        int groups = checked(columns * rows), lfGroups = checked((int)frame.LfGroupCount);
        if (2L + lfGroups + groups > 65536) { throw new ArgumentException("JPEG XL frame has too many groups.", nameof(frame)); }
        bool single = groups == 1, color = image.ColorChannels == 3;
        ulong[][] raw = new ulong[4][], runs = new ulong[4][];
        for (int c = 0; c < 4; c++) { raw[c] = new ulong[JxlFastPrefixCode.RawSymbols]; runs[c] = new ulong[JxlFastPrefixCode.RunSymbols]; }
        // As in the chunked native path, sample central rows of the center group.
        // Every later import is one 256x256 group; no full-frame sample buffer.
        int sampleX = (columns - 1) / 2 * 256, sampleY = (rows - 1) / 2 * 256;
        int sampleWidth = Math.Min(256, width - sampleX), sampleHeight = Math.Min(256, height - sampleY);
        // cjxl's public effort 1 calls the fast implementation with effort 2.
        int wanted = Math.Min(256, 4 * groups), begin = Math.Max(0, sampleHeight - wanted) / 2;
        int count = Math.Max(0, Math.Min(wanted, sampleHeight - begin - 1));
        if (count > 0 && sampleWidth >= 8)
        {
            Rectangle bounds = new(sampleX, sampleY + begin, sampleWidth / 8 * 8, count + 1);
            using JxlModularWorkspace workspace = JxlModularWorkspace.ForEncoding(allocator, 65536);
            JxlModularPlane[] planes = Import(bounds, workspace);
            for (int c = 0; c < channels; c++)
            {
                int channel = c;
                Process(planes[c], 1, false, (value, run) =>
                {
                    if (run != 0) { raw[channel][0]++; runs[channel][RunConfig.Encode((uint)run - 8).Token]++; }
                    else { raw[channel][RawConfig.Encode(value).Token]++; }
                }, cancellationToken);
            }
        }
        JxlPrefixEncoder[] codes = new JxlPrefixEncoder[4];
        for (int c = 0; c < 4; c++) { codes[c] = JxlFastPrefixCode.Create(raw[c], runs[c], (int)image.BitDepth.Bits, color); }
        byte[][] payloads = new byte[single ? 1 : 2 + lfGroups + groups][]; Array.Fill(payloads, Array.Empty<byte>());
        using MemoryStream global = new(); JxlBitWriter globalWriter = new(global); WriteGlobal(globalWriter, codes, color);
        for (int group = 0; group < groups; group++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int x = group % columns * 256, y = group / columns * 256;
            Rectangle bounds = new(x, y, Math.Min(256, width - x), Math.Min(256, height - y));
            using JxlModularWorkspace workspace = JxlModularWorkspace.ForEncoding(allocator, 65536);
            JxlModularPlane[] planes = Import(bounds, workspace);
            using MemoryStream stream = single ? null : new();
            JxlBitWriter writer = single ? globalWriter : new(stream);
            if (!single) { writer.WriteBool(true); writer.WriteBool(true); writer.WriteBits(2, 0); }
            for (int c = 0; c < channels; c++)
            {
                JxlPrefixEncoder code = codes[c];
                Process(planes[c], 0, true, (value, run) =>
                {
                    if (run != 0)
                    {
                        code.WriteSymbol(writer, 0);
                        (uint token, int bits, uint extra) = RunConfig.Encode((uint)run - 8);
                        code.WriteSymbol(writer, 224 + token); writer.WriteBits(bits, extra);
                        // Distance 1 has a single-symbol code and no extra bits.
                    }
                    else
                    {
                        (uint token, int bits, uint extra) = RawConfig.Encode(value);
                        code.WriteSymbol(writer, token); writer.WriteBits(bits, extra);
                    }
                }, cancellationToken);
            }
            if (!single) { writer.ZeroPadToByte(); payloads[2 + lfGroups + group] = stream.ToArray(); }
        }
        globalWriter.ZeroPadToByte(); payloads[0] = global.ToArray();
        frame.Write(output, image); uint[] sizes = new uint[payloads.Length];
        for (int i = 0; i < sizes.Length; i++) { sizes[i] = checked((uint)payloads[i].Length); }
        JxlFrameToc.WriteOrdered(output, frame, sizes);
        foreach (byte[] payload in payloads) { cancellationToken.ThrowIfCancellationRequested(); output.WriteBytes(payload); }

        JxlModularPlane[] Import(Rectangle bounds, JxlModularWorkspace workspace)
        {
            JxlModularPlane[] planes = new JxlModularPlane[channels];
            for (int c = 0; c < channels; c++) { planes[c] = workspace.Allocate(bounds.Width, bounds.Height); }
            import(bounds, planes);
            if (color) { JxlRctEncoder.Apply(planes, 6, cancellationToken); }
            return planes;
        }
    }

    // Scalar native chunk size. Runs begin on an eight-sample chunk and may
    // cross row boundaries; short rows and final partial chunks stay exact.
    private static void Process(JxlModularPlane plane, int firstRow, bool finish, Action<uint, int> emit, CancellationToken token)
    {
        Span<uint> residuals = stackalloc uint[8]; int run = 0;
        for (int y = firstRow; y < plane.Height; y++)
        {
            token.ThrowIfCancellationRequested(); Span<int> row = plane.Row(y);
            ReadOnlySpan<int> above = y == 0 ? default : plane.Row(y - 1);
            for (int x = 0; x < row.Length; x += 8)
            {
                int n = Math.Min(8, row.Length - x), prefix = 0;
                for (int i = 0; i < n; i++)
                {
                    int px = x + i, left = px == 0 ? y == 0 ? 0 : above[px] : row[px - 1];
                    int top = y == 0 ? left : above[px], topLeft = y == 0 ? left : px == 0 ? above[px] : above[px - 1];
                    int prediction = (int)Math.Clamp((long)left + top - topLeft, Math.Min(left, top), Math.Max(left, top));
                    residuals[i] = JxlBitWriter.PackSigned(unchecked(row[px] - prediction));
                    if (prefix == i && residuals[i] == 0) { prefix++; }
                }
                if (prefix == n && (run > 0 || prefix > 7)) { run += prefix; }
                else if (prefix + run > 7)
                {
                    emit(0, run + prefix); run = 0;
                    for (int i = prefix; i < n; i++) { emit(residuals[i], 0); }
                }
                else { for (int i = 0; i < n; i++) { emit(residuals[i], 0); } }
            }
        }
        if (finish && run != 0) { emit(0, run); }
    }

    private static void WriteGlobal(JxlBitWriter writer, JxlPrefixEncoder[] codes, bool color)
    {
        writer.WriteBool(true); writer.WriteBool(true); // Default LF factors, global tree.
        writer.WriteBool(false); writer.WriteBool(true); writer.WriteBits(2, 0); // Tree: no LZ77, one cluster.
        writer.WriteBool(true); RawConfig.Write(writer, 15); // Prefix, hybrid 000.
        writer.WriteBool(true); writer.WriteBits(4, 1); writer.WriteBits(1, 1); // Alphabet 4.
        writer.WriteBits(2, 1); writer.WriteBits(2, 3);
        for (uint symbol = 0; symbol < 4; symbol++) { writer.WriteBits(2, symbol); }
        writer.WriteBool(false);
        ReadOnlySpan<byte> bits = [0, 2, 1, 5, 3, 7], lengths = [2, 2, 3, 3, 4, 4];
        ReadOnlySpan<byte> tree = [1, 2, 1, 4, 1, 0, 0, 5, 0, 0, 0, 0, 5, 0, 0, 0, 0, 5, 0, 0, 0, 0, 5, 0, 0, 0];
        foreach (byte value in tree) { writer.WriteBits(lengths[value], bits[value]); }
        writer.WriteBool(true); // Main stream: LZ77, offset 224, minimum 7.
        writer.WriteBits(2, 0); writer.WriteBits(4, 10); RunConfig.Write(writer, 8);
        writer.WriteBool(true); writer.WriteBits(2, 3);
        for (int cluster = 4; cluster >= 0; cluster--) { writer.WriteBits(3, (uint)cluster); }
        writer.WriteBool(true);
        for (int i = 0; i < 5; i++) { RawConfig.Write(writer, 15); }
        writer.WriteBool(true); writer.WriteBits(4, 0); // Distance alphabet 2.
        for (int i = 0; i < 4; i++) { writer.WriteBool(true); writer.WriteBits(4, 8); writer.WriteBits(8, 255); }
        writer.WriteBits(2, 1); writer.WriteBits(2, 0); writer.WriteBits(1, 1); // Constant distance 1.
        foreach (JxlPrefixEncoder code in codes) { code.Write(writer); }
        writer.WriteBool(true); writer.WriteBool(true); // Global tree, default WP.
        writer.WriteBits(2, color ? 1U : 0U);
        if (color) { writer.WriteBits(2, 0); writer.WriteBits(5, 0); writer.WriteBits(2, 0); }
    }
}
