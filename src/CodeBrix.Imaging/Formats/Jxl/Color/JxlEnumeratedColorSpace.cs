// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-color/src/{ciexyz,consts,convert,tf}.rs
// and tf/{bt709,pq}.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

using System;
using System.Numerics;
using CodeBrix.Imaging.ColorSpaces.Icc;
using CodeBrix.Imaging.Formats.Jxl.Headers;

namespace CodeBrix.Imaging.Formats.Jxl.Color;

internal sealed class JxlEnumeratedColorSpace
{
    private readonly JxlColorEncoding encoding;
    private readonly float intensity;
    private readonly IccMatrix toSrgb, fromSrgb;
    private readonly Vector3 luminance;
    internal readonly IccMatrix ToXyzD50;
    internal readonly IccMatrix AdaptToD50;
    internal readonly Vector3 WhiteXyz;
    internal bool IsSrgb => this.encoding.WhitePoint == 1 && this.encoding.Primaries == 1 &&
        !this.encoding.HasGamma && this.encoding.TransferFunction == 13 && this.encoding.ColorSpace is 0 or 1;
    internal static readonly IccMatrix SrgbToXyz = PrimariesMatrix(new() { Primaries = 1 }, new(.3127f, .329f));

    internal JxlEnumeratedColorSpace(JxlColorEncoding encoding, float intensity)
    {
        this.encoding = encoding;
        this.intensity = intensity;
        Vector2 white = encoding.WhitePoint switch
        {
            2 => new(encoding.White[0] * 1e-6f, encoding.White[1] * 1e-6f),
            10 => new(1f / 3), 11 => new(.314f, .351f), _ => new(.3127f, .329f),
        };
        IccMatrix xyz = PrimariesMatrix(encoding, white);
        this.luminance = new(xyz.Values[3], xyz.Values[4], xyz.Values[5]);
        this.WhiteXyz = Xyz(white);
        this.AdaptToD50 = Adapt(white, new(.345669f, .358496f));
        this.ToXyzD50 = this.AdaptToD50.Multiply(xyz);
        this.toSrgb = encoding.WhitePoint == 1 && encoding.Primaries == 1 ? IccMatrix.Identity
            : SrgbToXyz.Inverse().Multiply(encoding.RenderingIntent == 3 ? xyz : Adapt(white, new(.3127f, .329f)).Multiply(xyz));
        this.fromSrgb = this.toSrgb.Inverse();
    }

    internal Vector3 ToLinearSrgb(Vector3 value)
    {
        value = new(this.DecodeTransfer(value.X), this.DecodeTransfer(value.Y), this.DecodeTransfer(value.Z));
        if (!this.encoding.HasGamma && this.encoding.TransferFunction == 18) { value = this.HlgOotf(value, false); }
        return this.toSrgb.Apply(value);
    }

    internal Vector3 FromLinearSrgb(Vector3 value)
    {
        value = this.fromSrgb.Apply(value);
        if (this.encoding.ColorSpace == 1) { value = new(Vector3.Dot(value, this.luminance)); }
        if (!this.encoding.HasGamma && this.encoding.TransferFunction == 18) { value = this.HlgOotf(value, true); }
        return new(this.EncodeTransfer(value.X), this.EncodeTransfer(value.Y), this.EncodeTransfer(value.Z));
    }

    internal float DecodeTransfer(float value)
    {
        if (this.encoding.HasGamma) { return value <= 1e-7f ? 0 : MathF.Pow(value, 1e7f / this.encoding.Gamma); }
        float a = Math.Abs(value);
        return this.encoding.TransferFunction switch
        {
            1 => value <= .081f ? value / 4.5f : MathF.Pow((value + .099f) / 1.099f, 1f / .45f),
            13 => LinearSrgb(value),
            16 => PqToLinear(value, this.intensity),
            17 => value <= 1e-7f ? 0 : MathF.Pow(value, 2.6f),
            18 => MathF.CopySign(a <= .5f ? a * a / 3 : (MathF.Exp((a - .5599107f) / .17883277f) + .28466892f) / 12, value),
            _ => value,
        };
    }

