// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Spline tokenization adapted from libjxl lib/jxl/enc_splines.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Entropy;
using CodeBrix.Imaging.Formats.Jxl.Headers;

namespace CodeBrix.Imaging.Formats.Jxl.Rendering;

internal sealed partial class JxlSplines
{
    internal static JxlSplines Create(JxlSpline[] source, int adjustment, JxlFrameHeader frame)
    {
        ulong pixels = (ulong)frame.Width * frame.Height;
        if (source == null || source.Length == 0 || (ulong)source.Length > Math.Min(1UL << 20, pixels / 2))
        { throw new ArgumentException("Invalid JPEG XL spline count.", nameof(source)); }
        JxlSplines result = new() { adjustment = adjustment, splines = new Quantized[source.Length] }; ulong total = 0;
        for (int i = 0; i < source.Length; i++)
        {
            JxlSpline input = source[i];
            if (input?.ControlPoints == null || input.ControlPoints.Length == 0 || (total += (uint)input.ControlPoints.Length) > Math.Min(1UL << 20, pixels / 2))
            { throw new ArgumentException("Invalid JPEG XL spline control points.", nameof(source)); }
            if (i == 0 && (input.ControlPoints[0].X < 0 || input.ControlPoints[0].Y < 0))
            { throw new ArgumentException("The first spline must start at nonnegative coordinates.", nameof(source)); }
            Quantized spline = new() { Points = new Vector2[input.ControlPoints.Length], EncodedPoints = (Point[])input.ControlPoints.Clone() };
            for (int p = 0; p < spline.Points.Length; p++)
            {
                Point point = spline.EncodedPoints[p]; spline.Points[p] = new(point.X, point.Y);
                if (p == 0) { continue; }
                if (spline.Points[p] == spline.Points[p - 1]) { throw new ArgumentException("Consecutive spline points must be distinct at binary32 precision.", nameof(source)); }
                Point previous = spline.EncodedPoints[p - 1];
                spline.Distance += (ulong)Math.Abs((long)point.X - previous.X) + (ulong)Math.Abs((long)point.Y - previous.Y);
            }
            int[][] values = [input.X, input.Y, input.B, input.Width];
            for (int c = 0; c < 4; c++)
            {
                if (values[c]?.Length != 32) { throw new ArgumentException("Each spline coefficient array must contain 32 values.", nameof(source)); }
                values[c].CopyTo(spline.Coefficients[c], 0);
            }
            result.splines[i] = spline;
        }
        result.ValidateArea(frame, 0, 1); return result;
    }

    internal void Write(JxlBitWriter writer, int effort, CancellationToken cancellationToken)
    {
        List<JxlToken> tokens = [new(2, (uint)this.splines.Length - 1)];
        Point previous = default;
        for (int i = 0; i < this.splines.Length; i++)
        {
            Point point = this.splines[i].EncodedPoints[0];
            tokens.Add(new(1, i == 0 ? (uint)point.X : Signed((long)point.X - previous.X)));
            tokens.Add(new(1, i == 0 ? (uint)point.Y : Signed((long)point.Y - previous.Y))); previous = point;
        }
        tokens.Add(new(0, JxlBitWriter.PackSigned(this.adjustment)));
        foreach (Quantized spline in this.splines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tokens.Add(new(3, (uint)spline.EncodedPoints.Length - 1)); long dx = 0, dy = 0;
            for (int p = 1; p < spline.EncodedPoints.Length; p++)
            {
                Point a = spline.EncodedPoints[p - 1], b = spline.EncodedPoints[p];
                long x = (long)b.X - a.X, y = (long)b.Y - a.Y;
                tokens.Add(new(4, Signed(x - dx))); tokens.Add(new(4, Signed(y - dy))); dx = x; dy = y;
            }
            foreach (int[] coefficients in spline.Coefficients)
            { foreach (int value in coefficients) { tokens.Add(new(5, JxlBitWriter.PackSigned(value))); } }
        }
        JxlToken[] values = tokens.ToArray();
        JxlEntropyEncoder entropy = JxlEntropyEncoder.CreateOptimized(values, 6, false, effort, false, cancellationToken);
        entropy.WriteHeader(writer); entropy.WriteTokens(writer, values, cancellationToken);
    }

    private static uint Signed(long value)
    {
        if (value < int.MinValue || value > int.MaxValue) { throw new ArgumentException("Spline coordinate differences exceed the signed 32-bit range."); }
        return JxlBitWriter.PackSigned((int)value);
    }
}
