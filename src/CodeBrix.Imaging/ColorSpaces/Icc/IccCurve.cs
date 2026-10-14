// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Curve parsing/evaluation adapted from skcms skcms.cc and src/Transform_inl.h,
// 7e572ada698a60ff50bd9038fd6e01e44f40868a (BSD-3-Clause).
// Copyright (c) 2018 Google Inc. All rights reserved.
// See THIRD-PARTY-NOTICES.txt for the complete upstream notice.

using System;

namespace CodeBrix.Imaging.ColorSpaces.Icc;

internal sealed class IccCurve
{
    internal float G = 1, A = 1, B, C, D, E, F;
    private ReadOnlyMemory<byte> table;
    private int count, bytesPerEntry;
    internal static readonly IccCurve Identity = new();
    internal static readonly IccCurve Srgb = new() { G = 2.4f, A = 1f / 1.055f, B = .055f / 1.055f, C = 1f / 12.92f, D = .04045f };

    internal static IccCurve Read(ReadOnlyMemory<byte> data, out int bytesRead)
    {
        ReadOnlySpan<byte> bytes = data.Span;
        uint type = IccData.U32(bytes, 0);
        IccCurve result = new();
        if (type == 0x63757276) // curv
        {
            int count = IccData.Offset(bytes, 8);
            if (count > 1 << 24) { throw IccData.Invalid("curve table is too large."); }
            bytesRead = 12 + (count * 2);
            IccData.Require(bytes, 0, bytesRead);
            if (count == 1) { result.G = IccData.U16(bytes, 12) / 256f; }
            else if (count > 1) { return Table(data.Slice(12, count * 2), count, 2); }
            return result;
        }
        if (type != 0x70617261) { throw IccData.Invalid("unknown curve encoding."); } // para
        int function = IccData.U16(bytes, 8);
        ReadOnlySpan<int> parameters = [1, 3, 4, 5, 7];
        if (function >= parameters.Length) { throw IccData.Invalid("unknown parametric curve function."); }
        bytesRead = 12 + (parameters[function] * 4);
        IccData.Require(bytes, 0, bytesRead);
        result.G = IccData.Fixed(bytes, 12);
        if (function != 0)
        {
            result.A = IccData.Fixed(bytes, 16); result.B = IccData.Fixed(bytes, 20);
            if (function is 1 or 2)
            {
                if (result.A == 0) { throw IccData.Invalid("zero curve scale."); }
                result.D = -result.B / result.A;
                if (function == 2) { result.E = result.F = IccData.Fixed(bytes, 24); }
            }
            else
            {
                result.C = IccData.Fixed(bytes, 24); result.D = IccData.Fixed(bytes, 28);
                if (function == 4) { result.E = IccData.Fixed(bytes, 32); result.F = IccData.Fixed(bytes, 36); }
            }
        }
        if (result.A < 0 || result.C < 0 || result.D < 0 || result.G < 0 || (result.A * result.D) + result.B < 0 ||
            !float.IsFinite(result.A + result.B + result.C + result.D + result.E + result.F + result.G))
        { throw IccData.Invalid("invalid parametric curve coefficients."); }
        return result;
    }

    internal static IccCurve Table(ReadOnlyMemory<byte> data, int count, int bytesPerEntry)
    {
        if (count < 2 || count > 1 << 24 || bytesPerEntry is not (1 or 2)) { throw IccData.Invalid("invalid curve table dimensions."); }
        IccData.Require(data.Span, 0, checked(count * bytesPerEntry));
        return new() { table = data[..(count * bytesPerEntry)], count = count, bytesPerEntry = bytesPerEntry };
    }

    internal float Evaluate(float value)
    {
        if (this.count != 0)
        {
            float index = (float.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1)) * (this.count - 1);
            int low = (int)index, high = Math.Min(this.count - 1, low + 1);
            float a = this.Entry(low), b = this.Entry(high);
            return a + ((b - a) * (index - low));
        }
        float x = Math.Abs(value);
        float result = x < this.D ? (this.C * x) + this.F : MathF.Pow(Math.Max(0, (this.A * x) + this.B), this.G) + this.E;
        return BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(result) | (BitConverter.SingleToInt32Bits(value) & int.MinValue));
    }

    // Exact monotone table inversion avoids fitting away precision in output profiles.
    internal float Inverse(float value)
    {
        if (this.count != 0)
        {
            if (value <= this.Entry(0)) { return 0; }
            if (value >= this.Entry(this.count - 1)) { return 1; }
            int low = 0, high = this.count - 1;
            while (high - low > 1)
            {
                int middle = low + ((high - low) / 2);
                if (this.Entry(middle) <= value) { low = middle; } else { high = middle; }
            }
            float a = this.Entry(low), b = this.Entry(high);
            return (low + (a == b ? 0 : (value - a) / (b - a))) / (this.count - 1);
        }
        if (this.G <= 0 || this.A <= 0) { throw IccData.Invalid("destination curve cannot be inverted."); }
        float x = Math.Abs(value);
        float threshold = (this.C * this.D) + this.F;
        float result = x < threshold && this.C != 0 ? (x - this.F) / this.C
            : (MathF.Pow(Math.Max(0, x - this.E), 1 / this.G) - this.B) / this.A;
        return MathF.CopySign(result, value);
    }

    internal void ValidateInverse()
    {
        if (this.count == 0)
        {
            if (this.A <= 0 || this.G <= 0) { throw IccData.Invalid("destination curve cannot be inverted."); }
            return;
        }
        float previous = this.Entry(0);
        for (int i = 1; i < this.count; i++)
        {
            float value = this.Entry(i);
            if (value < previous) { throw IccData.Invalid("destination table curve is not monotone."); }
            previous = value;
        }
    }

    private float Entry(int index) => this.bytesPerEntry == 1 ? this.table.Span[index] / 255f : IccData.U16(this.table.Span, index * 2) / 65535f;
}