    internal float EncodeTransfer(float value)
    {
        if (this.encoding.HasGamma) { return value <= 1e-7f ? 0 : MathF.Pow(value, this.encoding.Gamma * 1e-7f); }
        float a = Math.Abs(value);
        return this.encoding.TransferFunction switch
        {
            1 => value <= .018f ? value * 4.5f : (1.099f * MathF.Pow(value, .45f)) - .099f,
            13 => Srgb(value),
            16 => LinearToPq(value, this.intensity),
            17 => value <= 1e-7f ? 0 : MathF.Pow(value, 1f / 2.6f),
            18 => MathF.CopySign(a <= 1f / 12 ? MathF.Sqrt(3 * a) : (.17883277f * MathF.Log((12 * a) - .28466892f)) + .5599107f, value),
            _ => value,
        };
    }

    internal static float Srgb(float value)
    {
        float a = Math.Abs(value);
        return MathF.CopySign(a <= .0031308f ? 12.92f * a : (1.055f * MathF.Pow(a, 1f / 2.4f)) - .055f, value);
    }
    internal static float LinearSrgb(float value)
    {
        float a = Math.Abs(value);
        return MathF.CopySign(a <= .04045f ? a / 12.92f : MathF.Pow((a + .055f) / 1.055f, 2.4f), value);
    }
    internal static float LinearToPq(float value, float intensity)
    {
        double power = Math.Pow(Math.Abs(value) * (double)intensity / 10000, 2610.0 / 16384);
        return MathF.CopySign((float)Math.Pow(((2413.0 / 128 * power) + (107.0 / 128)) / ((2392.0 / 128 * power) + 1), 2523.0 / 32), value);
    }
    internal static float PqToLinear(float value, float intensity)
    {
        double power = Math.Pow(Math.Abs(value), 32.0 / 2523);
        return MathF.CopySign((float)(Math.Pow(Math.Max(power - (107.0 / 128), 0) / ((2413.0 / 128) - (2392.0 / 128 * power)), 16384.0 / 2610) * 10000 / intensity), value);
    }
    private Vector3 HlgOotf(Vector3 value, bool inverse)
    {
        if (this.intensity is >= 295 and <= 305) { return value; }
        float gamma = 1.2f * MathF.Pow(1.111f, MathF.Log2(this.intensity / 1000));
        float y = Vector3.Dot(value, this.luminance);
        return y == 0 ? value : value * MathF.Pow(Math.Abs(y), inverse ? (1 - gamma) / gamma : gamma - 1);
    }

    private static IccMatrix PrimariesMatrix(JxlColorEncoding encoding, Vector2 white)
    {
        float[] p = encoding.Primaries switch
        {
            2 => Array.ConvertAll(encoding.CustomPrimaries, v => v * 1e-6f),
            9 => [.708f, .292f, .170f, .797f, .131f, .046f],
            11 => [.680f, .320f, .265f, .690f, .150f, .060f],
            _ => [.639998686f, .330010138f, .300003784f, .600003357f, .150002046f, .059997204f],
        };
        IccMatrix matrix = new([p[0], p[2], p[4], p[1], p[3], p[5], 1 - p[0] - p[1], 1 - p[2] - p[3], 1 - p[4] - p[5]]);
        Vector3 scale = matrix.Inverse().Apply(Xyz(white));
        for (int r = 0; r < 3; r++)
        { matrix.Values[r * 3] *= scale.X; matrix.Values[(r * 3) + 1] *= scale.Y; matrix.Values[(r * 3) + 2] *= scale.Z; }
        return matrix;
    }
    private static Vector3 Xyz(Vector2 white)
    {
        if (white.Y == 0) { JxlThrowHelper.Invalid("invalid color white point."); }
        return new(white.X / white.Y, 1, ((1 - white.X) / white.Y) - 1);
    }
    private static IccMatrix Adapt(Vector2 from, Vector2 to)
    {
        if (from == to) { return IccMatrix.Identity; }
        IccMatrix bradford = new([.8951f, .2664f, -.1614f, -.7502f, 1.7135f, .0367f, .0389f, -.0685f, 1.0296f]);
        IccMatrix inverse = new([.9869929f, -.1470543f, .1599627f, .4323053f, .5183603f, .0492912f, -.0085287f, .0400428f, .9684867f]);
        Vector3 scale = bradford.Apply(Xyz(to)) / bradford.Apply(Xyz(from));
        for (int c = 0; c < 3; c++)
        { bradford.Values[c] *= scale.X; bradford.Values[c + 3] *= scale.Y; bradford.Values[c + 6] *= scale.Z; }
        return inverse.Multiply(bradford);
    }
}
