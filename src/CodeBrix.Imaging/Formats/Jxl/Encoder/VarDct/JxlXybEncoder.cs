// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from libjxl lib/jxl/enc_xyb.cc and cms/opsin_params.h,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using System.Numerics;
using CodeBrix.Imaging.ColorSpaces.Icc;
using CodeBrix.Imaging.Formats.Jxl.Headers;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.VarDct;

internal sealed class JxlXybEncoder
{
    private const float Bias = .0037930732552754493f;
    private readonly float[] matrix;
    private readonly Vector3 biases = new(Bias);
    private readonly Vector3 cubeRootBias = new(MathF.Cbrt(Bias));
    private readonly bool custom;

    internal JxlXybEncoder(JxlImageHeader header) : this(header.IntensityTarget)
    {
        if (!header.CustomOpsin) { return; }
        this.custom = true;
        this.matrix = new IccMatrix(header.Opsin[..9]).Inverse().Values;
        for (int i = 0; i < 9; i++) { this.matrix[i] *= header.IntensityTarget / 255; }
        this.biases = new(-header.Opsin[9], -header.Opsin[10], -header.Opsin[11]);
        this.cubeRootBias = new(MathF.Cbrt(this.biases.X), MathF.Cbrt(this.biases.Y), MathF.Cbrt(this.biases.Z));
    }

    internal JxlXybEncoder(float intensityTarget)
    {
        if (!float.IsFinite(intensityTarget) || intensityTarget <= 0) { throw new ArgumentOutOfRangeException(nameof(intensityTarget)); }
        float scale = intensityTarget / 255;
        this.matrix = [.30f, 1 - .078f - .30f, .078f, .23f, 1 - .078f - .23f, .078f,
            .24342268924547819f, .20476744424496821f, 1 - .24342268924547819f - .20476744424496821f];
        for (int i = 0; i < this.matrix.Length; i++) { this.matrix[i] *= scale; }
    }

    internal Vector3 FromLinearSrgb(Vector3 rgb)
    {
        float[] m = this.matrix;
        float r = Root(0), g = Root(3), b = Root(6);
        return new((r - g) * .5f, (r + g) * .5f, b);

        float Root(int offset)
        {
            float value = MathF.FusedMultiplyAdd(m[offset], rgb.X,
                MathF.FusedMultiplyAdd(m[offset + 1], rgb.Y, MathF.FusedMultiplyAdd(m[offset + 2], rgb.Z, this.biases[offset / 3])));
            return MathF.Cbrt(this.custom ? value : Math.Max(0, value)) - this.cubeRootBias[offset / 3];
        }
    }
}
