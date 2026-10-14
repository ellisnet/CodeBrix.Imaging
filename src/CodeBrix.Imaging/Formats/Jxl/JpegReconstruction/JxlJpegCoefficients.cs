// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// JPEG coefficient extraction adapted from jxl-oxide crates/jxl-jbr/src/
// reconstruct.rs and reconstruct/scan.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).
// Integer chroma-from-luma derives through that source from libjxl
// lib/jxl/dec_group.cc, ef67fde2ec16d52e644c5a0969230b9a85e3eb31
// (BSD-3-Clause plus PATENTS).

using System;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Formats.Jxl.VarDct;

namespace CodeBrix.Imaging.Formats.Jxl.JpegReconstruction;

// Coefficient planes borrow their decoder workspace and cannot outlive it.
internal sealed class JxlJpegCoefficients
{
    internal JxlImageHeader Image;
    internal JxlFrameHeader Frame;
    internal JxlModularPlane[] Planes;
    internal int[][] Quantization;

    internal int ComponentPlane(int component) => this.Frame.YCbCr && component < 2 ? 1 - component : component;

    internal static JxlJpegCoefficients Create(JxlImageHeader image, JxlFrameHeader frame, JxlLfGlobal global,
        JxlDequantMatrices matrices, JxlModularPlane[][] lfQuant, JxlHfMetadata[] metadata,
        JxlModularPlane[] coefficients, CancellationToken cancellationToken)
    {
        int[][] quantization = matrices.JpegQuantization;
        if (quantization == null) { JxlThrowHelper.Invalid("JPEG reconstruction requires JPEG quantization matrices."); }
        foreach (int[] channel in quantization)
        {
            foreach (int value in channel)
            { if (value is < 1 or > 65535) { JxlThrowHelper.Invalid("invalid JPEG quantization value."); } }
        }
        bool subsampled = Array.Exists(frame.JpegUpsampling, v => v != 0);
        if (!subsampled && (global.ColorFactor != 84 || global.BaseCorrelationX != 0 || global.BaseCorrelationB != 0))
        { JxlThrowHelper.Invalid("JPEG reconstruction requires integer chroma correlation."); }
        foreach (JxlHfMetadata hf in metadata)
        {
            for (int y = 0; y < hf.Blocks.Height; y++)
            {
                foreach (JxlVarBlock block in hf.Blocks.DangerousGetRowSpan(y))
                { if (block.Transform != JxlTransformType.Dct8 || block.Multiplier <= 0) { JxlThrowHelper.Invalid("JPEG reconstruction requires 8x8 DCT blocks."); } }
            }
        }
        int lfDimension = (int)frame.GroupDimension * 8;
        int lfColumns = ((int)frame.SampleWidth + lfDimension - 1) / lfDimension;
        if (image.Color.ColorSpace != 1 && !subsampled)
        {
            Span<int> ratios = stackalloc int[64];
            foreach (int c in new[] { 0, 2 })
            {
                for (int i = 0; i < 64; i++) { ratios[i] = 2048 * quantization[1][i] / quantization[c][i]; }
                for (int y = 0; y < coefficients[c].Height; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Span<int> target = coefficients[c].Row(y), luma = coefficients[1].Row(y);
                    for (int x = 0; x < target.Length; x++)
                    {
                        int group = (y / lfDimension * lfColumns) + (x / lfDimension);
                        JxlModularPlane factors = c == 0 ? metadata[group].XFromY : metadata[group].BFromY;
                        int factor = factors.Get((x % lfDimension) / 64, (y % lfDimension) / 64);
                        int scale = unchecked(factor * 2048) / 84;
                        int qScale = unchecked((ratios[((x & 7) * 8) + (y & 7)] * scale) + 1024) >> 11;
                        int correlated = unchecked((luma[x] * qScale) + 1024) >> 11;
                        target[x] = unchecked(target[x] + correlated);
                    }
                }
            }
        }
        bool horizontal = Array.Exists(frame.JpegUpsampling, v => v is 1 or 2);
        bool vertical = Array.Exists(frame.JpegUpsampling, v => v is 1 or 3);
        for (int c = 0; c < 3; c++)
        {
            int hs = horizontal && frame.JpegUpsampling[c] is 0 or 3 ? 1 : 0;
            int vs = vertical && frame.JpegUpsampling[c] is 0 or 2 ? 1 : 0;
            int groupBlocksX = lfDimension >> (3 + hs), groupBlocksY = lfDimension >> (3 + vs);
            int offset = frame.YCbCr ? 0 : 1024 / quantization[c][0];
            for (int by = 0; by < coefficients[c].Height / 8; by++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Span<int> output = coefficients[c].Row(by * 8);
                for (int bx = 0; bx < coefficients[c].Width / 8; bx++)
                {
                    int group = (by / groupBlocksY * lfColumns) + (bx / groupBlocksX);
                    JxlModularPlane dc = lfQuant[group][c];
                    int x = bx % groupBlocksX, y = by % groupBlocksY;
                    if (x >= dc.Width || y >= dc.Height) { JxlThrowHelper.Invalid("JPEG DC grid does not cover the coefficient plane."); }
                    output[bx * 8] = (int)Math.Clamp((long)dc.Get(x, y) - offset, -2047, 2047);
                }
            }
        }
        return new() { Image = image, Frame = frame, Planes = coefficients, Quantization = quantization };
    }
}
