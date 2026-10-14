// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-frame/src/data/{lf_global,lf_group,
// hf_global,pass_group}.rs and crates/jxl-render/src/vardct/mod.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Coding;
using CodeBrix.Imaging.Formats.Jxl.Color;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Formats.Jxl.JpegReconstruction;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.VarDct;

internal sealed partial class JxlVarDctFrame : IDisposable
{
    internal JxlPatches Patches;
    internal JxlModularFrame RawSources;
    internal JxlNoise Noise;
    internal JxlSplines Splines;
    internal float CorrelationX, CorrelationB;
    internal Buffer2D<float> Sigma;
    internal JxlJpegCoefficients JpegCoefficients;
    private JxlFloatFrame rendered;
    internal JxlFloatFrame TakeFrame() { JxlFloatFrame value = this.rendered; this.rendered = null; return value; }

    internal static JxlVarDctFrame Read(JxlBitReader input, JxlImageHeader image, JxlFrameHeader frame, JxlFrameToc toc,
        JxlModularWorkspace workspace, CancellationToken cancellationToken, JxlFloatFrame lfReference = null, bool jpegReconstruction = false,
        int maxDegreeOfParallelism = 1, JxlDecodeBudget budget = null)
    {
        if (frame.UseLfFrame && lfReference == null) { JxlThrowHelper.Invalid("missing low-frequency reference frame."); }
        if (jpegReconstruction && (image.XybEncoded || frame.Modular || !frame.IsNormal || frame.UseLfFrame || (frame.Flags & 128) == 0))
        { JxlThrowHelper.Invalid("frame is incompatible with JPEG reconstruction."); }
        JxlFrameSections sections = new(input, toc, workspace.Allocator, cancellationToken);
        JxlBitReader globalReader = sections.Get(0);
        JxlVarDctFrame result = new();
        int lfCount = (int)frame.LfGroupCount, groupCount = (int)frame.GroupCount;
        JxlHfMetadata[] metadata = new JxlHfMetadata[lfCount];
        JxlModularPlane[][] lfQuant = new JxlModularPlane[lfCount][];
        int[] precision = new int[lfCount];
        try
        {
            if ((frame.Flags & 2) != 0) { result.Patches = JxlPatches.Read(globalReader, image, frame, workspace.Allocator, budget); }
            if ((frame.Flags & 16) != 0) { result.Splines = JxlSplines.Read(globalReader, frame, workspace.Allocator); }
            if ((frame.Flags & 1) != 0) { result.Noise = JxlNoise.Read(globalReader); }
            float[] lfDequant = [1f / 32, 1f / 4, 1f / 2];
            if (!globalReader.ReadBool())
            {
                for (int c = 0; c < 3; c++)
                {
                    lfDequant[c] = globalReader.ReadF16();
                    if (lfDequant[c] / 128 < 1E-8f) { JxlThrowHelper.Invalid("LF dequantization weight is too small."); }
                }
            }
            JxlLfGlobal global = JxlLfGlobal.Read(globalReader, workspace.Allocator);
            result.CorrelationX = global.BaseCorrelationX; result.CorrelationB = global.BaseCorrelationB;
            result.Splines?.ValidateArea(frame, result.CorrelationX, result.CorrelationB);
            long nodes = Math.Min(1 << 22, 1024 + ((long)frame.Width * frame.Height * (3 + image.ExtraChannels.Length) / 16));
            JxlMaTree tree = globalReader.ReadBool() ? JxlMaTree.Read(globalReader, workspace.Allocator, (int)nodes) : null;
            int width = (int)frame.SampleWidth, height = (int)frame.SampleHeight;
            List<JxlModularPlane> extras = [];
            foreach (JxlExtraChannel extra in image.ExtraChannels)
            {
                int shift = BitOperations.Log2(frame.ExtraUpsampling[extras.Count]) + (int)extra.DimShift - BitOperations.Log2(frame.Upsampling);
                extras.Add(workspace.Allocate((width + (1 << shift) - 1) >> shift, (height + (1 << shift) - 1) >> shift, shift, shift));
            }
            JxlModularImage modular = null;
            if (extras.Count != 0)
            {
                modular = JxlModularImage.Read(globalReader, extras, tree, workspace);
                modular.DecodeGlobal(globalReader, (int)frame.GroupDimension, workspace);
            }
            sections.Finish(0);
            int dimension = (int)frame.GroupDimension, lfDimension = dimension * 8;
            int lfColumns = (width + lfDimension - 1) / lfDimension, columns = (width + dimension - 1) / dimension;
            int[] hshift = new int[3], vshift = new int[3];
            GetShifts(frame, hshift, vshift);
            int parallelism = sections.Single ? 1 : maxDegreeOfParallelism;
            JxlParallel.For(lfCount, parallelism, cancellationToken, group =>
            {
                JxlBitReader reader = sections.Get(1 + group);
                int w = Math.Min(lfDimension, width - (group % lfColumns * lfDimension));
                int h = Math.Min(lfDimension, height - (group / lfColumns * lfDimension));
                if (!frame.UseLfFrame)
                {
                    precision[group] = (int)reader.ReadBits(2);
                    List<JxlModularPlane> planes = [];
                    for (int order = 0; order < 3; order++)
                    {
                        int c = order == 2 ? 2 : 1 - order;
                        planes.Add(workspace.Allocate(PaddedBlocks(w, hshift) >> hshift[c], PaddedBlocks(h, vshift) >> vshift[c], hshift[c], vshift[c]));
                    }
                    JxlModularFrame.DecodeGroup(reader, planes, tree, 1 + group, (int)image.BitDepth.Bits, workspace);
                    lfQuant[group] = [planes[1], planes[0], planes[2]];
                }
                JxlModularFrame.DecodeGroup(reader, JxlModularFrame.SelectGroup(modular, frame, group, true, 0), tree,
                    1 + lfCount + group, (int)image.BitDepth.Bits, workspace);
                metadata[group] = JxlHfMetadata.Read(reader, frame, group, w, h, (int)image.BitDepth.Bits, tree, global.GlobalScale, workspace);
                sections.Finish(1 + group);
            });
            JxlBitReader hfReader = sections.Get(1 + lfCount);
            JxlDequantMatrices matrices = JxlDequantMatrices.Read(hfReader, (int)image.BitDepth.Bits, lfCount, tree, workspace);
            uint presets = 1 + hfReader.ReadBits(JxlIntegerConfig.ContextBits((uint)groupCount - 1));
            if (presets > groupCount) { JxlThrowHelper.Invalid("too many HF presets."); }
            JxlHfPass[] passes = new JxlHfPass[frame.PassCount];
            for (int pass = 0; pass < passes.Length; pass++) { passes[pass] = JxlHfPass.Read(hfReader, global.BlockContext, presets, workspace.Allocator); }
            sections.Finish(1 + lfCount);
            JxlModularPlane[] coefficients = new JxlModularPlane[3];
            for (int c = 0; c < 3; c++)
            {
                int w = (PaddedBlocks(width, hshift) >> hshift[c]) * 8;
                int h = (PaddedBlocks(height, vshift) >> vshift[c]) * 8;
                coefficients[c] = workspace.Allocate(w, h);
            }
            for (int pass = 0; pass < passes.Length; pass++)
            {
                JxlParallel.For(groupCount, parallelism, cancellationToken, group =>
                {
                    int gx = group % columns, gy = group / columns, lfGroup = (gx / 8) + (gy / 8 * lfColumns);
                    int left = gx % 8 * dimension / 8, top = gy % 8 * dimension / 8;
                    JxlHfMetadata hf = metadata[lfGroup];
                    int bw = Math.Min(dimension / 8, hf.Blocks.Width - left), bh = Math.Min(dimension / 8, hf.Blocks.Height - top);
                    JxlModularPlane[] quantViews = frame.UseLfFrame ? null : new JxlModularPlane[3], coefficientViews = new JxlModularPlane[3];
                    for (int c = 0; c < 3; c++)
                    {
                        int w = (bw + (1 << hshift[c]) - 1) >> hshift[c], h = (bh + (1 << vshift[c]) - 1) >> vshift[c];
                        if (quantViews != null)
                        {
                            JxlModularPlane q = lfQuant[lfGroup][c];
                            int qx = left >> hshift[c], qy = top >> vshift[c];
                            quantViews[c] = q.View(qx, qy, Math.Min(w, q.Width - qx), Math.Min(h, q.Height - qy));
                        }
                        JxlModularPlane coeff = coefficients[c];
                        int cx = gx * dimension >> hshift[c], cy = gy * dimension >> vshift[c];
                        coefficientViews[c] = coeff.View(cx, cy, Math.Min(w * 8, coeff.Width - cx), Math.Min(h * 8, coeff.Height - cy));
                    }
                    int logical = 2 + lfCount + (pass * groupCount) + group;
                    JxlBitReader reader = sections.Get(logical);
                    JxlHfCoefficients.Read(reader, presets, global.BlockContext, passes[pass], hf, left, top, bw, bh,
                        frame.JpegUpsampling, quantViews, coefficientViews, pass < frame.PassShifts.Length ? (int)frame.PassShifts[pass] : 0);
                    JxlModularFrame.DecodeGroup(reader, JxlModularFrame.SelectGroup(modular, frame, group, false, pass), tree,
                        18 + (3 * lfCount) + (pass * groupCount) + group, (int)image.BitDepth.Bits, workspace);
                    sections.Finish(logical);
                });
            }
            if (sections.Single) { sections.Finish(0, true); }
            modular?.Finish((int)image.BitDepth.Bits, workspace);
            if (jpegReconstruction)
            {
                result.JpegCoefficients = JxlJpegCoefficients.Create(image, frame, global, matrices, lfQuant, metadata, coefficients, cancellationToken);
                return result;
            }
            result.RawSources = new() { Header = frame, Planes = new JxlModularPlane[3 + extras.Count] };
            for (int c = 0; c < extras.Count; c++) { result.RawSources.Planes[3 + c] = extras[c]; }
            result.rendered = Render(image, frame, global, lfDequant, matrices, lfQuant, precision, metadata, coefficients, extras,
                workspace.Allocator, cancellationToken, lfReference, maxDegreeOfParallelism);
            result.Sigma = workspace.Allocator.Allocate2D<float>((width + 7) / 8, (height + 7) / 8);
            for (int y = 0; y < result.Sigma.Height; y++)
            {
                Span<float> row = result.Sigma.DangerousGetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    int group = (y / (lfDimension / 8) * lfColumns) + (x / (lfDimension / 8));
                    row[x] = metadata[group].Sigma.DangerousGetRowSpan(y % (lfDimension / 8))[x % (lfDimension / 8)];
                }
            }
            return result;
        }
        catch { result.Dispose(); throw; }
        finally { foreach (JxlHfMetadata hf in metadata) { hf?.Dispose(); } }
    }

    private static int PaddedBlocks(int pixels, int[] shifts) => ((pixels + 7) / 8 + (Array.Exists(shifts, s => s != 0) ? 1 : 0)) & (Array.Exists(shifts, s => s != 0) ? ~1 : -1);

    private static void GetShifts(JxlFrameHeader frame, int[] horizontalShifts, int[] verticalShifts)
    {
        bool horizontal = Array.Exists(frame.JpegUpsampling, j => j is 1 or 2), vertical = Array.Exists(frame.JpegUpsampling, j => j is 1 or 3);
        for (int c = 0; c < 3; c++)
        {
            horizontalShifts[c] = horizontal && frame.JpegUpsampling[c] is 0 or 3 ? 1 : 0;
            verticalShifts[c] = vertical && frame.JpegUpsampling[c] is 0 or 2 ? 1 : 0;
        }
    }

    public void Dispose() { this.rendered?.Dispose(); this.Sigma?.Dispose(); }
}
