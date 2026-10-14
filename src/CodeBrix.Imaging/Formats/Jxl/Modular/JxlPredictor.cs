// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-modular/src/predictor.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Modular;

internal sealed class JxlWeightedPredictorHeader
{
    internal int[] Parameters = [16, 10, 7, 7, 7, 0, 0];
    internal uint[] Weights = [13, 12, 12, 12];

    internal void Write(JxlBitWriter writer)
    {
        if (this.Parameters is not { Length: 7 } || this.Weights is not { Length: 4 }) { throw new ArgumentException("Invalid weighted predictor parameters."); }
        bool defaults = this.Parameters.AsSpan().SequenceEqual([16, 10, 7, 7, 7, 0, 0]) && this.Weights.AsSpan().SequenceEqual([13U, 12U, 12U, 12U]);
        writer.WriteBool(defaults);
        if (!defaults)
        {
            foreach (int parameter in this.Parameters) { writer.WriteBits(5, (uint)parameter); }
            foreach (uint weight in this.Weights) { writer.WriteBits(4, weight); }
        }
    }

    internal static JxlWeightedPredictorHeader Read(JxlBitReader reader)
    {
        JxlWeightedPredictorHeader header = new();
        if (!reader.ReadBool())
        {
            for (int i = 0; i < 7; i++) { header.Parameters[i] = (int)reader.ReadBits(5); }
            for (int i = 0; i < 4; i++) { header.Weights[i] = reader.ReadBits(4); }
        }
        return header;
    }
}

internal sealed class JxlPredictor : IDisposable
{
    private readonly JxlModularPlane plane;
    private readonly IReadOnlyList<JxlModularPlane> previous;
    private readonly bool previousNewestFirst;
    private readonly JxlSelfCorrectingPredictor selfCorrecting;
    private int x, y, w, n, nw, ne, nn, ww, nee, previousGradient;

    internal JxlPredictor(JxlModularPlane plane, IReadOnlyList<JxlModularPlane> previous, JxlWeightedPredictorHeader weighted, MemoryAllocator allocator,
        bool previousNewestFirst = true)
    {
        this.plane = plane;
        this.previous = previous;
        this.previousNewestFirst = previousNewestFirst;
        if (weighted != null && plane.Width != 0) { this.selfCorrecting = new(plane.Width, weighted, allocator); }
    }

    internal void Prepare(int x, int y)
        => this.Prepare(x, y, this.plane.Row(y), y == 0 ? default : this.plane.Row(y - 1),
            y < 2 ? default : this.plane.Row(y - 2));

    internal void Prepare(int x, int y, ReadOnlySpan<int> row, ReadOnlySpan<int> above, ReadOnlySpan<int> above2)
    {
        this.x = x; this.y = y;
        this.w = x != 0 ? row[x - 1] : y != 0 ? above[0] : 0;
        this.n = y != 0 ? above[x] : this.w;
        this.nw = y == 0 ? this.w : x == 0 ? this.n : above[x - 1];
        this.ne = y == 0 || x + 1 >= row.Length ? this.n : above[x + 1];
        this.nee = y == 0 || x + 2 >= row.Length ? this.ne : above[x + 2];
        this.nn = y < 2 ? this.n : above2[x];
        this.ww = x < 2 ? this.w : row[x - 2];
        this.selfCorrecting?.Predict(this.n, this.nw, this.ne, this.w, this.nn);
    }

    internal int Predict(int kind) => kind switch
    {
        0 => 0, 1 => this.w, 2 => this.n,
        3 => (int)(((long)this.w + this.n) / 2),
        4 => Math.Abs((long)this.n - this.nw) < Math.Abs((long)this.w - this.nw) ? this.w : this.n,
        5 => Gradient(this.n, this.w, this.nw),
        6 => (int)((this.selfCorrecting.Prediction + 3) >> 3),
        7 => this.ne, 8 => this.nw, 9 => this.ww,
        10 => (int)(((long)this.w + this.nw) / 2),
        11 => (int)(((long)this.n + this.nw) / 2),
        12 => (int)(((long)this.n + this.ne) / 2),
        13 => (int)(((6L * this.n) - (2L * this.nn) + (7L * this.w) + this.ww + this.nee + (3L * this.ne) + 8) / 16),
        _ => throw new InvalidImageContentException("JPEG XL: invalid Modular predictor."),
    };

