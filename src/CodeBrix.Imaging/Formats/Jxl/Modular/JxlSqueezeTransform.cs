// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-modular/src/transform.rs and
// transform/squeeze.rs, 0.12.6, f8ae722ef2d6b782941c89517d19cfbf605c4a9d.
// Apache-2.0 option.

using System.Collections.Generic;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using static CodeBrix.Imaging.Formats.Jxl.Bitstream.JxlU32;

namespace CodeBrix.Imaging.Formats.Jxl.Modular;

internal sealed class JxlSqueezeTransform : JxlModularTransform
{
    private sealed class Step(bool horizontal, bool inPlace, int begin, int count)
    {
        internal readonly bool Horizontal = horizontal, InPlace = inPlace;
        internal readonly int Begin = begin, Count = count;
        internal JxlModularPlane[] Originals;
    }
    private readonly List<Step> steps = [];

    internal static JxlSqueezeTransform ReadSqueeze(JxlBitReader reader)
    {
        JxlSqueezeTransform transform = new();
        uint count = reader.ReadU32(0, U(4, 1), U(6, 9), U(8, 41));
        for (int i = 0; i < count; i++)
        {
            bool horizontal = reader.ReadBool(), inPlace = reader.ReadBool();
            int begin = ReadChannel(reader), channels = (int)reader.ReadU32(1, 2, 3, U(4, 4));
            transform.steps.Add(new(horizontal, inPlace, begin, channels));
        }
        return transform;
    }

    internal override void Prepare(List<JxlModularPlane> channels, ref int metaChannels, JxlModularWorkspace workspace)
    {
        if (this.steps.Count == 0) { this.SetDefaults(channels, metaChannels); }
        foreach (Step step in this.steps)
        {
            int end = step.Begin + step.Count;
            if (end > channels.Count) { JxlThrowHelper.Invalid("invalid squeeze channel range."); }
            if (step.Begin < metaChannels)
            {
                if (!step.InPlace || end > metaChannels) { JxlThrowHelper.Invalid("squeeze crosses metadata channel boundary."); }
                metaChannels += step.Count;
            }
            step.Originals = channels.GetRange(step.Begin, step.Count).ToArray();
            JxlModularPlane[] residuals = new JxlModularPlane[step.Count];
            for (int i = 0; i < step.Count; i++)
            {
                JxlModularPlane original = step.Originals[i];
                if (original.Width == 0 || original.Height == 0 || original.HorizontalShift > 30 || original.VerticalShift > 30)
                { JxlThrowHelper.Invalid("invalid squeeze dimensions or shift."); }
                int hshift = original.HorizontalShift, vshift = original.VerticalShift;
                if (step.Horizontal && hshift >= 0) { hshift++; }
                if (!step.Horizontal && vshift >= 0) { vshift++; }
                JxlModularPlane average = workspace.Allocate(step.Horizontal ? (original.Width + 1) / 2 : original.Width,
                    step.Horizontal ? original.Height : (original.Height + 1) / 2, hshift, vshift);
                JxlModularPlane residual = workspace.Allocate(step.Horizontal ? original.Width / 2 : original.Width,
                    step.Horizontal ? original.Height : original.Height / 2, hshift, vshift);
                average.OriginalWidth = residual.OriginalWidth = original.OriginalWidth;
                average.OriginalHeight = residual.OriginalHeight = original.OriginalHeight;
                channels[step.Begin + i] = average;
                residuals[i] = residual;
            }
            channels.InsertRange(step.InPlace ? end : channels.Count, residuals);
            if (channels.Count > 65536) { JxlThrowHelper.Invalid("too many transformed Modular channels."); }
        }
    }

    private void SetDefaults(List<JxlModularPlane> channels, int first)
    {
        if (first == channels.Count) { JxlThrowHelper.Invalid("default squeeze needs an image channel."); }
        int width = channels[first].Width, height = channels[first].Height;
        if (channels.Count - first >= 3 && channels[first + 1].Width == width && channels[first + 1].Height == height)
        {
            this.steps.Add(new(true, false, first + 1, 2));
            this.steps.Add(new(false, false, first + 1, 2));
        }
        int count = channels.Count - first;
        if (height >= width && height > 8) { this.steps.Add(new(false, true, first, count)); height = (height + 1) / 2; }
        while (width > 8 || height > 8)
        {
            if (width > 8) { this.steps.Add(new(true, true, first, count)); width = (width + 1) / 2; }
            if (height > 8) { this.steps.Add(new(false, true, first, count)); height = (height + 1) / 2; }
        }
    }

    internal override void Undo(List<JxlModularPlane> channels, int bitDepth, JxlModularWorkspace workspace)
    {
        for (int s = this.steps.Count - 1; s >= 0; s--)
        {
            Step step = this.steps[s];
            int residualBegin = step.InPlace ? step.Begin + step.Count : channels.Count - step.Count;
            for (int c = 0; c < step.Count; c++)
            {
                JxlModularPlane average = channels[step.Begin + c], residual = channels[residualBegin + c], target = step.Originals[c];
                int lines = step.Horizontal ? target.Height : target.Width;
                int length = step.Horizontal ? target.Width : target.Height;
                for (int line = 0; line < lines; line++)
                {
                    int avg = Get(average, line, 0, step.Horizontal), previous = avg;
                    for (int position = 0; position < length / 2; position++)
                    {
                        int next = position + 1 < (length + 1) / 2 ? Get(average, line, position + 1, step.Horizontal) : avg;
                        int diff = unchecked(Get(residual, line, position, step.Horizontal) + Tendency(previous, avg, next));
                        int first = unchecked(avg + (diff / 2)), second = unchecked(first - diff);
                        Set(target, line, position * 2, step.Horizontal, first);
                        Set(target, line, (position * 2) + 1, step.Horizontal, second);
                        avg = next; previous = second;
                    }
                    if ((length & 1) != 0) { Set(target, line, length - 1, step.Horizontal, avg); }
                }
                channels[step.Begin + c] = target;
            }
            channels.RemoveRange(residualBegin, step.Count);
        }
    }

    private static int Get(JxlModularPlane plane, int line, int position, bool horizontal) => horizontal ? plane.Get(position, line) : plane.Get(line, position);
    private static void Set(JxlModularPlane plane, int line, int position, bool horizontal, int value)
    {
        if (horizontal) { plane.Set(position, line, value); }
        else { plane.Set(line, position, value); }
    }

    internal static int Tendency(int a, int b, int c)
    {
        unchecked
        {
            if (a >= b && b >= c)
            {
                int x = ((4 * a) - (3 * c) - b + 6) / 12;
                if (x - (x & 1) > 2 * (a - b)) { x = (2 * (a - b)) + 1; }
                if (x + (x & 1) > 2 * (b - c)) { x = 2 * (b - c); }
                return x;
            }
            if (a <= b && b <= c)
            {
                int x = ((4 * a) - (3 * c) - b - 6) / 12;
                if (x + (x & 1) < 2 * (a - b)) { x = (2 * (a - b)) - 1; }
                if (x - (x & 1) < 2 * (b - c)) { x = 2 * (b - c); }
                return x;
            }
            return 0;
        }
    }
}
