// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Inverts the attributed jxl-oxide managed VarDCT frame, HF metadata and
// coefficient grammar, f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0).
// Uses the separately attributed managed transform, quantizer and entropy ports.
// Progressive frequency passes and restoration defaults adapt libjxl
// enc_frame.cc, enc_progressive_split.cc and enc_heuristics.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31
// (BSD-3-Clause plus PATENTS). Copyright (c) the JPEG XL Project Authors.

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Coding;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Entropy;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Modular;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Formats.Jxl.VarDct;
using CodeBrix.Imaging.Memory;
using static CodeBrix.Imaging.Formats.Jxl.Bitstream.JxlU32;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;

internal static class JxlVarDctEncoder
{
    internal static void WriteFrame(JxlBitWriter output, JxlImageHeader image, JxlFrameHeader frame, JxlFloatFrame xyb,
        JxlModularPlane[] extras, float distance, int effort, MemoryAllocator allocator, CancellationToken cancellationToken,
        JxlTransformType? transform = null, bool progressive = false, float photonNoiseIso = 0,
        JxlFloatFrame linearReference = null, float metricIntensity = 80, int decodingSpeed = 0, JxlFrameFeatures features = null, bool progressiveDc = false, JxlDequantMatrices matrices = null, bool noiseModeling = false, bool automaticPatches = false, bool automaticDots = false)
    {
        if (frame.Modular || !image.XybEncoded || frame.YCbCr || frame.PassCount != 1 || frame.LfLevel > 4 ||
            frame.Upsampling is not (1 or 2 or 4 or 8) || frame.GroupSizeShift != 1 || (frame.Flags & ~160UL) != 0)
        { throw new ArgumentException("Expected a single-pass XYB frame.", nameof(frame)); }
        if (extras.Length != image.ExtraChannels.Length) { throw new ArgumentException("Extra-channel count mismatch.", nameof(extras)); }
        int width = (int)frame.SampleWidth, height = (int)frame.SampleHeight;
        for (int c = 0; c < extras.Length; c++)
        {
            int shift = (int)image.ExtraChannels[c].DimShift;
            if (extras[c].Width != (width + (1 << shift) - 1) >> shift || extras[c].Height != (height + (1 << shift) - 1) >> shift || frame.ExtraUpsampling[c] != frame.Upsampling)
            { throw new ArgumentException("Extra channels must match their declared sample resolution.", nameof(extras)); }
        }
        JxlChromaticityQuantization.Adjust(xyb, frame, distance, effort, cancellationToken);
        frame.Restoration = new()
        {
            GaborEnabled = effort >= 5 && distance > .5f && decodingSpeed < 4,
            EpfIterations = decodingSpeed >= 3 ? 0U : (uint)Math.Max(0, (distance >= 4 ? 3 : distance >= 1.5f ? 2 : distance >= .7f ? 1 : 0) - (decodingSpeed == 2 ? 1 : 0)),
        };
        if (!float.IsFinite(photonNoiseIso) || photonNoiseIso < 0) { throw new ArgumentOutOfRangeException(nameof(photonNoiseIso)); }
        float[] estimatedNoise = noiseModeling && photonNoiseIso == 0 && features?.HasNoise != true && frame.FrameType == 0
            ? JxlNoiseEstimator.Estimate(xyb, distance, cancellationToken) : null;
        if (photonNoiseIso != 0) { frame.Flags |= 1; }
        int passCount = progressive ? 3 : 1;
        if (progressive) { frame.PassCount = 3; frame.PassShifts = [0, 0]; frame.Downsample = [4, 2]; frame.LastPass = [0, 1]; }
        int groups = checked((int)frame.GroupCount), lfGroups = checked((int)frame.LfGroupCount);
        if (2L + lfGroups + groups * passCount > 65536) { throw new ArgumentException("JPEG XL frame has too many groups.", nameof(frame)); }
        bool canSearchPatches = features == null && frame.FrameType == 0 && !frame.HaveCrop && frame.Upsampling == 1;
        JxlLossyPatchEncoder patches = automaticPatches && canSearchPatches
            ? JxlLossyPatchEncoder.Find(xyb, cancellationToken) : null;
        patches ??= automaticDots && canSearchPatches ? JxlLossyPatchEncoder.FindDots(xyb, cancellationToken) : null;
        if (patches != null)
        {
            features = patches.WriteReference(output, image, frame, distance, effort, allocator, cancellationToken);
            patches.Apply(xyb, true, cancellationToken);
        }
        features = JxlFrameFeatures.WithNoise(features, estimatedNoise);
        // The no-Gaborish path needs a finer initial quantization field.
        // Global scale and transform selection still use the requested distance.
        float adaptiveDistance = frame.Restoration.GaborEnabled ? distance : distance * .62f;
        using JxlAdaptiveQuantization adaptive = effort >= 5 ? new(xyb, adaptiveDistance, cancellationToken) : null;
        if (frame.Restoration.GaborEnabled) { JxlInverseGabor.Apply(xyb, cancellationToken); }
        using JxlVarDctPreparation prepared = effort >= 8 && linearReference != null
            ? JxlPerceptualQuantizer.Prepare(image, frame, xyb, linearReference, adaptive, distance, effort, metricIntensity, allocator, cancellationToken, transform, decodingSpeed, matrices, patches)
            : new(image, frame, xyb, distance, allocator, cancellationToken, transform, effort, adaptive, decodingSpeed: decodingSpeed, matrices: matrices);
        if (progressiveDc && !frame.UseLfFrame) { JxlLowFrequencyEncoder.Write(output, image, frame, prepared, effort, allocator, cancellationToken); }
        WritePreparedFrame(output, image, frame, prepared, extras, distance, effort, allocator, cancellationToken, photonNoiseIso, decodingSpeed, features);
    }

