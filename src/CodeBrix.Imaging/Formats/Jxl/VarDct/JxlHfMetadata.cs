// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-vardct/src/hf_metadata.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).
// Subsampled transform limits follow libjxl lib/jxl/dec_modular.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Copyright (c) the JPEG XL Project Authors. All rights reserved.

using System;
using System.Collections.Generic;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Coding;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.VarDct;

internal readonly record struct JxlVarBlock(JxlTransformType Transform, int Multiplier);

internal sealed class JxlHfMetadata : IDisposable
{
    internal JxlModularPlane XFromY, BFromY;
    internal Buffer2D<JxlVarBlock> Blocks;
    internal Buffer2D<float> Sigma;

    internal static JxlHfMetadata Read(JxlBitReader reader, JxlFrameHeader frame, int group, int width, int height,
        int bitDepth, JxlMaTree tree, uint globalScale, JxlModularWorkspace workspace)
    {
        int bw = (width + 7) / 8, bh = (height + 7) / 8;
        bool subsampled = Array.Exists(frame.JpegUpsampling, j => j != frame.JpegUpsampling[0]);
        if (Array.Exists(frame.JpegUpsampling, j => j is 1 or 2)) { bw = (bw + 1) & ~1; }
        if (Array.Exists(frame.JpegUpsampling, j => j is 1 or 3)) { bh = (bh + 1) & ~1; }
        uint count = 1 + reader.ReadBits(JxlIntegerConfig.ContextBits((uint)(bw * bh - 1)));
        if (count > bw * bh) { JxlThrowHelper.Invalid("too many VarDCT blocks in the LF group."); }
        List<JxlModularPlane> planes =
        [
            workspace.Allocate((width + 63) / 64, (height + 63) / 64),
            workspace.Allocate((width + 63) / 64, (height + 63) / 64),
            workspace.Allocate((int)count, 2), workspace.Allocate(bw, bh),
        ];
        JxlModularImage modular = JxlModularImage.Read(reader, planes, tree, workspace);
        modular.DecodeAll(reader, 1 + (2 * (int)frame.LfGroupCount) + group, workspace);
        modular.Finish(bitDepth, workspace);
        JxlHfMetadata result = new() { XFromY = planes[0], BFromY = planes[1] };
        try
        {
            result.Blocks = workspace.Allocator.Allocate2D<JxlVarBlock>(bw, bh, AllocationOptions.Clean);
            result.Sigma = workspace.Allocator.Allocate2D<float>(bw, bh, AllocationOptions.Clean);
            int block = 0;
            for (int y = 0; y < bh; y++)
            {
                reader.CheckCancellation();
                for (int x = 0; x < bw; x++)
                {
                    if (result.Blocks.DangerousGetRowSpan(y)[x].Multiplier != 0) { continue; }
                    if (block >= count) { JxlThrowHelper.Invalid("VarDCT block list does not fill the LF group."); }
                    JxlTransformType type = JxlTransformInfo.Parse(planes[2].Get(block, 0));
                    int multiplier = unchecked(planes[2].Get(block, 1) + 1);
                    if (multiplier <= 0) { JxlThrowHelper.Invalid("nonpositive VarDCT multiplier."); }
                    (int dw, int dh) = type.Blocks();
                    if (subsampled && (dw > 1 || dh > 1))
                    { JxlThrowHelper.Invalid("VarDCT transform is incompatible with chroma subsampling."); }
                    if ((x % 32) + dw > 32 || (y % 32) + dh > 32 || x + dw > bw || y + dh > bh)
                    { JxlThrowHelper.Invalid("VarDCT block crosses a group boundary."); }
                    float sigma = frame.Restoration.QuantMultiplier * 65536f / globalScale / multiplier;
                    for (int dy = 0; dy < dh; dy++)
                    {
                        Span<JxlVarBlock> row = result.Blocks.DangerousGetRowSpan(y + dy);
                        for (int dx = 0; dx < dw; dx++)
                        {
                            if (row[x + dx].Multiplier != 0) { JxlThrowHelper.Invalid("VarDCT blocks overlap."); }
                            row[x + dx] = dx == 0 && dy == 0 ? new(type, multiplier) : new(default, -1);
                            if (frame.Restoration.EpfIterations != 0)
                            {
                                int sharpness = planes[3].Get(x + dx, y + dy);
                                if ((uint)sharpness >= 8) { JxlThrowHelper.Invalid("invalid EPF sharpness."); }
                                result.Sigma.DangerousGetRowSpan(y + dy)[x + dx] = sigma * frame.Restoration.Sharpness[sharpness];
                            }
                        }
                    }
                    block++;
                    x += dw - 1;
                }
            }
            if (block != count) { JxlThrowHelper.Invalid("unused VarDCT block descriptors."); }
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    public void Dispose() { this.Blocks?.Dispose(); this.Sigma?.Dispose(); }
}
