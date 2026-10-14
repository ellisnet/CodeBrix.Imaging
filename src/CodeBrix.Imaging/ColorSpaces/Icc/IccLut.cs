// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// ICC LUT parsing and interpolation adapted from skcms skcms.cc and
// src/Transform_inl.h, 7e572ada698a60ff50bd9038fd6e01e44f40868a (BSD-3-Clause).
// Copyright (c) 2018 Google Inc. All rights reserved.
// See THIRD-PARTY-NOTICES.txt for the complete upstream notice.
// PCS scaling and legacy XYZ input matrices follow ICC.1:2022,
// sections 6.3.4.2 and 10.10-10.13, rather than skcms's matrix-folded scaling.

using System;
using System.Numerics;

namespace CodeBrix.Imaging.ColorSpaces.Icc;

internal sealed class IccLut
{
    internal int InputChannels, OutputChannels;
    private bool reverse, legacy, legacyLab;
    private IccCurve[] input, middle, output;
    private IccMatrix matrix;
    private ReadOnlyMemory<byte> grid;
    private int[] dimensions;
    private int precision;
    private float pcsScale = 1;

    internal static IccLut Read(ReadOnlyMemory<byte> data, bool reverse, bool xyz)
    {
        ReadOnlySpan<byte> bytes = data.Span;
        IccData.Require(bytes, 0, 12);
        IccLut result = new() { InputChannels = bytes[8], OutputChannels = bytes[9], reverse = reverse };
        if ((!reverse && (result.InputChannels is < 1 or > 4 || result.OutputChannels != 3)) ||
            (reverse && (result.InputChannels != 3 || result.OutputChannels is < 3 or > 4)))
        { throw IccData.Invalid("unsupported LUT channel dimensions."); }
        uint type = IccData.U32(bytes, 0);
        // LUT tables operate on normalized coordinates. Decode XYZ only after
        // the final B curve, or encode it before the first B curve in reverse.
        // ICC deliberately leaves 8-bit XYZ undefined; retain unit scaling for
        // that legacy case instead of inventing a standardized encoding.
        if (xyz && type != 0x6d667431) { result.pcsScale = 65535f / 32768; }
        else if (!xyz && type == 0x6d667432)
        {
            // lut16Type retains the v2 Lab encoding even in a v4 profile:
            // FF00, rather than FFFF, denotes normalized Lab's upper endpoint.
            result.pcsScale = 65535f / 65280; result.legacyLab = true;
        }
        if (type is 0x6d667431 or 0x6d667432) // mft1, mft2
        {
            result.legacy = true;
            result.precision = type == 0x6d667431 ? 1 : 2;
            IccData.Require(bytes, 0, result.precision == 1 ? 48 : 52);
            int inputCount = result.precision == 1 ? 256 : IccData.U16(bytes, 48);
            int outputCount = result.precision == 1 ? 256 : IccData.U16(bytes, 50);
            if (inputCount is < 2 or > 4096 || outputCount is < 2 or > 4096) { throw IccData.Invalid("invalid LUT curve lengths."); }
            result.dimensions = new int[result.InputChannels]; Array.Fill(result.dimensions, bytes[10]);
            int offset = result.precision == 1 ? 48 : 52;
            result.input = ReadTables(data, ref offset, result.InputChannels, inputCount, result.precision);
            int gridBytes = result.GridBytes();
            IccData.Require(bytes, offset, gridBytes); result.grid = data.Slice(offset, gridBytes); offset += gridBytes;
            result.output = ReadTables(data, ref offset, result.OutputChannels, outputCount, result.precision);
            // A legacy matrix can be nonidentity only for PCSXYZ input.
            if (reverse && xyz)
            {
                float[] matrix = new float[9];
                for (int i = 0; i < 9; i++) { matrix[i] = IccData.Fixed(bytes, 12 + (i * 4)); }
                result.matrix = new(matrix);
            }
            return result;
        }
        if (type != (reverse ? 0x6d424120U : 0x6d414220U)) { throw IccData.Invalid("unknown LUT encoding."); }
        IccData.Require(bytes, 0, 32);
        int bOffset = IccData.Offset(bytes, 12), matrixOffset = IccData.Offset(bytes, 16), mOffset = IccData.Offset(bytes, 20);
        int clutOffset = IccData.Offset(bytes, 24), aOffset = IccData.Offset(bytes, 28);
        if (bOffset == 0) { throw IccData.Invalid("LUT B curves are missing."); }
        IccCurve[] b = ReadCurves(data, bOffset, 3);
        if (reverse) { result.input = b; } else { result.output = b; }
        if ((matrixOffset == 0) != (mOffset == 0)) { throw IccData.Invalid("LUT matrix and M curves must occur together."); }
        if (matrixOffset != 0)
        {
            result.middle = ReadCurves(data, mOffset, 3);
            IccData.Require(bytes, matrixOffset, 48);
            float[] matrix = new float[12];
            for (int i = 0; i < 12; i++) { matrix[i] = IccData.Fixed(bytes, matrixOffset + (i * 4)); }
            result.matrix = new(matrix);
        }
        if ((aOffset == 0) != (clutOffset == 0)) { throw IccData.Invalid("LUT A curves and grid must occur together."); }
        if (aOffset != 0)
        {
            IccCurve[] a = ReadCurves(data, aOffset, reverse ? result.OutputChannels : result.InputChannels);
            if (reverse) { result.output = a; } else { result.input = a; }
            IccData.Require(bytes, clutOffset, 20);
            result.dimensions = new int[result.InputChannels];
            for (int i = 0; i < result.dimensions.Length; i++) { result.dimensions[i] = bytes[clutOffset + i]; }
            result.precision = bytes[clutOffset + 16];
            int size = result.GridBytes();
            IccData.Require(bytes, clutOffset + 20, size); result.grid = data.Slice(clutOffset + 20, size);
        }
        else if (result.InputChannels != result.OutputChannels) { throw IccData.Invalid("LUT without grid cannot change channel count."); }
        return result;
    }