    internal static void WritePreparedFrame(JxlBitWriter output, JxlImageHeader image, JxlFrameHeader frame, JxlVarDctPreparation prepared,
        JxlModularPlane[] extras, float distance, int effort, MemoryAllocator allocator, CancellationToken cancellationToken, float photonNoiseIso = 0, int decodingSpeed = 0, JxlFrameFeatures features = null)
    {
        frame.Flags |= features?.Flags ?? 0;
        int width = (int)frame.SampleWidth, height = (int)frame.SampleHeight;
        int groups = checked((int)frame.GroupCount), lfGroups = checked((int)frame.LfGroupCount), passCount = checked((int)frame.PassCount);
        JxlCoefficientOrder order = new(prepared, effort, cancellationToken);
        JxlBlockContextModel contexts = new(prepared.Blocks, distance, effort, cancellationToken, decodingSpeed);
        int columns = (width + 255) / 256, lfColumns = (width + 2047) / 2048;
        int globalExtras = 0;
        foreach (JxlModularPlane extra in extras) { if (extra.Width > 256 || extra.Height > 256) { break; } globalExtras++; }
        JxlModularImage extraLayout = new() { Channels = new(extras), GlobalChannelCount = globalExtras };
        (byte[] Data, long Bits)[] sections = new (byte[], long)[2 + lfGroups + groups * passCount];
        sections[0] = Section(writer =>
        {
            features?.Write(writer);
            if (photonNoiseIso != 0 && features?.HasNoise != true) { JxlPhotonNoise.Write(writer, (int)frame.Width, (int)frame.Height, photonNoiseIso); }
            writer.WriteBool(true); // Default LF dequantization weights.
            writer.WriteU32(prepared.GlobalScale, U(11, 1), U(11, 2049), U(12, 4097), U(16, 8193));
            writer.WriteU32(prepared.QuantLf, 16, U(5, 1), U(8, 1), U(16, 1));
            contexts.Write(writer);
            writer.WriteBool(true); writer.WriteBool(false); // Default correlation; local Modular trees.
            if (extras.Length != 0) { Modular(writer, extras[..globalExtras]); }
        });
        // LF groups, AC token groups and their sections are independent; each
        // item writes only its own array slot, so the output order is unchanged.
        JxlParallel.ForEncoder(lfGroups, cancellationToken, group =>
        {
            int left = group % lfColumns * 2048, top = group / lfColumns * 2048;
            int w = Math.Min(2048, width - left), h = Math.Min(2048, height - top), bw = (w + 7) / 8, bh = (h + 7) / 8;
            using JxlModularWorkspace workspace = JxlModularWorkspace.ForEncoding(allocator, Math.Max(262144, (long)w * h));
            sections[1 + group] = Section(writer =>
            {
                if (!frame.UseLfFrame)
                {
                    writer.WriteBits(2, 0); // LF precision.
                    JxlModularPlane[] dc = new JxlModularPlane[3];
                    for (int order = 0; order < 3; order++)
                    { int c = order == 2 ? 2 : 1 - order; dc[order] = prepared.LowFrequency[c].View(left / 8, top / 8, bw, bh); }
                    Modular(writer, dc);
                }
                JxlModularPlane[] lfExtras = JxlModularFrame.SelectGroup(extraLayout, frame, group, true, 0).ToArray();
                if (lfExtras.Length != 0) { Modular(writer, lfExtras); }
                int count = 0;
                for (int y = 0; y < bh; y++)
                { foreach (JxlVarBlock block in prepared.Blocks.DangerousGetRowSpan(top / 8 + y).Slice(left / 8, bw)) { if (block.Multiplier > 0) { count++; } } }
                writer.WriteBits(JxlIntegerConfig.ContextBits((uint)(bw * bh - 1)), (uint)count - 1);
                JxlModularPlane[] metadata = [prepared.XFromY.View(left / 64, top / 64, (w + 63) / 64, (h + 63) / 64), prepared.BFromY.View(left / 64, top / 64, (w + 63) / 64, (h + 63) / 64),
                    workspace.Allocate(count, 2), workspace.Allocate(bw, bh)];
                if (frame.Restoration.EpfIterations != 0)
                { for (int y = 0; y < bh; y++) { metadata[3].Row(y).Fill(4); } }
                int index = 0;
                for (int y = 0; y < bh; y++)
                {
                    foreach (JxlVarBlock block in prepared.Blocks.DangerousGetRowSpan(top / 8 + y).Slice(left / 8, bw))
                    {
                        if (block.Multiplier <= 0) { continue; }
                        metadata[2].Set(index, 0, (int)block.Transform); metadata[2].Set(index++, 1, block.Multiplier - 1);
                    }
                }
                Modular(writer, metadata);
            });
        });
        JxlToken[][] tokens = new JxlToken[groups * passCount][];
        JxlEntropyEncoder[] entropy = new JxlEntropyEncoder[passCount];
        for (int pass = 0; pass < passCount; pass++)
        {
            int lower = passCount == 1 || pass == 0 ? 1 : pass == 1 ? 2 : 3;
            int upper = passCount == 1 || pass == 2 ? 8 : pass == 0 ? 2 : 3;
            int passIndex = pass;
            JxlParallel.ForEncoder(groups, cancellationToken, group =>
            {
                int index = passIndex * groups + group;
                tokens[index] = CoefficientTokens(prepared, order, contexts, group % columns * 256, group / columns * 256,
                    Math.Min(256, width - group % columns * 256), Math.Min(256, height - group / columns * 256), lower, upper, cancellationToken);
            });
            entropy[pass] = JxlEntropyEncoder.CreateOptimized(new JxlTokenSequence(tokens.AsSpan(pass * groups, groups)),
                contexts.Count * 495, false, effort, false, cancellationToken, decodingSpeed > 0 ? 6 : 256);
        }
        sections[1 + lfGroups] = Section(writer =>
        {
            prepared.Matrices.Write(writer, allocator, Modular); writer.WriteBits(JxlIntegerConfig.ContextBits((uint)groups - 1), 0);
            foreach (JxlEntropyEncoder model in entropy) { order.Write(writer, effort, cancellationToken); model.WriteHeader(writer); }
        });
        JxlParallel.ForEncoder(groups * passCount, cancellationToken, index =>
        {
            sections[2 + lfGroups + index] = Section(writer =>
            {
                entropy[index / groups].WriteTokens(writer, tokens[index], cancellationToken);
                JxlModularPlane[] views = JxlModularFrame.SelectGroup(extraLayout, frame, index % groups, false, index / groups).ToArray();
                if (views.Length != 0) { Modular(writer, views); }
            });
        });
        if (groups == 1 && passCount == 1)
        { sections = [Section(writer => { foreach (var section in sections) { writer.WriteBitstream(section.Data, section.Bits); } })]; }
        frame.Write(output, image); uint[] sizes = new uint[sections.Length];
        for (int i = 0; i < sizes.Length; i++) { sizes[i] = checked((uint)sections[i].Data.Length); }
        JxlFrameToc.WriteOrdered(output, frame, sizes);
        foreach (var section in sections) { cancellationToken.ThrowIfCancellationRequested(); output.WriteBytes(section.Data); }

        void Modular(JxlBitWriter writer, JxlModularPlane[] planes) =>
            JxlModularEncoder.WriteStreamCore(writer, planes, 0, null, null, effort == 1, effort, allocator, cancellationToken, (int)image.BitDepth.Bits,
                search: JxlModularSearchOptions.ForDecodingSpeed(null, decodingSpeed, effort, 1));
    }