    internal int Property(uint property) => property switch
    {
        2 => this.y, 3 => this.x, 4 => Abs(this.n), 5 => Abs(this.w), 6 => this.n, 7 => this.w,
        8 => unchecked(this.w - this.previousGradient), 9 => unchecked(this.w - this.nw + this.n),
        10 => unchecked(this.w - this.nw), 11 => unchecked(this.nw - this.n), 12 => unchecked(this.n - this.ne),
        13 => unchecked(this.n - this.nn), 14 => unchecked(this.w - this.ww), 15 => this.selfCorrecting?.MaxError ?? 0,
        >= 16 => this.ExtraProperty(property - 16), _ => 0,
    };

    private int ExtraProperty(uint property)
    {
        uint channel = property / 4;
        if (channel >= this.previous.Count) { return 0; }
        int index = this.previousNewestFirst ? (int)channel : this.previous.Count - 1 - (int)channel;
        JxlModularPlane other = this.previous[index];
        int value = other.Get(this.x, this.y);
        if ((property & 3) == 0) { return Abs(value); }
        if ((property & 3) == 1) { return value; }
        int gradient = this.x == 0 ? this.y == 0 ? 0 : other.Get(0, this.y - 1) : this.y == 0 ? other.Get(this.x - 1, 0) :
            Gradient(other.Get(this.x, this.y - 1), other.Get(this.x - 1, this.y), other.Get(this.x - 1, this.y - 1));
        return (property & 3) == 2 ? unchecked((int)Math.Abs((long)value - gradient)) : unchecked(value - gradient);
    }

    internal void Record(int value) => this.Record(value, this.plane.Row(this.y));

    internal void Record(int value, Span<int> row)
    {
        row[this.x] = value;
        this.selfCorrecting?.Record(value);
        this.previousGradient = this.x + 1 == this.plane.Width ? 0 : unchecked(this.w - this.nw + this.n);
    }

    internal static int Gradient(int n, int w, int nw) => (int)Math.Clamp((long)n + w - nw, Math.Min(n, w), Math.Max(n, w));
    private static int Abs(int value) => value < 0 ? unchecked(-value) : value;
    public void Dispose() => this.selfCorrecting?.Dispose();
}

internal sealed class JxlSelfCorrectingPredictor : IDisposable
{
    [InlineArray(4)]
    private struct Errors { private uint element; }
    [InlineArray(4)]
    private struct Predictions { private long element; }
    private readonly int width;
    private readonly JxlWeightedPredictorHeader header;
    private readonly IMemoryOwner<int> trueErrors;
    private readonly IMemoryOwner<Errors> errors;
    private int x, y, errorW, errorN, errorNw, errorNe;
    private Errors errorsNwWw, errorsNW, errorsNe;
    private Predictions predictions;
    internal long Prediction { get; private set; }
    internal int MaxError { get; private set; }

    internal JxlSelfCorrectingPredictor(int width, JxlWeightedPredictorHeader header, MemoryAllocator allocator)
    {
        this.width = width; this.header = header;
        this.trueErrors = allocator.Allocate<int>(width, AllocationOptions.Clean);
        try { this.errors = allocator.Allocate<Errors>(width, AllocationOptions.Clean); }
        catch { this.trueErrors.Dispose(); throw; }
    }

