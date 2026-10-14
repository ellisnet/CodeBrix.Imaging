// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-frame/src/data/spline.rs and
// crates/jxl-render/src/features/spline.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).
// Control-point accounting follows libjxl lib/jxl/splines.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Copyright (c) the JPEG XL Project Authors. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Coding;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Rendering;

internal sealed partial class JxlSplines
{
    private sealed class Quantized
    {
        internal Vector2[] Points;
        internal Point[] EncodedPoints;
        internal readonly int[][] Coefficients = [new int[32], new int[32], new int[32], new int[32]];
        internal UInt128 Distance;
    }
    private readonly record struct Arc(Vector2 Point, float Length);
    private Quantized[] splines;
    private int adjustment;

    internal static JxlSplines Read(JxlBitReader reader, JxlFrameHeader header, MemoryAllocator allocator)
    {
        using JxlEntropyDecoder decoder = JxlEntropyDecoder.Read(reader, 6, allocator);
        decoder.Begin(reader);
        ulong pixels = (ulong)header.Width * header.Height;
        ulong maxPoints = Math.Min(1UL << 20, pixels / 2);
        uint count = decoder.Read(reader, 2);
        if (count >= maxPoints || ((ulong)count + 1) * 600 > (ulong)allocator.MemoryGroupAllocationLimitBytes)
        { JxlThrowHelper.Invalid("too many splines."); }
        count++;
        (long X, long Y)[] starts = new (long, long)[count];
        starts[0] = (decoder.Read(reader, 1), decoder.Read(reader, 1));
        for (int i = 1; i < starts.Length; i++)
        { starts[i] = (starts[i - 1].X + Signed(reader, decoder, 1), starts[i - 1].Y + Signed(reader, decoder, 1)); }
        JxlSplines result = new() { adjustment = Signed(reader, decoder, 0), splines = new Quantized[count] };
        // Every spline already has a starting point, including splines whose
        // remaining point counts have not yet been decoded.
        ulong totalPoints = count;
        for (int i = 0; i < result.splines.Length; i++)
        {
            reader.CheckCancellation();
            uint points = decoder.Read(reader, 3);
            if (totalPoints + points > maxPoints) { JxlThrowHelper.Invalid("too many spline control points."); }
            totalPoints += points;
            Quantized spline = new() { Points = new Vector2[points + 1] };
            (long x, long y) = starts[i];
            spline.Points[0] = new(x, y);
            long dx = 0, dy = 0;
            for (int p = 1; p < spline.Points.Length; p++)
            {
                dx += Signed(reader, decoder, 4); dy += Signed(reader, decoder, 4);
                if (dx == 0 && dy == 0) { JxlThrowHelper.Invalid("duplicate consecutive spline control points."); }
                spline.Distance += (UInt128)(ulong)Math.Abs(dx) + (ulong)Math.Abs(dy);
                try { x = checked(x + dx); y = checked(y + dy); }
                catch (OverflowException) { JxlThrowHelper.Invalid("spline control point overflow."); }
                spline.Points[p] = new(x, y);
                if (spline.Points[p] == spline.Points[p - 1]) { JxlThrowHelper.Invalid("spline control points exceed floating-point resolution."); }
            }
            for (int c = 0; c < 4; c++)
            { for (int k = 0; k < 32; k++) { spline.Coefficients[c][k] = Signed(reader, decoder, 5); } }
            result.splines[i] = spline;
        }
        decoder.Finish();
        return result;
    }

