// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from libjxl lib/jxl/enc_ac_strategy.cc and quant_weights.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Formats.Jxl.Rendering;
using CodeBrix.Imaging.Formats.Jxl.VarDct;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;

internal sealed class JxlAcStrategy : IDisposable
{
    private readonly JxlFloatFrame source;
    private readonly JxlDequantMatrices matrices;
    private readonly JxlAdaptiveQuantization adaptive;
    private readonly JxlModularPlane xMap, bMap;
    private readonly Buffer2D<JxlVarBlock> blocks;
    private readonly CancellationToken cancellationToken;
    private readonly float distance;
    private readonly int effort, decodingSpeed;
    private readonly double lossMultiplier, zerosMultiplier, coefficientMultiplier;
    private readonly IMemoryOwner<float> scratch;
    private readonly Dictionary<(JxlTransformType Type, int X, int Y), (double Entropy, double Loss)> cache = [];
    private readonly double[] costs = new double[64];
    private int left, top, width, height;

    internal JxlAcStrategy(JxlFloatFrame source, JxlAdaptiveQuantization adaptive, JxlModularPlane xMap, JxlModularPlane bMap,
        Buffer2D<JxlVarBlock> blocks, float distance, int effort, CancellationToken cancellationToken, int decodingSpeed = 0, JxlDequantMatrices matrices = null)
    {
        this.matrices = matrices ?? JxlDequantMatrices.Default;
        this.source = source; this.adaptive = adaptive; this.xMap = xMap; this.bMap = bMap; this.blocks = blocks;
        this.distance = distance; this.effort = effort; this.cancellationToken = cancellationToken; this.decodingSpeed = decodingSpeed;
        double ratio = (distance + .13731742964354549) / (1 + .13731742964354549);
        this.lossMultiplier = 1.2 * Math.Pow(ratio, .33677806662454718);
        this.zerosMultiplier = 9.3089059022677905 * Math.Pow(ratio, .50990926717963703);
        this.coefficientMultiplier = 10.833273317067883 * Math.Pow(ratio, .36702940662370243);
        this.scratch = source.Allocator.Allocate<float>(4 * 64 * 64);
    }

    // A worker shares the read-only inputs and owns its tile position, cost cache and scratch.
    private JxlAcStrategy(JxlAcStrategy shared)
    {
        this.matrices = shared.matrices; this.source = shared.source; this.adaptive = shared.adaptive;
        this.xMap = shared.xMap; this.bMap = shared.bMap; this.blocks = shared.blocks;
        this.distance = shared.distance; this.effort = shared.effort; this.cancellationToken = shared.cancellationToken;
        this.decodingSpeed = shared.decodingSpeed; this.lossMultiplier = shared.lossMultiplier;
        this.zerosMultiplier = shared.zerosMultiplier; this.coefficientMultiplier = shared.coefficientMultiplier;
        this.scratch = shared.source.Allocator.Allocate<float>(4 * 64 * 64);
    }

    internal void Select()
    {
        // Each 64x64 color tile reads shared inputs and writes only its own blocks.
        int columns = (this.blocks.Width + 7) / 8, rows = (this.blocks.Height + 7) / 8;
        JxlParallel.ForEncoder(columns * rows, this.cancellationToken, () => new JxlAcStrategy(this),
            (tile, worker) => worker.SelectTile(tile % columns * 8, tile / columns * 8));
    }

