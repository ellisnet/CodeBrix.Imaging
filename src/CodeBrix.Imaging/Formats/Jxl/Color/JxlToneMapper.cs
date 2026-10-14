// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from libjxl lib/jxl/cms/tone_mapping.h and
// lib/jxl/render_pipeline/stage_tone_mapping.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause + PATENTS).
// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// See THIRD-PARTY-NOTICES.txt for the complete upstream notice.

using System;
using System.Numerics;
using CodeBrix.Imaging.ColorSpaces.Icc;

namespace CodeBrix.Imaging.Formats.Jxl.Color;

internal sealed class JxlToneMapper
{
    private static readonly Vector3 Luminances = new(.212639f, .7151687f, .0721923f);
    private readonly float sourcePeak, targetPeak, sourceMinPq, sourceRangePq, minimum, maximum, knee, hlgExponent;
    private readonly bool hlg;

    internal JxlToneMapper(float sourcePeak, float sourceMin, float targetPeak, bool hlg)
    {
        this.sourcePeak = sourcePeak; this.targetPeak = targetPeak; this.hlg = hlg;
        this.hlgExponent = MathF.Pow(1.111f, MathF.Log2(targetPeak / sourcePeak)) - 1;
        this.sourceMinPq = Pq(sourceMin);
        this.sourceRangePq = Pq(sourcePeak) - this.sourceMinPq;
        this.minimum = (Pq(0) - this.sourceMinPq) / this.sourceRangePq;
        this.maximum = (Pq(targetPeak) - this.sourceMinPq) / this.sourceRangePq;
        this.knee = (1.5f * this.maximum) - .5f;
    }

    internal Vector3 Apply(Vector3 rgb)
    {
        if (this.hlg)
        {
            if (Math.Abs(this.hlgExponent) > .01f)
            {
                float y = Math.Max(0, Vector3.Dot(rgb, Luminances));
                rgb *= Math.Min(MathF.Pow(y, this.hlgExponent), 1e9f);
            }
            return this.hlgExponent < 0 ? MapGamut(rgb) : rgb;
        }
        if (this.targetPeak >= this.sourcePeak) { return rgb; }
        float luminance = this.sourcePeak * Vector3.Dot(rgb, Luminances);
        float normalized = Math.Min(1, (Pq(luminance) - this.sourceMinPq) / this.sourceRangePq);
        float compressed = normalized;
        if (normalized >= this.knee)
        {
            float t = (normalized - this.knee) / Math.Max(1e-6f, 1 - this.knee), t2 = t * t, t3 = t2 * t;
            compressed = (((2 * t3) - (3 * t2) + 1) * this.knee) +
                ((t3 - (2 * t2) + t) * (1 - this.knee)) + (((-2 * t3) + (3 * t2)) * this.maximum);
        }
        float inverse = 1 - compressed, squared = inverse * inverse;
        float mapped = (((this.minimum * squared * squared) + compressed) * this.sourceRangePq) + this.sourceMinPq;
        float nits = Math.Clamp(IccHdrTransfer.Decode(mapped, 16) * 203, 0, this.targetPeak);
        rgb = luminance <= 1e-6f ? new(nits / this.targetPeak) : rgb * (nits / luminance * (this.sourcePeak / this.targetPeak));
        return MapGamut(rgb);
    }

    internal static Vector3 MapGamut(Vector3 rgb)
    {
        float y = Vector3.Dot(rgb, Luminances), saturation = 0, luminance = 0;
        for (int c = 0; c < 3; c++)
        {
            float value = rgb[c], difference = value - y, inverse = 1 / (difference == 0 ? 1 : difference);
            float ratio = value * inverse;
            if (difference < 0) { saturation = Math.Max(saturation, ratio); }
            luminance = Math.Max(luminance, difference <= 0 ? saturation : ratio - inverse);
        }
        float mix = Math.Clamp((.1f * (saturation - luminance)) + luminance, 0, 1);
        rgb += mix * (new Vector3(y) - rgb);
        return rgb / Math.Max(1, Math.Max(rgb.X, Math.Max(rgb.Y, rgb.Z)));
    }

    private static float Pq(float nits) => IccHdrTransfer.Encode(nits / 203, 16);
}