    internal void ValidateArea(JxlFrameHeader header, float correlationX, float correlationB)
    {
        ulong maxArea = Math.Min(1UL << 42, (1024UL * header.Width * header.Height) + (1UL << 32));
        UInt128 total = 0;
        foreach (Quantized spline in this.splines)
        {
            UInt128[] color = new UInt128[3];
            for (int c = 0; c < 3; c++) { foreach (int q in spline.Coefficients[c]) { color[c] += this.CeilingAdjustment(q); } }
            color[0] += (ulong)Math.Ceiling(Math.Abs(correlationX)) * color[1];
            color[2] += (ulong)Math.Ceiling(Math.Abs(correlationB)) * color[1];
            UInt128 maximum = UInt128.Max(color[0], UInt128.Max(color[1], color[2]));
            uint logColor = 0;
            while (maximum != 0) { logColor++; maximum >>= 1; }
            UInt128 width = 0;
            foreach (int q in spline.Coefficients[3]) { UInt128 weight = 1 + this.CeilingAdjustment(q); width += weight * weight * logColor; }
            // Compare before multiplying: extreme fields must not wrap the work estimate.
            if (spline.Distance != 0 && width > maxArea / spline.Distance) { JxlThrowHelper.Invalid("spline rendering area exceeds Level 10."); }
            total += width * spline.Distance;
            if (total > maxArea) { JxlThrowHelper.Invalid("spline rendering area exceeds Level 10."); }
        }
    }

    internal void Apply(JxlFloatFrame frame, float correlationX, float correlationB, CancellationToken cancellationToken)
    {
        if (frame.ColorChannels != 3) { return; }
        float adjustment = this.adjustment >= 0 ? 1 / (1 + (this.adjustment / 8f)) : 1 - (this.adjustment / 8f);
        ReadOnlySpan<float> weights = [.0042f, .075f, .07f, .3333f];
        Span<float> values = stackalloc float[3];
        foreach (Quantized spline in this.splines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            float[][] dct = [new float[32], new float[32], new float[32], new float[32]];
            bool nonzero = false;
            for (int c = 0; c < 4; c++)
            {
                for (int i = 0; i < 32; i++)
                { dct[c][i] = spline.Coefficients[c][i] * weights[c] * adjustment; if (c < 3 && dct[c][i] != 0) { nonzero = true; } }
            }
            if (!nonzero) { continue; }
            for (int i = 0; i < 32; i++) { dct[0][i] += correlationX * dct[1][i]; dct[2][i] += correlationB * dct[1][i]; }
            List<Arc> samples = Sample(spline.Points, frame.Allocator.MemoryGroupAllocationLimitBytes / 16, cancellationToken);
            float length = samples.Count - 2 + samples[^1].Length;
            for (int i = 0; i < samples.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Arc arc = samples[i];
                float t = 31 * (length == 0 ? 1 : Math.Min(1, i / length));
                float sigma = ContinuousIdct(dct[3], t), inverse = 1 / sigma;
                if (sigma == 0) { continue; }
                for (int c = 0; c < 3; c++) { values[c] = ContinuousIdct(dct[c], t) * arc.Length; }
                float maxColor = Math.Max(.01f, Math.Max(values[0], Math.Max(values[1], values[2])));
                float distance = MathF.Sqrt(2 * ((MathF.Log(10) * 3) + maxColor)) * Math.Abs(sigma);
                int left = Bound(arc.Point.X - distance + .5f, frame.Planes[0].Width);
                int right = Bound(arc.Point.X + distance + 1.5f, frame.Planes[0].Width);
                int top = Bound(arc.Point.Y - distance + .5f, frame.Planes[0].Height);
                int bottom = Bound(arc.Point.Y + distance + 1.5f, frame.Planes[0].Height);
                for (int y = top; y < bottom; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (int x = left; x < right; x++)
                    {
                        float dx = x - arc.Point.X, dy = y - arc.Point.Y;
                        float radius = MathF.Sqrt((dx * dx) + (dy * dy));
                        float factor = Erf(((.5f * radius) + .35355338f) * inverse) - Erf(((.5f * radius) - .35355338f) * inverse);
                        for (int c = 0; c < 3; c++)
                        { frame.Planes[c].DangerousGetRowSpan(y)[x] += .25f * values[c] * sigma * factor * factor; }
                    }
                }
            }
        }
    }

