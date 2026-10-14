// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using CodeBrix.Imaging.Formats.Jxl.VarDct;

namespace CodeBrix.Imaging.Formats.Jxl;

/// <summary>Explicit per-channel dequantization steps for one JPEG XL transform family.</summary>
/// <remarks>
/// Larger values discard more precision. Weights use canonical coefficient order,
/// with the longer dimension as the row width; sub-8 transforms use an 8 by 8 layout.
/// Transposed transforms share a matrix, as do the four AFV orientations.
/// Encoding rounds weights to integral multiples of a shared power of two.
/// Values must remain positive after rounding and be finite and less than 100,000,000.
/// </remarks>
public sealed class JxlQuantizationMatrix
{
    /// <summary>Gets or sets a transform identifying the matrix family.</summary>
    public JxlDctTransform Transform { get; set; }
    /// <summary>Gets or sets X-channel dequantization steps in canonical coefficient order.</summary>
    public float[] X { get; set; } = [];
    /// <summary>Gets or sets Y-channel dequantization steps in canonical coefficient order.</summary>
    public float[] Y { get; set; } = [];
    /// <summary>Gets or sets B-channel dequantization steps in canonical coefficient order.</summary>
    public float[] B { get; set; } = [];

    /// <summary>Creates an independently editable copy of the format's default matrix.</summary>
    /// <param name="transform">The transform family.</param>
    /// <returns>A matrix containing copies of all three default weight arrays.</returns>
    public static JxlQuantizationMatrix CreateDefault(JxlDctTransform transform)
    {
        if (!Enum.IsDefined(transform)) { throw new ArgumentOutOfRangeException(nameof(transform)); }
        JxlTransformType type = (JxlTransformType)transform;
        return new()
        {
            Transform = transform,
            X = (float[])JxlDequantMatrices.Default.Get(type, 0).Clone(),
            Y = (float[])JxlDequantMatrices.Default.Get(type, 1).Clone(),
            B = (float[])JxlDequantMatrices.Default.Get(type, 2).Clone(),
        };
    }
}
