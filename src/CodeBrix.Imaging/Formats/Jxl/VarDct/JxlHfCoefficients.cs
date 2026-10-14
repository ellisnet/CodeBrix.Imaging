// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-vardct/src/hf_coeff.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

using System;
using System.Numerics;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Coding;
using CodeBrix.Imaging.Formats.Jxl.Modular;

namespace CodeBrix.Imaging.Formats.Jxl.VarDct;

internal static class JxlHfCoefficients
{
    internal static ReadOnlySpan<int> FrequencyContexts =>
        [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 15, 16, 16, 17, 17, 18, 18, 19, 19,
         20, 20, 21, 21, 22, 22, 23, 23, 23, 23, 24, 24, 24, 24, 25, 25, 25, 25, 26, 26, 26, 26, 27,
         27, 27, 27, 28, 28, 28, 28, 29, 29, 29, 29, 30, 30, 30, 30];
    internal static ReadOnlySpan<int> NonzeroContexts =>
        [0, 31, 62, 62, 93, 93, 93, 93, 123, 123, 123, 123, 152, 152, 152, 152, 152, 152, 152, 152,
         180, 180, 180, 180, 180, 180, 180, 180, 180, 180, 180, 180, 206, 206, 206, 206, 206, 206,
         206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206, 206,
         206, 206, 206, 206, 206, 206, 206];