    private void SelectTile(int tileLeft, int tileTop)
    {
        // Like the reference search, automatic choices stay within color tiles.
        // Larger transforms remain available to explicit transform callers.
        ReadOnlySpan<(JxlTransformType Type, int Effort, double Weight)> small =
        [(JxlTransformType.Dct8, 1, .8), (JxlTransformType.Dct4, 5, 1.08), (JxlTransformType.Dct2, 5, .95),
         (JxlTransformType.Dct4x8, 6, .85931637428340035), (JxlTransformType.Dct8x4, 6, .85931637428340035),
         (JxlTransformType.Hornuss, 5, 1.0427542510634957), (JxlTransformType.Afv0, 6, .81779489591359944),
         (JxlTransformType.Afv1, 6, .81779489591359944), (JxlTransformType.Afv2, 6, .81779489591359944), (JxlTransformType.Afv3, 6, .81779489591359944)];
        this.top = tileTop; this.left = tileLeft;
        {
            {
                this.cancellationToken.ThrowIfCancellationRequested();
                this.width = Math.Min(8, this.blocks.Width - this.left); this.height = Math.Min(8, this.blocks.Height - this.top);
                this.cache.Clear(); Array.Clear(this.costs);
                for (int y = 0; y < this.height; y++)
                {
                    for (int x = 0; x < this.width; x++)
                    {
                        double best = double.PositiveInfinity; JxlTransformType selected = JxlTransformType.Dct8;
                        foreach (var candidate in small)
                        {
                            if (candidate.Effort > this.effort) { continue; }
                            double weight = candidate.Weight / .8;
                            if (candidate.Type is JxlTransformType.Dct2 or JxlTransformType.Hornuss && this.distance < 5)
                            { weight -= .4 * Math.Pow((5 - this.distance) / 5, 2); }
                            if (candidate.Type is not (JxlTransformType.Dct8 or JxlTransformType.Dct2 or JxlTransformType.Hornuss) && this.distance > 4)
                            { weight += .5 * (this.distance < 12 ? 8 / (this.distance - 4) : 1); }
                            double cost = this.Estimate(candidate.Type, x, y, weight);
                            if (cost < best) { best = cost; selected = candidate.Type; }
                        }
                        this.Set(x, y, selected, best * (1 - .4 / (this.distance + 1.4)));
                    }
                }
                // Fairly compare both rectangular divisions with their square.
                for (int size = 2; size <= 8; size *= 2)
                {
                    for (int y = 0; y < this.height; y += size)
                    {
                        for (int x = 0; x < this.width; x += size)
                        {
                            if (x + size <= this.width && y + size <= this.height) { this.TrySquare(x, y, size); }
                            else
                            {
                                var (vertical, horizontal, _, rectangleWeight, _) = Types(size);
                                for (int offset = 0; offset < size; offset += size / 2)
                                { this.TryMerge(x + offset, y, vertical, rectangleWeight); this.TryMerge(x, y + offset, horizontal, rectangleWeight); }
                            }
                        }
                    }
                }
                if (this.effort < 6) { return; }
                for (int y = 0; y + 2 <= this.height; y++)
                { for (int x = 0; x + 2 <= this.width; x++) { if (((x | y) & 1) != 0) { this.TrySquare(x, y, 2); } } }
                int step = this.effort == 10 ? 1 : 2;
                for (int y = 0; y + 4 <= this.height; y += step)
                { for (int x = 0; x + 4 <= this.width; x += step) { if (((x | y) & 3) != 0) { this.TrySquare(x, y, 4); } } }
            }
        }
    }

    private void TrySquare(int x, int y, int size)
    {
        if (!this.Closed(x, y, size, size)) { return; }
        int half = size / 2;
        var (vertical, horizontal, square, rectangularWeight, squareWeight) = Types(size);
        double tl = this.Cost(x, y, half, half), tr = this.Cost(x + half, y, half, half);
        double bl = this.Cost(x, y + half, half, half), br = this.Cost(x + half, y + half, half, half);
        bool canVertical = this.Closed(x, y, half, size), canHorizontal = this.Closed(x, y, size, half);
        double vl = canVertical ? this.Estimate(vertical, x, y, rectangularWeight) : double.PositiveInfinity;
        double vr = canVertical ? this.Estimate(vertical, x + half, y, rectangularWeight) : double.PositiveInfinity;
        double ht = canHorizontal ? this.Estimate(horizontal, x, y, rectangularWeight) : double.PositiveInfinity;
        double hb = canHorizontal ? this.Estimate(horizontal, x, y + half, rectangularWeight) : double.PositiveInfinity;
        double full = this.Estimate(square, x, y, squareWeight);
        double vCost = Math.Min(vl, tl + bl) + Math.Min(vr, tr + br), hCost = Math.Min(ht, tl + tr) + Math.Min(hb, bl + br);
        if (full < vCost && full < hCost) { this.Set(x, y, square, full); }
        else if (vCost < hCost)
        {
            if (vl < tl + bl) { this.Set(x, y, vertical, vl); }
            if (vr < tr + br) { this.Set(x + half, y, vertical, vr); }
        }
        else
        {
            if (ht < tl + tr) { this.Set(x, y, horizontal, ht); }
            if (hb < bl + br) { this.Set(x, y + half, horizontal, hb); }
        }
    }

