// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Palette cost estimation follows the scalar EstimateCost in libjxl
// lib/jxl/enc_modular_simd.cc and try_palettes in lib/jxl/enc_modular.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.IO;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Coding;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Modular;

internal static partial class JxlModularEncoder
{
    // Estimate all whole-frame palette candidates before the expensive group
    // tree/entropy searches. The selected palette still competes on actual
    // compressed size with the untransformed frame. Efforts 10/11 keep their
    // exhaustive whole-frame comparisons.
    private static (int Channels, int Order) SelectGlobalPalette(JxlModularPlane[] planes, int minChannels, int orders,
        int maximum, int independentMaximum, int bitDepth, MemoryAllocator allocator, CancellationToken cancellationToken)
    {
        (int Channels, int Order) selected = (-1, -1);
        double best = double.PositiveInfinity;
        for (int channels = Math.Min(4, planes.Length); channels >= minChannels; channels--)
        {
            if (channels == 0 ? independentMaximum == 0 : maximum == 0) { continue; }
            for (int order = orders == 0 ? 0 : 1; order <= orders; order++)
            {
                using JxlModularWorkspace workspace = JxlModularWorkspace.ForEncoding(allocator, Math.Max(262144, (long)planes[0].Width * planes[0].Height));
                JxlPaletteEncoder palette = channels == 0
                    ? JxlPaletteEncoder.TryIndependent(planes, independentMaximum, workspace, cancellationToken)
                    : JxlPaletteEncoder.TryCreate(planes, channels, maximum, workspace, cancellationToken, order: (JxlPaletteOrder)order, bitDepth: bitDepth);
                if (palette == null) { continue; }
                JxlBitWriter header = new(Stream.Null); palette.WriteHeader(header);
                double cost = header.Position + EstimatePaletteCost(palette.Planes, cancellationToken);
                if (cost < best) { best = cost; selected = (channels, order); }
            }
        }
        return selected;
    }

    private static double EstimatePaletteCost(JxlModularPlane[] planes, CancellationToken cancellationToken)
    {
        ReadOnlySpan<uint> cutoffs = [0, 1, 3, 5, 7, 11, 15, 23, 31, 47, 63, 95, 127, 191, 255, 392, 500];
        JxlIntegerConfig config = new(4, 2, 0);
        int[,] histograms = new int[18, 256]; int[] totals = new int[18];
        double cost = 0;
        foreach (JxlModularPlane plane in planes)
        {
            Array.Clear(histograms); Array.Clear(totals);
            long pixels = (long)plane.Width * plane.Height;
            // Contexts depend only on direct neighbors, so bounded sampling
            // needs no approximate predictor history or full-image token arrays.
            long stride = Math.Max(1, (pixels + 16383) / 16384), count = 0, extra = 0;
            for (long position = 0; position < pixels; position += stride)
            {
                if ((count++ & 1023) == 0) { cancellationToken.ThrowIfCancellationRequested(); }
                int x = (int)(position % plane.Width), y = (int)(position / plane.Width);
                int west = x != 0 ? plane.Get(x - 1, y) : y != 0 ? plane.Get(0, y - 1) : 0;
                int north = y != 0 ? plane.Get(x, y - 1) : west;
                int northwest = x != 0 && y != 0 ? plane.Get(x - 1, y - 1) : west;
                long difference = (long)Math.Max(west, Math.Max(north, northwest)) - Math.Min(west, Math.Min(north, northwest));
                int context = 0; foreach (uint cutoff in cutoffs) { if (difference < cutoff) { context++; } }
                int residual = unchecked(plane.Get(x, y) - JxlPredictor.Gradient(north, west, northwest));
                (uint symbol, int bits, _) = config.Encode(JxlBitWriter.PackSigned(residual));
                histograms[context, symbol]++; totals[context]++; extra += bits;
            }
            if (count == 0) { continue; }
            double entropy = extra;
            for (int c = 0; c < totals.Length; c++)
            {
                if (totals[c] == 0) { continue; }
                entropy += totals[c] * Math.Log2(totals[c]);
                for (int s = 0; s < 256; s++) { int n = histograms[c, s]; if (n > 1) { entropy -= n * Math.Log2(n); } }
            }
            cost += Math.Max(0, entropy) * pixels / count;
        }
        return cost;
    }
}