    private static int Bound(float value, int size) => (int)Math.Clamp(MathF.Floor(value), 0, size);
    private static int Signed(JxlBitReader reader, JxlEntropyDecoder decoder, int context) => JxlBitReader.UnpackSigned(decoder.Read(reader, context));
    private ulong CeilingAdjustment(int coefficient)
    {
        ulong value = (ulong)Math.Abs((long)coefficient);
        return this.adjustment >= 0 ? ((8 * value) + 7 + (uint)this.adjustment) / (8 + (ulong)this.adjustment)
            : value + (((value * (ulong)-(long)this.adjustment) + 7) / 8);
    }

    private static List<Arc> Sample(Vector2[] points, long limit, CancellationToken cancellationToken)
    {
        Vector2[] upsampled = new Vector2[Math.Max(1, ((points.Length - 1) * 16) + 1)];
        upsampled[^1] = points[^1];
        Span<Vector2> p = stackalloc Vector2[4], a = stackalloc Vector2[3], b = stackalloc Vector2[2];
        Span<float> knots = stackalloc float[4];
        for (int segment = 0; segment < points.Length - 1; segment++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            p[0] = segment == 0 ? (points[0] + points[0]) - points[1] : points[segment - 1];
            p[1] = points[segment]; p[2] = points[segment + 1];
            p[3] = segment + 2 == points.Length ? (points[^1] + points[^1]) - points[^2] : points[segment + 2];
            upsampled[segment * 16] = p[1]; knots[0] = 0;
            for (int k = 1; k < 4; k++) { Vector2 delta = p[k] - p[k - 1]; knots[k] = knots[k - 1] + MathF.Pow((delta.X * delta.X) + (delta.Y * delta.Y), .25f); }
            for (int step = 1; step < 16; step++)
            {
                float knot = knots[1] + ((step / 16f) * (knots[2] - knots[1]));
                for (int k = 0; k < 3; k++) { a[k] = p[k] + ((p[k + 1] - p[k]) * ((knot - knots[k]) / (knots[k + 1] - knots[k]))); }
                for (int k = 0; k < 2; k++) { b[k] = a[k] + ((a[k + 1] - a[k]) * ((knot - knots[k]) / (knots[k + 2] - knots[k]))); }
                upsampled[(segment * 16) + step] = b[0] + ((b[1] - b[0]) * ((knot - knots[1]) / (knots[2] - knots[1])));
            }
        }
        Vector2 current = upsampled[0];
        List<Arc> samples = [new(current, 1)];
        int next = 0;
        while (next < upsampled.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Vector2 previous = current;
            float distance = 0;
            while (true)
            {
                if (samples.Count >= limit) { JxlThrowHelper.Invalid("spline samples exceed the allocator limit."); }
                if (next >= upsampled.Length) { samples.Add(new(previous, distance)); break; }
                Vector2 delta = upsampled[next] - previous;
                float length = MathF.Sqrt((delta.X * delta.X) + (delta.Y * delta.Y));
                if (!float.IsFinite(length)) { JxlThrowHelper.Invalid("invalid spline length."); }
                if (distance + length >= 1)
                {
                    current = previous + (delta * ((1 - distance) / length));
                    if (current == previous) { JxlThrowHelper.Invalid("spline sampling exceeds floating-point resolution."); }
                    samples.Add(new(current, 1)); break;
                }
                distance += length; previous = upsampled[next++];
            }
        }
        return samples;
    }

    private static float ContinuousIdct(float[] dct, float t)
    {
        float value = dct[0];
        for (int i = 1; i < 32; i++) { value += 1.4142135623730951f * dct[i] * MathF.Cos(i * (MathF.PI / 32) * (t + .5f)); }
        return value;
    }

    private static float Erf(float value)
    {
        float x = Math.Abs(value);
        float denominator = (((((((x * .0777394369f) + .000205260015f) * x) + .232120216f) * x) + .277820801f) * x) + 1;
        float inverse = 1 / (denominator * denominator), result = 1 - (inverse * inverse);
        return MathF.CopySign(result, value);
    }
}
