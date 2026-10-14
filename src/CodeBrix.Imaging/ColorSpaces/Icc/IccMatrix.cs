// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Matrix math adapted from jxl-oxide crates/jxl-color/src/ciexyz.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

using System;
using System.Numerics;

namespace CodeBrix.Imaging.ColorSpaces.Icc;

internal sealed class IccMatrix(float[] values)
{
    internal readonly float[] Values = values;
    internal static readonly IccMatrix Identity = new([1, 0, 0, 0, 1, 0, 0, 0, 1]);
    internal Vector3 Apply(Vector3 value)
    {
        float[] m = this.Values;
        return new((m[0] * value.X) + (m[1] * value.Y) + (m[2] * value.Z) + (m.Length == 12 ? m[9] : 0),
            (m[3] * value.X) + (m[4] * value.Y) + (m[5] * value.Z) + (m.Length == 12 ? m[10] : 0),
            (m[6] * value.X) + (m[7] * value.Y) + (m[8] * value.Z) + (m.Length == 12 ? m[11] : 0));
    }
    internal IccMatrix Inverse()
    {
        float[] m = this.Values;
        double det = ((double)m[0] * (((double)m[4] * m[8]) - ((double)m[5] * m[7]))) +
            ((double)m[1] * (((double)m[5] * m[6]) - ((double)m[3] * m[8]))) +
            ((double)m[2] * (((double)m[3] * m[7]) - ((double)m[4] * m[6])));
        if (!double.IsFinite(det) || det == 0) { throw IccData.Invalid("singular color matrix."); }
        float[] inverse = new float[9];
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                int r0 = (column + 1) % 3, r1 = (column + 2) % 3, c0 = (row + 1) % 3, c1 = (row + 2) % 3;
                inverse[(row * 3) + column] = (float)((((double)m[(r0 * 3) + c0] * m[(r1 * 3) + c1]) - ((double)m[(r0 * 3) + c1] * m[(r1 * 3) + c0])) / det);
                if (!float.IsFinite(inverse[(row * 3) + column])) { throw IccData.Invalid("nonfinite inverse color matrix."); }
            }
        }
        return new(inverse);
    }
    internal IccMatrix Multiply(IccMatrix right)
    {
        float[] result = new float[9];
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            { for (int k = 0; k < 3; k++) { result[(r * 3) + c] += this.Values[(r * 3) + k] * right.Values[(k * 3) + c]; } }
        }
        return new(result);
    }
}
