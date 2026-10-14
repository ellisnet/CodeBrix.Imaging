// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// JPEG coefficient preparation adapted from libjxl lib/jxl/enc_frame.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Field/token writing inverts the attributed managed VarDCT decoder grammar.
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Coding;
using CodeBrix.Imaging.Formats.Jxl.Color;
using CodeBrix.Imaging.Formats.Jxl.Container;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Entropy;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Modular;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.JpegReconstruction;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Formats.Jxl.VarDct;
using CodeBrix.Imaging.Memory;
using static CodeBrix.Imaging.Formats.Jxl.Bitstream.JxlU32;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Jpeg;

internal static class JxlJpegEncoder
{
    internal static void Write(Stream output, JxlJpegParser jpeg, MemoryAllocator allocator, int effort, CancellationToken cancellationToken,
        long inputBytes = 0)
    {
        JxlJpegCoefficients coefficients = jpeg.Coefficients;
        JxlImageHeader image = coefficients.Image; JxlFrameHeader frame = coefficients.Frame;
        long sections = JxlEncoderPreflight.ValidateToc(frame, frame.GroupSizeShift, frame.PassCount);
        long fixedBytes = sections * 4096 + JxlEncoderPreflight.MetadataBytes(jpeg.Icc, jpeg.Exif, jpeg.Xmp, null);
        JxlEncoderPreflight.Require(JxlEncoderPreflight.JpegBytes(jpeg.CoefficientSamples, inputBytes, 0) + fixedBytes, allocator);
        frame.Flags = 128; // JPEG coefficients must not undergo adaptive LF smoothing.
        frame.Restoration = new() { GaborEnabled = false, EpfIterations = 0 };
        byte[] reconstruction = jpeg.Data.Write(effort, cancellationToken);
        fixedBytes += reconstruction.LongLength * 4;
        // Tokenize before any output, admitting each group's retained tokens as
        // they are produced; token counts depend on the coefficient content.
        JxlToken[][] tokens = Tokenize(coefficients, tokenCount => JxlEncoderPreflight.Require(
            JxlEncoderPreflight.JpegBytes(jpeg.CoefficientSamples, inputBytes, tokenCount) + fixedBytes, allocator), cancellationToken);
        byte[] encodedIcc = image.Color.WantIcc ? JxlIccCodec.Encode(jpeg.Icc, cancellationToken) : null;
        JxlContainerWriter.Start(output, jpeg.Icc.Length > 1 << 22 || (long)frame.Width * frame.Height > 268435456 ? 10 : 5, cancellationToken);
        if (jpeg.Exif.Length != 0)
        {
            byte[] exif = new byte[jpeg.Exif.Length + 4]; jpeg.Exif.CopyTo(exif, 4);
            JxlContainerWriter.WriteMetadata(output, "Exif"u8, exif, true, effort, cancellationToken);
        }
        if (jpeg.Xmp.Length != 0) { JxlContainerWriter.WriteMetadata(output, "xml "u8, jpeg.Xmp, true, effort, cancellationToken); }
        JxlContainerWriter.WriteBox(output, "jbrd"u8, reconstruction, cancellationToken);
        JxlContainerWriter.BeginFinalCodestream(output, cancellationToken);
        JxlBitWriter writer = new(output); image.Write(writer);
        if (encodedIcc != null) { JxlIccCodec.WriteEncoded(writer, encodedIcc, effort, cancellationToken); }
        WriteFrame(writer, coefficients, tokens, allocator, effort, cancellationToken);
        writer.ZeroPadToByte();
    }

    private static JxlToken[][] Tokenize(JxlJpegCoefficients coefficients, Action<long> admit, CancellationToken cancellationToken)
    {
        JxlFrameHeader frame = coefficients.Frame;
        int width = (int)frame.Width, dimension = (int)frame.GroupDimension, groups = (int)frame.GroupCount;
        int columns = (width + dimension - 1) / dimension;
        int[] hs = new int[3], vs = new int[3];
        ChromaShifts(frame, hs, vs);
        // Groups tokenize independently in bands; the running token total is
        // admitted after each band, so storage is still checked as it grows.
        JxlToken[][] tokens = new JxlToken[groups][]; long count = 0;
        int band = Math.Max(1, JxlParallel.EncoderParallelism is -1 ? Environment.ProcessorCount : JxlParallel.EncoderParallelism);
        for (int start = 0; start < groups; start += band)
        {
            int first = start, end = Math.Min(groups, start + band);
            JxlParallel.ForEncoder(end - first, cancellationToken, i =>
            {
                int group = first + i;
                tokens[group] = CoefficientTokens(coefficients, group % columns * dimension, group / columns * dimension, dimension, hs, vs, cancellationToken);
            });
            for (int group = first; group < end; group++) { count += tokens[group].LongLength; }
            admit(count);
        }
        return tokens;
    }

