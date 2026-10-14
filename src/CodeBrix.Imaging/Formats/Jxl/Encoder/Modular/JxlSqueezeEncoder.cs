// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from libjxl lib/jxl/modular/transform/enc_squeeze.cc, squeeze.cc
// and squeeze_params.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Collections.Generic;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using static CodeBrix.Imaging.Formats.Jxl.Bitstream.JxlU32;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Modular;

internal sealed class JxlSqueezeEncoder
{
    internal JxlModularPlane[] Planes;
    private readonly record struct Step(bool Horizontal, bool InPlace, int Begin, int Count);
    private readonly List<Step> steps = [];
    internal int[] Components;

    internal static JxlSqueezeEncoder Create(JxlModularPlane[] input, int levels, JxlModularWorkspace workspace,
        CancellationToken cancellationToken, int begin = 0)
    {
        int count = input.Length - begin;
        if (begin < 0 || count is < 1 or > 19) { throw new ArgumentOutOfRangeException(nameof(input)); }
        JxlSqueezeEncoder result = new();
        List<JxlModularPlane> planes = new(input);
        List<int> components = []; for (int i = 0; i < input.Length; i++) { components.Add(i); }
        for (int level = 0; level < levels; level++)
        {
            bool changed = false;
            for (int axis = 0; axis < 2; axis++)
            {
                bool horizontal = axis == 0;
                if ((horizontal ? planes[begin].Width : planes[begin].Height) <= 8) { continue; }
                result.Add(planes, components, new(horizontal, true, begin, count), workspace, cancellationToken); changed = true;
            }
            if (!changed) { break; }
        }
        result.Planes = planes.ToArray(); result.Components = components.ToArray(); return result;
    }

    // The default global layout first halves chroma in both axes, then places
    // progressively coarser averages before each level's residual channels.
    internal static JxlSqueezeEncoder CreateDefault(JxlModularPlane[] input, JxlModularWorkspace workspace, CancellationToken cancellationToken)
    {
        if (input.Length is < 1 or > 259) { throw new ArgumentOutOfRangeException(nameof(input)); }
        JxlSqueezeEncoder result = new(); List<JxlModularPlane> planes = new(input);
        List<int> components = []; for (int i = 0; i < input.Length; i++) { components.Add(i); }
        int width = input[0].Width, height = input[0].Height;
        if (input.Length >= 3 && input[1].Width == width && input[1].Height == height)
        {
            result.Add(planes, components, new(true, false, 1, 2), workspace, cancellationToken);
            result.Add(planes, components, new(false, false, 1, 2), workspace, cancellationToken);
        }
        // A transform can name at most nineteen channels. For larger inputs,
        // squeeze color alone and retain the extra channels' original sampling.
        int count = input.Length <= 19 ? input.Length : 3;
        if (height >= width && height > 8)
        { result.Add(planes, components, new(false, true, 0, count), workspace, cancellationToken); height = (height + 1) / 2; }
        while (width > 8 || height > 8)
        {
            if (width > 8) { result.Add(planes, components, new(true, true, 0, count), workspace, cancellationToken); width = (width + 1) / 2; }
            if (height > 8) { result.Add(planes, components, new(false, true, 0, count), workspace, cancellationToken); height = (height + 1) / 2; }
        }
        result.Planes = planes.ToArray(); result.Components = components.ToArray(); return result;
    }

    private void Add(List<JxlModularPlane> planes, List<int> components, Step step, JxlModularWorkspace workspace, CancellationToken cancellationToken)
    {
        JxlModularPlane[] differences = new JxlModularPlane[step.Count];
        int[] originalComponents = components.GetRange(step.Begin, step.Count).ToArray();
        for (int c = 0; c < step.Count; c++)
        { (planes[step.Begin + c], differences[c]) = Split(planes[step.Begin + c], step.Horizontal, workspace, cancellationToken); }
        int position = step.InPlace ? step.Begin + step.Count : planes.Count;
        planes.InsertRange(position, differences); components.InsertRange(position, originalComponents); this.steps.Add(step);
    }

    internal void WriteHeader(JxlBitWriter writer)
    {
        writer.WriteBits(2, 2);
        writer.WriteU32((uint)this.steps.Count, 0, U(4, 1), U(6, 9), U(8, 41));
        foreach (Step step in this.steps)
        {
            writer.WriteBool(step.Horizontal); writer.WriteBool(step.InPlace);
            writer.WriteU32((uint)step.Begin, U(3), U(6, 8), U(10, 72), U(13, 1096));
            writer.WriteU32((uint)step.Count, 1, 2, 3, U(4, 4));
        }
    }

    private static (JxlModularPlane Average, JxlModularPlane Residual) Split(JxlModularPlane source, bool horizontal,
        JxlModularWorkspace workspace, CancellationToken cancellationToken)
    {
        int hshift = source.HorizontalShift + (horizontal && source.HorizontalShift >= 0 ? 1 : 0);
        int vshift = source.VerticalShift + (!horizontal && source.VerticalShift >= 0 ? 1 : 0);
        JxlModularPlane average = workspace.Allocate(horizontal ? (source.Width + 1) / 2 : source.Width,
            horizontal ? source.Height : (source.Height + 1) / 2, hshift, vshift);
        JxlModularPlane residual = workspace.Allocate(horizontal ? source.Width / 2 : source.Width,
            horizontal ? source.Height : source.Height / 2, hshift, vshift);
        average.OriginalWidth = residual.OriginalWidth = source.OriginalWidth;
        average.OriginalHeight = residual.OriginalHeight = source.OriginalHeight;
        int lines = horizontal ? source.Height : source.Width, length = horizontal ? source.Width : source.Height;
        for (int line = 0; line < lines; line++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int i = 0; i < length / 2; i++)
            {
                int a = Get(source, line, i * 2, horizontal), b = Get(source, line, (i * 2) + 1, horizontal);
                // This inverse-safe form also defines wrapping differences.
                Set(average, line, i, horizontal, unchecked(a - (unchecked(a - b) / 2)));
            }
            if ((length & 1) != 0) { Set(average, line, length / 2, horizontal, Get(source, line, length - 1, horizontal)); }
            for (int i = 0; i < length / 2; i++)
            {
                int a = Get(source, line, i * 2, horizontal), b = Get(source, line, (i * 2) + 1, horizontal);
                int avg = Get(average, line, i, horizontal), previous = i == 0 ? avg : Get(source, line, (i * 2) - 1, horizontal);
                int next = i + 1 < (length + 1) / 2 ? Get(average, line, i + 1, horizontal) : avg;
                Set(residual, line, i, horizontal, unchecked(a - b - JxlSqueezeTransform.Tendency(previous, avg, next)));
            }
        }
        return (average, residual);
    }

    private static int Get(JxlModularPlane plane, int line, int position, bool horizontal) => horizontal ? plane.Get(position, line) : plane.Get(line, position);
    private static void Set(JxlModularPlane plane, int line, int position, bool horizontal, int value)
    { if (horizontal) { plane.Set(position, line, value); } else { plane.Set(line, position, value); } }
}