    private void TryMerge(int x, int y, JxlTransformType type, double weight)
    {
        (int w, int h) = type.Blocks();
        if (x + w > this.width || y + h > this.height || !this.Closed(x, y, w, h)) { return; }
        double candidate = this.Estimate(type, x, y, weight);
        if (candidate < this.Cost(x, y, w, h)) { this.Set(x, y, type, candidate); }
    }

    private double Cost(int x, int y, int width, int height)
    {
        double sum = 0;
        for (int dy = 0; dy < height; dy++) { for (int dx = 0; dx < width; dx++) { sum += this.costs[(y + dy) * 8 + x + dx]; } }
        return sum;
    }

    // Reject regions whose boundaries cut an already selected transform.
    private bool Closed(int x, int y, int width, int height)
    {
        for (int by = 0; by < this.height; by++)
        {
            for (int bx = 0; bx < this.width; bx++)
            {
                JxlVarBlock block = this.blocks.DangerousGetRowSpan(this.top + by)[this.left + bx];
                if (block.Multiplier <= 0) { continue; }
                (int w, int h) = block.Transform.Blocks();
                if (bx < x + width && by < y + height && bx + w > x && by + h > y &&
                    (bx < x || by < y || bx + w > x + width || by + h > y + height)) { return false; }
            }
        }
        return true;
    }

    private void Set(int x, int y, JxlTransformType type, double cost)
    {
        (int w, int h) = type.Blocks();
        for (int dy = 0; dy < h; dy++)
        {
            this.blocks.DangerousGetRowSpan(this.top + y + dy).Slice(this.left + x, w).Fill(new(type, -1));
            this.costs.AsSpan((y + dy) * 8 + x, w).Clear();
        }
        this.blocks.DangerousGetRowSpan(this.top + y)[this.left + x] = new(type, 1);
        this.costs[y * 8 + x] = cost;
    }

    private double Estimate(JxlTransformType type, int x, int y, double multiplier)
    {
        (int w, int h) = type.Blocks();
        if ((this.decodingSpeed >= 2 && (w >= 8 || h >= 8)) || (this.decodingSpeed >= 4 && w == 4 && h == 4))
        { return double.PositiveInfinity; }
        var key = (type, x, y);
        if (!this.cache.TryGetValue(key, out var estimate)) { estimate = this.Measure(type, x, y); this.cache.Add(key, estimate); }
        return estimate.Entropy * multiplier + estimate.Loss;
    }