    private static void ChromaShifts(JxlFrameHeader frame, int[] hs, int[] vs)
    {
        bool horizontal = Array.Exists(frame.JpegUpsampling, j => j is 1 or 2), vertical = Array.Exists(frame.JpegUpsampling, j => j is 1 or 3);
        for (int c = 0; c < 3; c++)
        {
            hs[c] = horizontal && frame.JpegUpsampling[c] is 0 or 3 ? 1 : 0;
            vs[c] = vertical && frame.JpegUpsampling[c] is 0 or 2 ? 1 : 0;
        }
    }

    private static void WriteFrame(JxlBitWriter output, JxlJpegCoefficients coefficients, JxlToken[][] tokens, MemoryAllocator allocator,
        int effort, CancellationToken cancellationToken)
    {
        JxlFrameHeader frame = coefficients.Frame;
        int width = (int)frame.Width, height = (int)frame.Height, dimension = (int)frame.GroupDimension, lfDimension = dimension * 8;
        int groups = (int)frame.GroupCount, lfGroups = (int)frame.LfGroupCount;
        int lfColumns = (width + lfDimension - 1) / lfDimension;
        int[] hs = new int[3], vs = new int[3];
        ChromaShifts(frame, hs, vs);
        bool horizontal = Array.Exists(frame.JpegUpsampling, j => j is 1 or 2), vertical = Array.Exists(frame.JpegUpsampling, j => j is 1 or 3);
        (byte[] Data, long Bits)[] sections = new (byte[], long)[2 + lfGroups + groups];
        sections[0] = Section(writer =>
        {
            writer.WriteBool(false);
            for (int c = 0; c < 3; c++) { writer.WriteF16(coefficients.Quantization[c][0] * (128f / 2040)); }
            writer.WriteU32(65536, U(11, 1), U(11, 2049), U(12, 4097), U(16, 8193));
            writer.WriteU32(1, 16, U(5, 1), U(8, 1), U(16, 1));
            writer.WriteBool(false); // Three block contexts, one per channel; no LF/quant thresholds.
            for (int c = 0; c < 4; c++) { writer.WriteBits(4, 0); }
            byte[] map = new byte[39];
            for (int i = 0; i < map.Length; i++) { map[i] = (byte)(i / 13); }
            JxlEntropyEncoder.WriteContextMap(writer, map);
            writer.WriteBool(false); writer.WriteU32(84, 84, 256, U(8, 2), U(16, 258));
            writer.WriteF16(0); writer.WriteF16(0); writer.WriteBits(8, 128); writer.WriteBits(8, 128);
            writer.WriteBool(false); // Each Modular substream owns its tree.
        });
        JxlParallel.ForEncoder(lfGroups, cancellationToken, group =>
        {
            int left = group % lfColumns * lfDimension, top = group / lfColumns * lfDimension;
            int w = Math.Min(lfDimension, width - left), h = Math.Min(lfDimension, height - top);
            int bw = PaddedBlocks(w, horizontal), bh = PaddedBlocks(h, vertical);
            using JxlModularWorkspace workspace = JxlModularWorkspace.ForEncoding(allocator, Math.Max(262144, (long)w * h));
            sections[1 + group] = Section(writer =>
            {
                writer.WriteBits(2, 0); // Exact LF integer precision.
                JxlModularPlane[] dc = new JxlModularPlane[3];
                for (int order = 0; order < 3; order++)
                {
                    int c = order == 2 ? 2 : 1 - order;
                    JxlModularPlane plane = workspace.Allocate(bw >> hs[c], bh >> vs[c], hs[c], vs[c]); dc[order] = plane;
                    int offset = frame.YCbCr ? 0 : 1024 / coefficients.Quantization[c][0];
                    for (int y = 0; y < plane.Height; y++)
                    {
                        cancellationToken.ThrowIfCancellationRequested(); Span<int> row = plane.Row(y);
                        Span<int> source = coefficients.Planes[c].Row((top >> vs[c]) + (y * 8));
                        for (int x = 0; x < row.Length; x++) { row[x] = source[(left >> hs[c]) + (x * 8)] + offset; }
                    }
                }
                Modular(writer, dc, allocator, effort, cancellationToken);
                writer.WriteBits(JxlIntegerConfig.ContextBits((uint)(bw * bh - 1)), (uint)(bw * bh - 1));
                JxlModularPlane[] metadata = [workspace.Allocate((w + 63) / 64, (h + 63) / 64),
                    workspace.Allocate((w + 63) / 64, (h + 63) / 64), workspace.Allocate(bw * bh, 2), workspace.Allocate(bw, bh)];
                // Clean planes encode zero correlation, DCT8, multiplier 1 and sharpness 0.
                Modular(writer, metadata, allocator, effort, cancellationToken);
            });
        });
        JxlEntropyEncoder encoder = JxlEntropyEncoder.CreateOptimized(new JxlTokenSequence(tokens), 3 * 495, effort == 1, effort, false, cancellationToken);
        sections[1 + lfGroups] = Section(writer =>
        {
            writer.WriteBool(false); // Explicit JPEG quantization matrix, other transform matrices default.
            writer.WriteBits(3, 7); writer.WriteF16(1f / 2040);
            using JxlModularWorkspace workspace = JxlModularWorkspace.ForEncoding(allocator, 262144);
            JxlModularPlane[] planes = [workspace.Allocate(8, 8), workspace.Allocate(8, 8), workspace.Allocate(8, 8)];
            for (int c = 0; c < 3; c++)
            { for (int y = 0; y < 8; y++) { coefficients.Quantization[c].AsSpan(y * 8, 8).CopyTo(planes[c].Row(y)); } }
            Modular(writer, planes, allocator, effort, cancellationToken);
            for (int i = 1; i < 17; i++) { writer.WriteBits(3, 0); }
            writer.WriteBits(JxlIntegerConfig.ContextBits((uint)groups - 1), 0); // One histogram preset.
            writer.WriteU32(0, 0x5f, 0x13, 0, U(13)); // Natural coefficient order.
            encoder.WriteHeader(writer);
        });
        JxlParallel.ForEncoder(groups, cancellationToken, group =>
        { sections[2 + lfGroups + group] = Section(writer => encoder.WriteTokens(writer, tokens[group], cancellationToken)); });
        if (groups == 1)
        {
            // A single group has one TOC entry, with no padding between logical sections.
            sections = [Section(writer => { foreach (var section in sections) { writer.WriteBitstream(section.Data, section.Bits); } })];
        }
        frame.Write(output, coefficients.Image); uint[] lengths = new uint[sections.Length];
        for (int i = 0; i < lengths.Length; i++) { lengths[i] = (uint)sections[i].Data.Length; }
        JxlFrameToc.WriteOrdered(output, frame, lengths);
        foreach (var section in sections) { cancellationToken.ThrowIfCancellationRequested(); output.WriteBytes(section.Data); }
    }