    internal void Apply(Span<float> channels)
    {
        if (this.reverse) { this.ScalePcs(channels, 1 / this.pcsScale); }
        if (this.legacy) { this.ApplyMatrix(channels); }
        ApplyCurves(this.input, channels);
        if (this.legacy)
        {
            this.Interpolate(channels); ApplyCurves(this.output, channels);
            if (!this.reverse) { this.ScalePcs(channels, this.pcsScale); }
            return;
        }
        if (!this.reverse && !this.grid.IsEmpty) { this.Interpolate(channels); }
        if (!this.reverse) { ApplyCurves(this.middle, channels); }
        this.ApplyMatrix(channels);
        if (this.reverse) { ApplyCurves(this.middle, channels); }
        if (this.reverse && !this.grid.IsEmpty) { this.Interpolate(channels); }
        ApplyCurves(this.output, channels);
        if (!this.reverse) { this.ScalePcs(channels, this.pcsScale); }
    }

    private void ScalePcs(Span<float> channels, float scale)
    {
        if (this.pcsScale == 1) { return; }
        for (int c = 0; c < 3; c++)
        {
            float value = channels[c] * scale;
            channels[c] = this.legacyLab && !this.reverse ? Math.Clamp(value, 0, 1) : value;
        }
    }

    private void ApplyMatrix(Span<float> channels)
    {
        if (this.matrix == null) { return; }
        Vector3 value = this.matrix.Apply(new(channels[0], channels[1], channels[2]));
        channels[0] = Math.Clamp(value.X, 0, 1);
        channels[1] = Math.Clamp(value.Y, 0, 1);
        channels[2] = Math.Clamp(value.Z, 0, 1);
    }

    private int GridBytes()
    {
        if (this.precision is not (1 or 2)) { throw IccData.Invalid("invalid LUT grid precision."); }
        long points = 1;
        foreach (int dimension in this.dimensions)
        {
            if (dimension < 2) { throw IccData.Invalid("LUT grid axis is too small."); }
            points *= dimension;
            if (points > 350_000_000) { throw IccData.Invalid("LUT grid is too large."); }
        }
        long size = points * this.OutputChannels * this.precision;
        if (size > int.MaxValue) { throw IccData.Invalid("LUT exceeds the supported profile size."); }
        return (int)size;
    }

    private static IccCurve[] ReadTables(ReadOnlyMemory<byte> data, ref int offset, int channels, int entries, int precision)
    {
        IccCurve[] curves = new IccCurve[channels];
        int length = entries * precision;
        for (int c = 0; c < channels; c++)
        {
            IccData.Require(data.Span, offset, length); curves[c] = IccCurve.Table(data.Slice(offset, length), entries, precision); offset += length;
        }
        return curves;
    }

    private static IccCurve[] ReadCurves(ReadOnlyMemory<byte> data, int offset, int count)
    {
        IccCurve[] curves = new IccCurve[count];
        for (int c = 0; c < count; c++)
        {
            IccData.Require(data.Span, offset, 0); curves[c] = IccCurve.Read(data[offset..], out int size);
            offset = checked(offset + ((size + 3) & ~3));
        }
        return curves;
    }

    private static void ApplyCurves(IccCurve[] curves, Span<float> values)
    {
        if (curves == null) { return; }
        for (int c = 0; c < curves.Length; c++) { values[c] = curves[c].Evaluate(values[c]); }
    }

    private void Interpolate(Span<float> channels)
    {
        Span<int> low = stackalloc int[4], high = stackalloc int[4];
        Span<float> fraction = stackalloc float[4], result = stackalloc float[4]; result.Clear();
        int stride = 1;
        for (int c = this.InputChannels - 1; c >= 0; c--)
        {
            float value = float.IsNaN(channels[c]) ? 0 : Math.Clamp(channels[c], 0, 1);
            float scaled = value * (this.dimensions[c] - 1);
            int index = (int)scaled;
            low[c] = index * stride; high[c] = Math.Min(index + 1, this.dimensions[c] - 1) * stride;
            fraction[c] = scaled - index; stride *= this.dimensions[c];
        }
        for (int combination = 0; combination < 1 << this.InputChannels; combination++)
        {
            int index = 0;
            float weight = 1;
            for (int c = 0; c < this.InputChannels; c++)
            {
                bool upper = (combination & (1 << c)) != 0;
                index += upper ? high[c] : low[c]; weight *= upper ? fraction[c] : 1 - fraction[c];
            }
            for (int c = 0; c < this.OutputChannels; c++)
            {
                int offset = ((index * this.OutputChannels) + c) * this.precision;
                float value = this.precision == 1 ? this.grid.Span[offset] / 255f : IccData.U16(this.grid.Span, offset) / 65535f;
                result[c] += weight * value;
            }
        }
        result[..this.OutputChannels].CopyTo(channels);
    }
}