    // Quantized LF and coefficient planes are views covering the current pass group.
    internal static void Read(JxlBitReader reader, uint presets, JxlHfBlockContext contexts, JxlHfPass pass,
        JxlHfMetadata metadata, int left, int top, int width, int height, uint[] jpegUpsampling,
        JxlModularPlane[] lfQuant, JxlModularPlane[] coefficients, int coefficientShift)
    {
        using JxlEntropyDecoder decoder = pass.Distributions.CreateStream();
        int lfMultiplier = (contexts.LfThresholds[0].Length + 1) * (contexts.LfThresholds[1].Length + 1) * (contexts.LfThresholds[2].Length + 1);
        int hfMultiplier = contexts.QuantThresholds.Length + 1;
        Span<int> hshifts = stackalloc int[3], vshifts = stackalloc int[3];
        bool horizontal = Array.Exists(jpegUpsampling, j => j is 1 or 2), vertical = Array.Exists(jpegUpsampling, j => j is 1 or 3);
        uint[][] nonzeroRows = new uint[3][];
        for (int c = 0; c < 3; c++)
        {
            hshifts[c] = horizontal && jpegUpsampling[c] is 0 or 3 ? 1 : 0;
            vshifts[c] = vertical && jpegUpsampling[c] is 0 or 2 ? 1 : 0;
            nonzeroRows[c] = new uint[(width + (1 << hshifts[c]) - 1) >> hshifts[c]];
        }
        uint preset = reader.ReadBits(JxlIntegerConfig.ContextBits(presets - 1));
        if (preset >= presets) { JxlThrowHelper.Invalid("HF preset index is out of range."); }
        ReadOnlySpan<byte> map = decoder.Clusters.AsSpan(checked((int)preset * 495 * contexts.ClusterCount), 495 * contexts.ClusterCount);
        decoder.Begin(reader);
        for (int y = 0; y < height; y++)
        {
            reader.CheckCancellation();
            for (int x = 0; x < width; x++)
            {
                JxlVarBlock block = metadata.Blocks.DangerousGetRowSpan(top + y)[left + x];
                if (block.Multiplier <= 0) { continue; }
                (int w8, int h8) = block.Transform.Blocks();
                int blocks = w8 * h8, logBlocks = BitOperations.Log2((uint)blocks), orderIndex = block.Transform.OrderIndex();
                int lfIndex = 0;
                if (lfQuant != null)
                {
                    ReadOnlySpan<int> lfOrder = [0, 2, 1];
                    foreach (int c in lfOrder)
                    {
                        lfIndex *= contexts.LfThresholds[c].Length + 1;
                        int q = lfQuant[c].Get(x >> hshifts[c], y >> vshifts[c]);
                        foreach (int threshold in contexts.LfThresholds[c]) { if (q > threshold) { lfIndex++; } }
                    }
                }
                int hfIndex = 0;
                foreach (uint threshold in contexts.QuantThresholds) { if (block.Multiplier > threshold) { hfIndex++; } }
                for (int channelOrder = 0; channelOrder < 3; channelOrder++)
                {
                    int channelIndex = (channelOrder * 13) + orderIndex;
                    int c = channelOrder == 2 ? 2 : 1 - channelOrder;
                    int sx = x >> hshifts[c], sy = y >> vshifts[c];
                    if (hshifts[c] != 0 || vshifts[c] != 0)
                    {
                        if ((sx << hshifts[c]) != x || (sy << vshifts[c]) != y ||
                            metadata.Blocks.DangerousGetRowSpan(top + sy)[left + sx].Multiplier <= 0) { continue; }
                    }
                    int blockContext = contexts.Map[(((channelIndex * hfMultiplier) + hfIndex) * lfMultiplier) + lfIndex];
                    uint[] row = nonzeroRows[c];
                    if (sx + w8 > row.Length || sx * 8 > coefficients[c].Width - w8 * 8 || sy * 8 > coefficients[c].Height - h8 * 8)
                    { JxlThrowHelper.Invalid("subsampled VarDCT block exceeds the coefficient group."); }
                    uint predicted = sy == 0 ? sx == 0 ? 32 : row[sx - 1]
                        : sx == 0 ? row[sx] : (row[sx] + row[sx - 1] + 1) >> 1;
                    uint predictedContext = predicted >= 8 ? 4 + (predicted / 2) : predicted;
                    int context = blockContext + ((int)predictedContext * contexts.ClusterCount);
                    uint nonzero = decoder.ReadClustered(reader, map[context]);
                    if (nonzero > 63 * blocks) { JxlThrowHelper.Invalid("too many nonzero HF coefficients."); }
                    uint rowValue = (nonzero + (uint)blocks - 1) >> logBlocks;
                    row.AsSpan(sx, w8).Fill(rowValue);
                    if (nonzero == 0) { continue; }
                    int previousNonzero = nonzero <= blocks * 4 ? 1 : 0;
                    (ushort X, ushort Y)[] order = pass.Order(orderIndex, c);
                    ReadOnlySpan<byte> clusters = map.Slice((blockContext * 458) + (37 * contexts.ClusterCount), 458);
                    for (int i = blocks; i < order.Length; i++)
                    {
                        int frequency = (i - blocks) >> logBlocks;
                        int remaining = (int)((nonzero - 1) >> logBlocks);
                        int coefficientContext = ((NonzeroContexts[remaining] + FrequencyContexts[frequency]) * 2) + previousNonzero;
                        if (coefficientContext >= clusters.Length) { JxlThrowHelper.Invalid("too many zero HF coefficients."); }
                        uint value = decoder.ReadClustered(reader, clusters[coefficientContext]);
                        if (value == 0) { previousNonzero = 0; continue; }
                        int coefficient = unchecked(JxlBitReader.UnpackSigned(value) << coefficientShift);
                        (int dx, int dy) = order[i];
                        if (block.Transform.Transpose()) { (dx, dy) = (dy, dx); }
                        int ox = (sx * 8) + dx, oy = (sy * 8) + dy;
                        coefficients[c].Set(ox, oy, unchecked(coefficients[c].Get(ox, oy) + coefficient));
                        previousNonzero = 1;
                        if (--nonzero == 0) { break; }
                    }
                    if (nonzero != 0) { JxlThrowHelper.Invalid("HF coefficient count exceeds transform size."); }
                }
            }
        }
        decoder.Finish();
    }
}
