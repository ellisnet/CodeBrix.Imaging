// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Inverse of jxl-oxide crates/jxl-modular/src/transform/rct.rs,
// 0.12.6, f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).
// Effort-dependent candidate order adapted from libjxl lib/jxl/enc_modular.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Copyright (c) the JPEG XL Project Authors. All rights reserved.

using System;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Coding;
using CodeBrix.Imaging.Formats.Jxl.Modular;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Modular;

internal static class JxlRctEncoder
{
    internal static int Select(JxlModularPlane[] planes, int effort, CancellationToken cancellationToken)
    {
        if (effort <= 4) { return 6; }
        int width = planes[0].Width, height = planes[0].Height;
        long total = (long)width * height, stride = Math.Max(1, total / (effort >= 10 ? 16384 : 4096));
        double best = double.PositiveInfinity; int selected = 6;
        JxlIntegerConfig config = new(4, 1, 0);
        ReadOnlySpan<int> candidates = [0, 6, 5, 10, 26, 40, 12, 19, 8, 4, 9, 15, 16, 17, 32, 33, 2, 1, 3];
        int candidateCount = effort switch { 5 => 4, 6 => 5, 7 => 7, 8 => 9, _ => candidates.Length };
        foreach (int type in candidates[..candidateCount])
        {
            cancellationToken.ThrowIfCancellationRequested();
            int[,] counts = new int[3, 80]; long extra = 0; int count = 0;
            for (long position = 0; position < total; position += stride)
            {
                if ((count++ & 1023) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
                int x = (int)(position % width), y = (int)(position / width);
                var sample = At(x, y);
                var west = x != 0 ? At(x - 1, y) : y != 0 ? At(0, y - 1) : default;
                var north = y != 0 ? At(x, y - 1) : west;
                var northwest = y == 0 ? west : x == 0 ? north : At(x - 1, y - 1);
                Add(0, sample.A, north.A, west.A, northwest.A);
                Add(1, sample.B, north.B, west.B, northwest.B);
                Add(2, sample.C, north.C, west.C, northwest.C);
            }
            double cost = extra + (type == 0 ? 0 : 8);
            for (int c = 0; c < 3; c++)
            {
                cost += count * Math.Log2(Math.Max(count, 1));
                for (int s = 0; s < 80; s++) { int n = counts[c, s]; if (n != 0) { cost += 3 - (n * Math.Log2(n)); } }
            }
            if (cost < best) { best = cost; selected = type; }

            (int A, int B, int C) At(int x, int y) => Transform(type, planes[0].Get(x, y), planes[1].Get(x, y), planes[2].Get(x, y));
            void Add(int channel, int sample, int n, int w, int nw)
            {
                uint residual = JxlBitWriter.PackSigned(unchecked(sample - JxlPredictor.Gradient(n, w, nw)));
                (uint symbol, int bits, _) = config.Encode(residual); counts[channel, symbol]++; extra += bits;
            }
        }
        return selected;
    }

    internal static void Apply(JxlModularPlane[] planes, int type, CancellationToken cancellationToken)
    {
        if (type is < 0 or > 41) { throw new ArgumentOutOfRangeException(nameof(type)); }
        for (int y = 0; y < planes[0].Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Span<int> a = planes[0].Row(y), b = planes[1].Row(y), c = planes[2].Row(y);
            for (int x = 0; x < a.Length; x++) { (a[x], b[x], c[x]) = Transform(type, a[x], b[x], c[x]); }
        }
    }

    internal static (int A, int B, int C) Transform(int type, int a, int b, int c)
    {
        (int d, int e, int f) = (type / 7) switch
        { 1 => (b, c, a), 2 => (c, a, b), 3 => (a, c, b), 4 => (b, a, c), 5 => (c, b, a), _ => (a, b, c) };
        int transform = type % 7;
        unchecked
        {
            if (transform == 6)
            { int co = d - f, temporary = f + (co >> 1), cg = e - temporary; return (temporary + (cg >> 1), co, cg); }
            return (d, (transform >> 1) switch { 1 => e - d, 2 => e - ((d + f) >> 1), _ => e }, (transform & 1) != 0 ? f - d : f);
        }
    }
}