    private (double Entropy, double Loss) Measure(JxlTransformType type, int x, int y)
    {
        this.cancellationToken.ThrowIfCancellationRequested();
        (int bw, int bh) = type.Blocks(); int width = bw * 8, height = bh * 8, count = width * height;
        int left = (this.left + x) * 8, top = (this.top + y) * 8;
        Span<float> coefficients = this.scratch.Memory.Span[..(3 * count)], error = this.scratch.Memory.Span.Slice(3 * count, count);
        double maximum = 0;
        for (int dy = 0; dy < bh; dy++)
        { foreach (float q in this.adaptive.Quantization.DangerousGetRowSpan(this.top + y + dy).Slice(this.left + x, bw)) { maximum = Math.Max(maximum, q); } }
        double quant = maximum;
        if (bw * bh >= 4)
        {
            double sum = 0;
            for (int dy = 0; dy < bh; dy++)
            { foreach (float q in this.adaptive.Quantization.DangerousGetRowSpan(this.top + y + dy).Slice(this.left + x, bw)) { sum += Math.Pow(q / maximum, 16); } }
            quant *= Math.Pow(sum / (bw * bh), 1.0 / 16);
        }
        for (int c = 0; c < 3; c++)
        {
            Span<float> channel = coefficients.Slice(c * count, count);
            for (int dy = 0; dy < height; dy++)
            {
                Span<float> sourceRow = this.source.Planes[c].DangerousGetRowSpan(Math.Min(top + dy, this.source.Planes[c].Height - 1));
                for (int dx = 0; dx < width; dx++) { channel[dy * width + dx] = sourceRow[Math.Min(left + dx, sourceRow.Length - 1)]; }
            }
            JxlForwardTransform.Apply(channel, type);
        }
        double entropy = 0, loss = 0; bool transpose = type.Transpose();
        for (int c = 0; c < 3; c++)
        {
            float[] matrix = this.matrices.Get(type, c);
            double correlation = c == 0 ? this.xMap.Get(this.left / 8, this.top / 8) / 84.0 : c == 1 ? 0 : 1 + this.bMap.Get(this.left / 8, this.top / 8) / 84.0;
            double rootSum = 0; int nonzero = 0;
            for (int dy = 0; dy < height; dy++)
            {
                for (int dx = 0; dx < width; dx++)
                {
                    int index = dy * width + dx;
                    if (dx < bw && dy < bh) { error[index] = 0; continue; }
                    double weight = matrix[transpose ? dx * height + dy : index];
                    double value = (coefficients[c * count + index] - correlation * coefficients[count + index]) * quant / weight;
                    double rounded = Math.Round(value); error[index] = (float)(weight * (value - rounded));
                    rootSum += Math.Sqrt(Math.Abs(rounded)); if (rounded != 0) { nonzero++; }
                }
            }
            JxlInverseTransform.Apply(error, type); double channelLoss = 0;
            for (int dy = 0; dy < height; dy++)
            {
                Span<float> mask = this.adaptive.FineMask.DangerousGetRowSpan(top + dy).Slice(left, width);
                for (int dx = 0; dx < width; dx++)
                {
                    double value = (mask[dx] + (c == 0 ? 12 : c == 1 ? 0 : 4)) * error[dy * width + dx];
                    value *= value; value *= value; channelLoss += value * value;
                }
            }
            loss += channelLoss * (c == 0 ? Math.Pow(8.2, 8) : c == 1 ? 1 : Math.Pow(1.03, 8));
            int bits = CeilLog2(nonzero + 1) + 1;
            entropy += this.coefficientMultiplier * rootSum + this.zerosMultiplier * (CeilLog2(bits + 17) + bits);
            if (c == 0 && bw * bh >= 2) { double penalty = 1 + Math.Min(3, bw * bh / 8.0); entropy *= penalty; loss *= penalty; }
        }
        return (entropy, this.lossMultiplier * Math.Pow(loss / count, 1.0 / 8) * count / quant);
    }

    private static int CeilLog2(int value) => value <= 1 ? 0 : BitOperations.Log2((uint)value - 1) + 1;
    private static (JxlTransformType Vertical, JxlTransformType Horizontal, JxlTransformType Square, double RectangleWeight, double SquareWeight) Types(int size) => size switch
    {
        2 => (JxlTransformType.Dct16x8, JxlTransformType.Dct8x16, JxlTransformType.Dct16, 1.21, 1.34),
        4 => (JxlTransformType.Dct32x16, JxlTransformType.Dct16x32, JxlTransformType.Dct32, 1.49, 1.48),
        _ => (JxlTransformType.Dct64x32, JxlTransformType.Dct32x64, JxlTransformType.Dct64, 2.25, 2.25),
    };

    public void Dispose() => this.scratch.Dispose();
}
