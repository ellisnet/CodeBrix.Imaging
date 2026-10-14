// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// XYB and YCbCr transforms adapted from jxl-oxide crates/jxl-color/src/
// {xyb,ycbcr,convert}.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

using System;
using System.Numerics;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Rendering;

namespace CodeBrix.Imaging.Formats.Jxl.Color;

internal sealed class JxlColorTransform
{
    private readonly JxlImageHeader image;
    private readonly bool ycbcr;
    private readonly Vector3 bias;
    private readonly Vector3 cubeRootBias;
    private readonly JxlEnumeratedColorSpace original;
    private readonly bool linearOutput;

    internal JxlColorTransform(JxlImageHeader image, bool ycbcr, bool linearOutput = false)
    {
        this.image = image;
        this.ycbcr = ycbcr;
        this.linearOutput = linearOutput;
        this.bias = new(image.Opsin[9], image.Opsin[10], image.Opsin[11]);
        this.cubeRootBias = new(MathF.Cbrt(this.bias.X), MathF.Cbrt(this.bias.Y), MathF.Cbrt(this.bias.Z));
        if (image.XybEncoded && !image.Color.WantIcc && !linearOutput) { this.original = new(image.Color, image.IntensityTarget); }
    }

    internal Vector3 Convert(Vector3 value)
    {
        if (this.ycbcr)
        {
            float y = value.Y + (128f / 255);
            return new(MathF.FusedMultiplyAdd(value.Z, 1.402f, y),
                MathF.FusedMultiplyAdd(value.X, -0.114f * 1.772f / 0.587f, MathF.FusedMultiplyAdd(value.Z, -0.299f * 1.402f / 0.587f, y)),
                MathF.FusedMultiplyAdd(value.X, 1.772f, y));
        }
        if (!this.image.XybEncoded) { return value; }
        Vector3 g = new Vector3(value.Y + value.X, value.Y - value.X, value.Z) - this.cubeRootBias;
        float scale = 255 / this.image.IntensityTarget;
        Vector3 mixed = new(MathF.FusedMultiplyAdd(g.X * g.X, g.X, this.bias.X) * scale,
            MathF.FusedMultiplyAdd(g.Y * g.Y, g.Y, this.bias.Y) * scale,
            MathF.FusedMultiplyAdd(g.Z * g.Z, g.Z, this.bias.Z) * scale);
        float[] matrix = this.image.Opsin;
        Vector3 linear = new((matrix[0] * mixed.X) + (matrix[1] * mixed.Y) + (matrix[2] * mixed.Z),
            (matrix[3] * mixed.X) + (matrix[4] * mixed.Y) + (matrix[5] * mixed.Z),
            (matrix[6] * mixed.X) + (matrix[7] * mixed.Y) + (matrix[8] * mixed.Z));
        return this.original?.FromLinearSrgb(linear) ?? linear;
    }

    internal void Apply(JxlFloatFrame frame, CancellationToken cancellationToken)
    {
        if (frame.ColorTransformed) { return; }
        for (int y = 0; y < frame.Planes[0].Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Span<float> r = frame.Planes[0].DangerousGetRowSpan(y), g = frame.Planes[1].DangerousGetRowSpan(y), b = frame.Planes[2].DangerousGetRowSpan(y);
            for (int x = 0; x < r.Length; x++)
            {
                Vector3 result = this.Convert(new(r[x], g[x], b[x]));
                r[x] = result.X; g[x] = result.Y; b[x] = result.Z;
            }
        }
        frame.ColorTransformed = true;
        frame.LinearSrgb = this.image.XybEncoded && (this.image.Color.WantIcc || this.linearOutput);
    }
}
