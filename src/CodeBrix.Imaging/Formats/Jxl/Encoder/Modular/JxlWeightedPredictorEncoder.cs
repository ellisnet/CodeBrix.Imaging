// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from libjxl lib/jxl/enc_modular.cc (EstimateWPCost) and
// lib/jxl/modular/encoding/context_predict.h (PredictorMode),
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Coding;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Modular;

internal static class JxlWeightedPredictorEncoder
{
    internal static ReadOnlySpan<int> Cutoffs =>
        [-500, -392, -255, -191, -127, -95, -63, -47, -31, -23, -15, -11, -7, -4, -3, -1, 0, 1, 3, 5, 7, 11, 15, 23, 31, 47, 63, 95, 127, 191, 255, 392, 500];

    internal static JxlWeightedPredictorHeader Preset(int mode) => mode switch
    {
        0 => new(),
        1 => new() { Parameters = [8, 8, 4, 0, 3, 23, 2], Weights = [13, 12, 12, 11] },
        2 => new() { Parameters = [10, 9, 7, 0, 0, 16, 9], Weights = [13, 12, 13, 12] },
        3 => new() { Parameters = [16, 8, 0, 16, 0, 23, 0], Weights = [13, 13, 12, 12] },
        4 => new() { Parameters = [10, 10, 5, 5, 5, 12, 4], Weights = [13, 12, 12, 12] },
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    internal static JxlWeightedPredictorHeader Select(JxlModularPlane[] planes, int effort, MemoryAllocator allocator, CancellationToken cancellationToken)
    {
        JxlWeightedPredictorHeader best = new();
        if (effort < 8 || planes.Length == 0) { return best; }
        double cost = double.PositiveInfinity;
        for (int mode = 0; mode < (effort == 8 ? 2 : 5); mode++)
        {
            JxlWeightedPredictorHeader header = Preset(mode);
            double candidate = Estimate(planes, header, allocator, cancellationToken) + (mode == 0 ? 0 : 51);
            if (candidate < cost) { cost = candidate; best = header; }
        }
        return best;
    }

    private static double Estimate(JxlModularPlane[] planes, JxlWeightedPredictorHeader header, MemoryAllocator allocator, CancellationToken cancellationToken)
    {
        JxlIntegerConfig config = new(4, 1, 0); int[,] histograms = new int[34, 80]; int[] totals = new int[34];
        double cost = 0;
        foreach (JxlModularPlane plane in planes)
        {
            using JxlPredictor predictor = new(plane, [], header, allocator);
            Array.Clear(histograms); Array.Clear(totals);
            for (int y = 0; y < plane.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested(); Span<int> row = plane.Row(y);
                ReadOnlySpan<int> above = y == 0 ? default : plane.Row(y - 1), above2 = y < 2 ? default : plane.Row(y - 2);
                for (int x = 0; x < row.Length; x++)
                {
                    predictor.Prepare(x, y, row, above, above2); int property = predictor.Property(15), context = 0;
                    foreach (int cutoff in Cutoffs) { if (cutoff >= property) { context++; } }
                    long residual = (long)row[x] - predictor.Predict(6);
                    if (residual < int.MinValue || residual > int.MaxValue) { return double.PositiveInfinity; }
                    (uint symbol, int bits, _) = config.Encode(JxlBitWriter.PackSigned((int)residual));
                    histograms[context, symbol]++; totals[context]++; cost += bits;
                    predictor.Record(row[x], row);
                }
            }
            for (int context = 0; context < 34; context++)
            {
                if (totals[context] == 0) { continue; }
                cost += totals[context] * Math.Log2(totals[context]);
                for (int symbol = 0; symbol < 80; symbol++)
                { int count = histograms[context, symbol]; if (count > 1) { cost -= count * Math.Log2(count); } }
            }
        }
        return cost;
    }
}