    internal void Predict(int n, int nw, int ne, int w, int nn)
    {
        long en = this.errorN, ew = this.errorW, enw = this.errorNw, ene = this.errorNe;
        long n3 = (long)n << 3, nw3 = (long)nw << 3, ne3 = (long)ne << 3, w3 = (long)w << 3, nn3 = (long)nn << 3;
        int[] p = this.header.Parameters;
        this.predictions[0] = w3 + ne3 - n3;
        this.predictions[1] = n3 - (((ew + en + ene) * p[0]) >> 5);
        this.predictions[2] = w3 - (((ew + en + enw) * p[1]) >> 5);
        this.predictions[3] = n3 - (((enw * p[2]) + (en * p[3]) + (ene * p[4]) + ((nn3 - n3) * p[5]) + ((nw3 - w3) * p[6])) >> 5);
        Span<uint> weights = stackalloc uint[4];
        uint sum = 0;
        for (int i = 0; i < 4; i++)
        {
            uint errors = unchecked(this.errorsNwWw[i] + this.errorsNW[i] + this.errorsNe[i]);
            int shift = BitOperations.Log2(((ulong)errors + 1) >> 5);
            weights[i] = 4 + ((this.header.Weights[i] * Divide((errors >> shift) + 1)) >> shift);
            sum += weights[i];
        }
        int log = BitOperations.Log2(sum >> 4);
        sum = 0;
        for (int i = 0; i < 4; i++) { weights[i] >>= log; sum += weights[i]; }
        long accumulated = (sum >> 1) - 1;
        for (int i = 0; i < 4; i++) { accumulated += this.predictions[i] * weights[i]; }
        long prediction = (accumulated * Divide(sum)) >> 24;
        if (((en ^ ew) | (en ^ enw)) <= 0) { prediction = Math.Clamp(prediction, Math.Min(n3, Math.Min(w3, ne3)), Math.Max(n3, Math.Max(w3, ne3))); }
        this.Prediction = prediction;
        long maximum = ew;
        if (Math.Abs(en) > Math.Abs(maximum)) { maximum = en; }
        if (Math.Abs(enw) > Math.Abs(maximum)) { maximum = enw; }
        if (Math.Abs(ene) > Math.Abs(maximum)) { maximum = ene; }
        this.MaxError = (int)maximum;
    }

    internal void Record(int sample)
    {
        long scaled = (long)sample << 3;
        int error = unchecked((int)(this.Prediction - scaled));
        Errors errors = default;
        for (int i = 0; i < 4; i++) { errors[i] = unchecked((uint)((Math.Abs(this.predictions[i] - scaled) + 3) >> 3)); }
        this.trueErrors.Memory.Span[this.x] = error;
        this.errors.Memory.Span[this.x] = errors;
        this.x++;
        if (this.x == this.width)
        {
            this.y++; this.x = 0;
            this.errorW = 0;
            this.errorN = this.errorNw = this.trueErrors.Memory.Span[0];
            this.errorsNW = this.errorsNwWw = this.errors.Memory.Span[0];
            this.errorNe = this.width == 1 ? this.errorN : this.trueErrors.Memory.Span[1];
            this.errorsNe = this.width == 1 ? this.errorsNW : this.errors.Memory.Span[1];
        }
        else
        {
            this.errorW = error; this.errorNw = this.errorN; this.errorN = this.errorNe;
            this.errorsNwWw = this.errorsNW;
            this.errorsNW = this.errorsNe;
            for (int i = 0; i < 4; i++) { this.errorsNW[i] = unchecked(this.errorsNW[i] + errors[i]); }
            if (this.x + 1 >= this.width) { this.errorNe = this.errorN; this.errorsNe = this.errorsNW; }
            else if (this.y != 0) { this.errorNe = this.trueErrors.Memory.Span[this.x + 1]; this.errorsNe = this.errors.Memory.Span[this.x + 1]; }
        }
    }

    private static uint Divide(uint denominator) => (1U << 24) / denominator;
    public void Dispose() { this.trueErrors.Dispose(); this.errors.Dispose(); }
}