    private static JxlToken[] CoefficientTokens(JxlJpegCoefficients coefficients, int left, int top, int dimension,
        int[] hs, int[] vs, CancellationToken cancellationToken)
    {
        int width = Math.Min(dimension, (int)coefficients.Frame.Width - left), height = Math.Min(dimension, (int)coefficients.Frame.Height - top);
        int bw = PaddedBlocks(width, Array.Exists(hs, shift => shift != 0)), bh = PaddedBlocks(height, Array.Exists(vs, shift => shift != 0));
        uint[][] rows = new uint[3][]; for (int c = 0; c < 3; c++) { rows[c] = new uint[bw >> hs[c]]; }
        (ushort X, ushort Y)[] order = new JxlHfPass().Order(0, 0);
        List<JxlToken> tokens = []; Span<int> values = stackalloc int[64];
        for (int y = 0; y < bh; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = 0; x < bw; x++)
            {
                for (int channel = 0; channel < 3; channel++)
                {
                    int c = channel == 2 ? 2 : 1 - channel, sx = x >> hs[c], sy = y >> vs[c];
                    if ((sx << hs[c]) != x || (sy << vs[c]) != y) { continue; }
                    int nonzero = 0;
                    for (int i = 1; i < 64; i++)
                    {
                        // JPEG coefficient planes use ordinary row-major DCT coordinates;
                        // JPEG XL's square-DCT natural order is transposed on the wire.
                        (int dx, int dy) = order[i];
                        int value = coefficients.Planes[c].Get((left >> hs[c]) + (sx * 8) + dy, (top >> vs[c]) + (sy * 8) + dx);
                        values[i] = value; if (value != 0) { nonzero++; }
                    }
                    uint[] row = rows[c]; uint predicted = sy == 0 ? sx == 0 ? 32 : row[sx - 1] : sx == 0 ? row[sx] : (row[sx] + row[sx - 1] + 1) >> 1;
                    int predictedContext = (int)(predicted >= 8 ? 4 + (predicted / 2) : predicted);
                    tokens.Add(new(channel + (predictedContext * 3), (uint)nonzero)); row[sx] = (uint)nonzero;
                    int previous = nonzero <= 4 ? 1 : 0;
                    for (int i = 1; nonzero != 0 && i < 64; i++)
                    {
                        int context = 111 + (channel * 458) + ((JxlHfCoefficients.NonzeroContexts[nonzero - 1] + JxlHfCoefficients.FrequencyContexts[i - 1]) * 2) + previous;
                        tokens.Add(new(context, JxlBitWriter.PackSigned(values[i])));
                        previous = values[i] == 0 ? 0 : 1; nonzero -= previous;
                    }
                }
            }
        }
        return tokens.ToArray();
    }

    private static void Modular(JxlBitWriter writer, JxlModularPlane[] planes, MemoryAllocator allocator, int effort, CancellationToken token) =>
        JxlModularEncoder.WriteStreamCore(writer, planes, 0, null, null, effort == 1, effort, allocator, token);

    private static int PaddedBlocks(int pixels, bool pad) => ((pixels + 7) / 8 + (pad ? 1 : 0)) & (pad ? ~1 : -1);

    private static (byte[] Data, long Bits) Section(Action<JxlBitWriter> write)
    {
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream); write(writer);
        long bits = writer.Position; writer.ZeroPadToByte(); return (stream.ToArray(), bits);
    }
}
