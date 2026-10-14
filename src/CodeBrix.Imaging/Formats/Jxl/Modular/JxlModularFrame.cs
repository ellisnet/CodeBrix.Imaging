// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-frame/src/data/{lf_global,lf_group,
// pass_group}.rs, crates/jxl-modular/src/image.rs, and crates/jxl-vardct/src/lf.rs,
// 0.12.6, f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Rendering;

namespace CodeBrix.Imaging.Formats.Jxl.Modular;

internal sealed class JxlModularFrame
{
    internal JxlModularPlane[] Planes;
    internal JxlFrameHeader Header;
    internal JxlPatches Patches;
    internal JxlNoise Noise;
    internal JxlSplines Splines;
    internal float[] Dequant = [1f / 4096, 1f / 512, 1f / 256];

    internal static JxlModularFrame Read(JxlBitReader input, JxlImageHeader image, JxlFrameHeader frame, JxlFrameToc toc,
        JxlModularWorkspace workspace, CancellationToken cancellationToken, int maxDegreeOfParallelism = 1, JxlDecodeBudget budget = null)
    {
        if (!frame.Modular) { throw new NotSupportedException("VarDCT reconstruction is under development."); }
        JxlFrameSections sections = new(input, toc, workspace.Allocator, cancellationToken);
        JxlBitReader globalReader = sections.Get(0);
        JxlModularFrame result = new() { Header = frame };
        if ((frame.Flags & 2) != 0) { result.Patches = JxlPatches.Read(globalReader, image, frame, workspace.Allocator, budget); }
        if ((frame.Flags & 16) != 0) { result.Splines = JxlSplines.Read(globalReader, frame, workspace.Allocator); }
        if ((frame.Flags & 1) != 0) { result.Noise = JxlNoise.Read(globalReader); }
        if (!globalReader.ReadBool())
        {
            for (int i = 0; i < 3; i++)
            {
                result.Dequant[i] = globalReader.ReadF16() / 128;
                if (result.Dequant[i] < 1E-8f) { JxlThrowHelper.Invalid("Modular dequantization weight is too small."); }
            }
        }
        result.Splines?.ValidateArea(frame, 0, 1);
        int colorChannels = !frame.YCbCr && !image.XybEncoded && image.ColorChannels == 1 ? 1 : 3;
        long nodeLimit = Math.Min(1 << 22, 1024 + ((long)frame.Width * frame.Height * (colorChannels + image.ExtraChannels.Length) / 16));
        JxlMaTree globalTree = globalReader.ReadBool() ? JxlMaTree.Read(globalReader, workspace.Allocator, (int)nodeLimit) : null;
        List<JxlModularPlane> planes = [];
        int width = (int)frame.SampleWidth, height = (int)frame.SampleHeight;
        for (int i = 0; i < colorChannels; i++)
        {
            int hshift = 0, vshift = 0, paddedWidth = width, paddedHeight = height;
            if (frame.YCbCr)
            {
                bool horizontal = Array.Exists(frame.JpegUpsampling, value => value is 1 or 2);
                bool vertical = Array.Exists(frame.JpegUpsampling, value => value is 1 or 3);
                uint upsampling = frame.JpegUpsampling[i];
                hshift = horizontal && upsampling is 0 or 3 ? 1 : 0;
                vshift = vertical && upsampling is 0 or 2 ? 1 : 0;
                if (horizontal) { paddedWidth = (width + 1) & ~1; }
                if (vertical) { paddedHeight = (height + 1) & ~1; }
            }
            JxlModularPlane plane = workspace.Allocate(paddedWidth >> hshift, paddedHeight >> vshift, hshift, vshift);
            plane.OriginalWidth = width; plane.OriginalHeight = height;
            planes.Add(plane);
        }
        for (int i = 0; i < image.ExtraChannels.Length; i++)
        {
            long upsampling = BitOperations.Log2(frame.ExtraUpsampling[i]) + (long)image.ExtraChannels[i].DimShift;
            int shift = (int)(upsampling - BitOperations.Log2(frame.Upsampling));
            if (upsampling > 6 || shift < 0 || shift > 7 + frame.GroupSizeShift) { JxlThrowHelper.Invalid("invalid extra-channel sampling shift."); }
            JxlModularPlane plane = workspace.Allocate((width + (1 << shift) - 1) >> shift, (height + (1 << shift) - 1) >> shift, shift, shift);
            plane.OriginalWidth = width; plane.OriginalHeight = height;
            planes.Add(plane);
        }
        JxlModularImage modular = JxlModularImage.Read(globalReader, planes, globalTree, workspace);
        modular.DecodeGlobal(globalReader, (int)frame.GroupDimension, workspace);
        sections.Finish(0);
        int lfGroups = (int)frame.LfGroupCount, groups = (int)frame.GroupCount;
        int parallelism = sections.Single ? 1 : maxDegreeOfParallelism;
        JxlParallel.For(lfGroups, parallelism, cancellationToken, group =>
        {
            JxlBitReader reader = sections.Get(1 + group);
            List<JxlModularPlane> views = SelectGroup(modular, frame, group, true, 0);
            DecodeGroup(reader, views, globalTree, 1 + lfGroups + group, (int)image.BitDepth.Bits, workspace);
            sections.Finish(1 + group);
        });
        // A nonempty Modular HF-global section fails its size check.
        sections.Finish(1 + lfGroups);
        for (int pass = 0; pass < frame.PassCount; pass++)
        {
            JxlParallel.For(groups, parallelism, cancellationToken, group =>
            {
                int logical = 2 + lfGroups + (pass * groups) + group;
                JxlBitReader reader = sections.Get(logical);
                List<JxlModularPlane> views = SelectGroup(modular, frame, group, false, pass);
                DecodeGroup(reader, views, globalTree, 18 + (3 * lfGroups) + (pass * groups) + group, (int)image.BitDepth.Bits, workspace);
                sections.Finish(logical);
            });
        }
        if (sections.Single) { sections.Finish(0, true); }
        modular.Finish((int)image.BitDepth.Bits, workspace);
        result.Planes = modular.Channels.ToArray();
        return result;
    }

