// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-modular/src/transform.rs and
// transform/palette.rs, 0.12.6, f8ae722ef2d6b782941c89517d19cfbf605c4a9d.
// Apache-2.0 option.
// Implicit extra-channel values checked against libjxl modular/transform/palette.h,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Copyright (c) the JPEG XL Project Authors. All rights reserved.

using System;
using System.Collections.Generic;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using static CodeBrix.Imaging.Formats.Jxl.Bitstream.JxlU32;

namespace CodeBrix.Imaging.Formats.Jxl.Modular;

internal sealed partial class JxlPaletteTransform : JxlModularTransform
{
    private int begin, count, colors, deltas, predictor;
    private JxlWeightedPredictorHeader weighted;
    private JxlModularPlane[] originals;

    internal static JxlPaletteTransform ReadPalette(JxlBitReader reader, JxlWeightedPredictorHeader weighted)
    {
        JxlPaletteTransform transform = new()
        {
            begin = ReadChannel(reader), count = (int)reader.ReadU32(1, 3, 4, U(13, 1)),
            colors = (int)reader.ReadU32(U(8), U(10, 256), U(12, 1280), U(16, 5376)),
            deltas = (int)reader.ReadU32(0, U(8, 1), U(10, 257), U(16, 1281)),
            predictor = (int)reader.ReadBits(4), weighted = weighted,
        };
        if (transform.predictor > 13) { JxlThrowHelper.Invalid("invalid palette delta predictor."); }
        return transform;
    }

    internal override void Prepare(List<JxlModularPlane> channels, ref int metaChannels, JxlModularWorkspace workspace)
    {
        ValidateRange(channels, this.begin, this.count);
        if (this.begin < metaChannels)
        {
            if (this.begin + this.count > metaChannels) { JxlThrowHelper.Invalid("palette crosses metadata channel boundary."); }
            metaChannels += 2 - this.count;
        }
        else { metaChannels++; }
        this.originals = channels.GetRange(this.begin, this.count).ToArray();
        JxlModularPlane first = this.originals[0];
        JxlModularPlane index = workspace.Allocate(first.Width, first.Height, first.HorizontalShift, first.VerticalShift);
        index.OriginalWidth = first.OriginalWidth; index.OriginalHeight = first.OriginalHeight;
        channels.RemoveRange(this.begin, this.count);
        channels.Insert(this.begin, index);
        channels.Insert(0, workspace.Allocate(this.colors, this.count, -1, -1));
    }

    internal override void Undo(List<JxlModularPlane> channels, int bitDepth, JxlModularWorkspace workspace)
    {
        JxlModularPlane palette = channels[0];
        channels.RemoveAt(0);
        JxlModularPlane indices = channels[this.begin];
        long maximum = (1L << bitDepth) - 1;
        for (int c = 0; c < this.count; c++)
        {
            JxlModularPlane target = this.originals[c];
            using JxlPredictor prediction = new(target, [], this.predictor == 6 ? this.weighted : null, workspace.Allocator);
            ReadOnlySpan<int> entries = palette.Row(c);
            for (int y = 0; y < target.Height; y++)
            {
                Span<int> row = indices.Row(y);
                Span<int> output = target.Row(y);
                ReadOnlySpan<int> above = y == 0 ? default : target.Row(y - 1);
                ReadOnlySpan<int> above2 = y < 2 ? default : target.Row(y - 2);
                for (int x = 0; x < target.Width; x++)
                {
                    int index = row[x], value;
                    if (index >= 0 && index < this.colors) { value = entries[index]; }
                    else if (c >= 3) { value = 0; }
                    else if (index >= this.colors)
                    {
                        int implicitIndex = index - this.colors;
                        if (implicitIndex < 64)
                        {
                            long digit = c >= 16 ? 0 : (implicitIndex >> (2 * c)) & 3;
                            value = unchecked((int)(((digit * maximum) / 4) + (1L << Math.Max(0, bitDepth - 3))));
                        }
                        else
                        {
                            implicitIndex -= 64;
                            for (int i = 0; i < c; i++) { implicitIndex /= 5; }
                            value = unchecked((int)(((implicitIndex % 5) * maximum) / 4));
                        }
                    }
                    else
                    {
                        int deltaIndex = -(index + 1) % 143;
                        value = DeltaPalette[(((deltaIndex + 1) >> 1) * 3) + c];
                        if ((deltaIndex & 1) == 0) { value = -value; }
                        if (bitDepth > 8) { value <<= Math.Min(bitDepth, 24) - 8; }
                    }
                    // Only the weighted predictor carries state across samples. Other
                    // predictors can read their neighbors when a delta actually needs them.
                    if (this.predictor == 6 || (this.predictor != 0 && index < this.deltas))
                    {
                        prediction.Prepare(x, y, output, above, above2);
                        if (index < this.deltas) { value = unchecked(value + prediction.Predict(this.predictor)); }
                        prediction.Record(value, output);
                    }
                    else { output[x] = value; }
                }
            }
        }
        channels.RemoveAt(this.begin);
        channels.InsertRange(this.begin, this.originals);
    }
}