    private static JxlToken[] CoefficientTokens(JxlVarDctPreparation prepared, JxlCoefficientOrder pass, JxlBlockContextModel contexts, int left, int top, int width, int height,
        int lowerFrequency, int upperFrequency, CancellationToken cancellationToken)
    {
        int bw = (width + 7) / 8, bh = (height + 7) / 8;
        uint[][] rows = [new uint[bw], new uint[bw], new uint[bw]];
        List<JxlToken> tokens = [];
        for (int y = 0; y < bh; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = 0; x < bw; x++)
            {
                JxlVarBlock block = prepared.Blocks.DangerousGetRowSpan(top / 8 + y)[left / 8 + x];
                if (block.Multiplier <= 0) { continue; }
                (int dw, int dh) = block.Transform.Blocks(); int blocks = dw * dh, log = BitOperations.Log2((uint)blocks);
                for (int channel = 0; channel < 3; channel++)
                {
                    int c = channel == 2 ? 2 : 1 - channel;
                    (ushort X, ushort Y)[] order = pass.Order(block.Transform.OrderIndex(), c);
                    int nonzero = 0;
                    for (int i = blocks; i < order.Length; i++) { if (Value(i) != 0) { nonzero++; } }
                    uint[] row = rows[c]; uint predicted = y == 0 ? x == 0 ? 32 : row[x - 1] : x == 0 ? row[x] : (row[x] + row[x - 1] + 1) >> 1;
                    int context = (int)(predicted >= 8 ? 4 + predicted / 2 : predicted), blockContext = contexts.Context(channel, block);
                    tokens.Add(new(blockContext + context * contexts.Count, (uint)nonzero)); row.AsSpan(x, dw).Fill((uint)(nonzero + blocks - 1) >> log);
                    int previous = nonzero <= blocks * 4 ? 1 : 0;
                    for (int i = blocks; nonzero != 0 && i < order.Length; i++)
                    {
                        int frequency = (i - blocks) >> log, remaining = (nonzero - 1) >> log;
                        context = 37 * contexts.Count + blockContext * 458 + (JxlHfCoefficients.NonzeroContexts[remaining] + JxlHfCoefficients.FrequencyContexts[frequency]) * 2 + previous;
                        int value = Value(i); tokens.Add(new(context, JxlBitWriter.PackSigned(value)));
                        previous = value == 0 ? 0 : 1; nonzero -= previous;
                    }

                    int Value(int index)
                    {
                        (int dx, int dy) = order[index]; if (block.Transform.Transpose()) { (dx, dy) = (dy, dx); }
                        if (dx >= dw * upperFrequency || dy >= dh * upperFrequency || (dx < dw * lowerFrequency && dy < dh * lowerFrequency)) { return 0; }
                        return prepared.Coefficients[c].Get(left + x * 8 + dx, top + y * 8 + dy);
                    }
                }
            }
        }
        return tokens.ToArray();
    }

    private static (byte[] Data, long Bits) Section(Action<JxlBitWriter> write)
    {
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream); write(writer);
        long bits = writer.Position; writer.ZeroPadToByte(); return (stream.ToArray(), bits);
    }
}