    internal static void DecodeGroup(JxlBitReader reader, List<JxlModularPlane> views, JxlMaTree globalTree,
        int streamIndex, int bitDepth, JxlModularWorkspace workspace)
    {
        if (views.Count == 0) { return; }
        using JxlModularWorkspace temporary = workspace.CreateTemporary();
        JxlModularImage local = JxlModularImage.Read(reader, views, globalTree, temporary);
        local.DecodeAll(reader, streamIndex, temporary);
        local.Finish(bitDepth, temporary);
    }

    internal static List<JxlModularPlane> SelectGroup(JxlModularImage modular, JxlFrameHeader frame, int group, bool lf, int pass)
    {
        List<JxlModularPlane> views = [];
        if (modular == null) { return views; }
        int dimension = (int)frame.GroupDimension * (lf ? 8 : 1);
        int columns = ((int)frame.SampleWidth + dimension - 1) / dimension;
        int column = group % columns, row = group / columns;
        for (int c = modular.GlobalChannelCount; c < modular.Channels.Count; c++)
        {
            JxlModularPlane plane = modular.Channels[c];
            int hs = plane.HorizontalShift, vs = plane.VerticalShift;
            if (hs < 0 || vs < 0) { JxlThrowHelper.Invalid("metadata channel outside global Modular stream."); }
            bool low = hs >= 3 && vs >= 3;
            if (low != lf || (!lf && PassForShift(frame, Math.Min(hs, vs)) != pass)) { continue; }
            int groupWidth = dimension >> hs, groupHeight = dimension >> vs;
            if (groupWidth == 0 || groupHeight == 0) { JxlThrowHelper.Invalid("Modular group channel shift is too large."); }
            int left = column * groupWidth, top = row * groupHeight;
            if (left >= plane.Width || top >= plane.Height) { continue; }
            views.Add(plane.View(left, top, Math.Min(groupWidth, plane.Width - left), Math.Min(groupHeight, plane.Height - top)));
        }
        return views;
    }

    private static int PassForShift(JxlFrameHeader frame, int shift)
    {
        int max = 3;
        for (int i = 0; i < frame.Downsample.Length; i++)
        {
            int min = BitOperations.Log2(frame.Downsample[i]);
            if (shift >= min && shift < max) { return (int)frame.LastPass[i]; }
            max = min;
        }
        return (int)frame.PassCount - 1;
    }

}
