// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Modular XYB sample conversion adapted from jxl-oxide crates/jxl-render/src/
// image.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

using System;
using System.Numerics;
using CodeBrix.Imaging.Formats.Jxl.Color;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Rendering;

internal sealed class JxlFloatFrame(MemoryAllocator allocator, int colorChannels, int totalChannels) : IDisposable
{
    internal readonly MemoryAllocator Allocator = allocator;
    internal readonly int ColorChannels = colorChannels;
    internal readonly Buffer2D<float>[] Planes = new Buffer2D<float>[totalChannels];
    internal readonly int[] SamplingShifts = new int[totalChannels];
    internal bool ColorTransformed;
    internal bool LinearSrgb;

    internal static JxlFloatFrame FromModular(JxlModularFrame modular, JxlImageHeader header, MemoryAllocator allocator)
    {
        int colors = modular.Planes.Length - header.ExtraChannels.Length;
        JxlFloatFrame frame = new(allocator, colors, modular.Planes.Length) { ColorTransformed = !header.XybEncoded && !modular.Header.YCbCr };
        try
        {
            for (int c = 0; c < frame.Planes.Length; c++)
            {
                frame.SamplingShifts[c] = c < colors ? BitOperations.Log2(modular.Header.Upsampling)
                    : BitOperations.Log2(modular.Header.ExtraUpsampling[c - colors]) + (int)header.ExtraChannels[c - colors].DimShift;
                int source = header.XybEncoded && c < 2 ? 1 - c : c;
                JxlModularPlane plane = modular.Planes[source];
                frame.Planes[c] = allocator.Allocate2D<float>(plane.Width, plane.Height);
                JxlBitDepth depth = c < colors ? header.BitDepth : header.ExtraChannels[c - colors].BitDepth;
                for (int y = 0; y < plane.Height; y++)
                {
                    Span<int> row = plane.Row(y);
                    Span<float> output = frame.Planes[c].DangerousGetRowSpan(y);
                    for (int x = 0; x < plane.Width; x++)
                    {
                        if (header.XybEncoded && c < colors)
                        {
                            int value = c == 2 ? (int)Math.Clamp((long)row[x] + modular.Planes[0].Get(x, y), int.MinValue, int.MaxValue) : row[x];
                            output[x] = value * modular.Dequant[c];
                        }
                        else { output[x] = JxlSampleConverter.ToFloat(row[x], depth); }
                    }
                }
            }
            return frame;
        }
        catch { frame.Dispose(); throw; }
    }

    internal JxlFloatFrame CreateColorScratch()
    {
        JxlFloatFrame scratch = new(this.Allocator, this.ColorChannels, this.ColorChannels);
        try
        {
            for (int c = 0; c < this.ColorChannels; c++) { scratch.Planes[c] = this.Allocator.Allocate2D<float>(this.Planes[c].Width, this.Planes[c].Height); }
            return scratch;
        }
        catch { scratch.Dispose(); throw; }
    }

    internal void SwapColors(JxlFloatFrame other)
    {
        for (int c = 0; c < this.ColorChannels; c++) { (this.Planes[c], other.Planes[c]) = (other.Planes[c], this.Planes[c]); }
    }

    public void Dispose()
    {
        foreach (Buffer2D<float> plane in this.Planes) { plane?.Dispose(); }
        Array.Clear(this.Planes);
    }
}
