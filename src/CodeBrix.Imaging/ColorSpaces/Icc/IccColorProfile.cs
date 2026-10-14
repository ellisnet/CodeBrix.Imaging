// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// ICC parsing, PCS conversion and standard sRGB data adapted from skcms
// skcms.cc and src/Transform_inl.h, 7e572ada698a60ff50bd9038fd6e01e44f40868a
// (BSD-3-Clause). Copyright (c) 2018 Google Inc. All rights reserved.
// See THIRD-PARTY-NOTICES.txt for the complete upstream notice.

using System;
using System.Collections.Generic;
using System.Numerics;

namespace CodeBrix.Imaging.ColorSpaces.Icc;

internal sealed class IccColorProfile
{
    internal int Channels;
    internal bool Cmyk, Lab;
    internal IccCurve[] Curves;
    internal IccMatrix ToXyzMatrix;
    internal IccLut ToPcs, FromPcs;
    internal byte HdrTransfer;
    private Lazy<IccMatrix> inverse;

    internal static readonly IccMatrix SrgbToXyzD50 = new(
        [.436065674f, .385147095f, .143066406f, .222488403f, .716873169f, .060607910f, .013916016f, .097076416f, .714096069f]);
    internal static readonly IccColorProfile Srgb = CreateRgb(SrgbToXyzD50, IccCurve.Srgb);
    internal static readonly IccColorProfile LinearSrgb = CreateRgb(SrgbToXyzD50, IccCurve.Identity);

    internal static IccColorProfile CreateRgb(IccMatrix toXyz, IccCurve curve)
    {
        IccColorProfile profile = new() { Channels = 3, Curves = [curve, curve, curve], ToXyzMatrix = toXyz };
        profile.inverse = new(toXyz.Inverse);
        return profile;
    }

    internal static IccColorProfile Read(ReadOnlyMemory<byte> data, uint renderingIntent = 1)
    {
        ReadOnlySpan<byte> bytes = data.Span;
        IccData.Require(bytes, 0, 132);
        int size = IccData.Offset(bytes, 0), count = IccData.Offset(bytes, 128);
        if (IccData.U32(bytes, 36) != 0x61637370 || size > bytes.Length || 132L + (12L * count) > size || bytes[8] > 4)
        { throw IccData.Invalid("invalid profile header or tag table."); }
        float whiteX = IccData.Fixed(bytes, 68), whiteY = IccData.Fixed(bytes, 72), whiteZ = IccData.Fixed(bytes, 76);
        if (Math.Abs(whiteX - .9642f) > .01f || Math.Abs(whiteY - 1) > .01f || Math.Abs(whiteZ - .8249f) > .01f)
        { throw IccData.Invalid("profile connection illuminant is not D50."); }
        uint color = IccData.U32(bytes, 16), pcs = IccData.U32(bytes, 20);
        IccColorProfile result = new() { Channels = color switch { 0x47524159 => 1, 0x52474220 => 3, 0x434d594b => 4, _ => 0 }, Cmyk = color == 0x434d594b, Lab = pcs == 0x4c616220 };
        if (result.Channels == 0 || pcs is not (0x58595a20 or 0x4c616220)) { throw IccData.Invalid("unsupported profile color space."); }
        Dictionary<uint, ReadOnlyMemory<byte>> tags = new();
        for (int i = 0; i < count; i++)
        {
            int entry = 132 + (12 * i), offset = IccData.Offset(bytes, entry + 4), length = IccData.Offset(bytes, entry + 8);
            if (length < 4 || offset > size - length) { throw IccData.Invalid("tag exceeds the profile."); }
            tags.TryAdd(IccData.U32(bytes, entry), data.Slice(offset, length));
        }
        if (result.Channels == 1 && tags.TryGetValue(0x6b545243, out ReadOnlyMemory<byte> gray))
        {
            IccCurve curve = IccCurve.Read(gray, out _); result.Curves = [curve];
            if (!result.Lab) { result.ToXyzMatrix = new([whiteX, 0, 0, 0, whiteY, 0, 0, 0, whiteZ]); }
        }
        else if (result.Channels == 3)
        {
            if (tags.TryGetValue(0x72545243, out ReadOnlyMemory<byte> r) && tags.TryGetValue(0x67545243, out ReadOnlyMemory<byte> g) && tags.TryGetValue(0x62545243, out ReadOnlyMemory<byte> b))
            { result.Curves = [IccCurve.Read(r, out _), IccCurve.Read(g, out _), IccCurve.Read(b, out _)]; }
            if (tags.TryGetValue(0x7258595a, out r) && tags.TryGetValue(0x6758595a, out g) && tags.TryGetValue(0x6258595a, out b))
            {
                Vector3 rx = ReadXyz(r), gx = ReadXyz(g), bx = ReadXyz(b);
                result.ToXyzMatrix = new([rx.X, gx.X, bx.X, rx.Y, gx.Y, bx.Y, rx.Z, gx.Z, bx.Z]);
            }
        }
        uint intent = renderingIntent == 3 ? 1 : renderingIntent;
        if (intent > 2) { throw IccData.Invalid("invalid rendering intent."); }
        ReadOnlySpan<uint> priority = [intent, 0, 1, 2];
        foreach (uint candidate in priority)
        {
            if (result.ToPcs == null && tags.TryGetValue(0x41324230 + candidate, out ReadOnlyMemory<byte> tag))
            { result.ToPcs = IccLut.Read(tag, false, !result.Lab); }
            if (result.FromPcs == null && tags.TryGetValue(0x42324130 + candidate, out tag))
            { result.FromPcs = IccLut.Read(tag, true, !result.Lab); }
        }
        if (result.ToPcs != null && result.ToPcs.InputChannels != result.Channels) { throw IccData.Invalid("A2B channel count does not match the profile."); }
        if (result.FromPcs != null && result.FromPcs.OutputChannels != result.Channels) { throw IccData.Invalid("B2A channel count does not match the profile."); }
        if (result.ToXyzMatrix != null) { result.inverse = new(result.ToXyzMatrix.Inverse); }
        if (tags.TryGetValue(0x63696370, out ReadOnlyMemory<byte> cicp))
        {
            IccData.Require(cicp.Span, 0, 12);
            if (IccData.U32(cicp.Span, 0) != 0x63696370) { throw IccData.Invalid("invalid CICP tag."); }
            if (result.Channels == 3 && result.ToXyzMatrix != null && cicp.Span[10] == 0 && cicp.Span[11] == 1 && cicp.Span[9] is 16 or 18)
            { result.HdrTransfer = cicp.Span[9]; }
        }
        if (result.HdrTransfer == 0 && result.ToPcs == null && (result.Curves == null || result.ToXyzMatrix == null))
        { throw IccData.Invalid("profile has no usable source transform."); }
        return result;
    }

    internal Vector3 ToXyz(ReadOnlySpan<float> source, bool invertedCmyk = false)
    {
        if (this.HdrTransfer != 0)
        {
            Vector3 rgb = new(IccHdrTransfer.Decode(source[0], this.HdrTransfer), IccHdrTransfer.Decode(source[1], this.HdrTransfer), IccHdrTransfer.Decode(source[2], this.HdrTransfer));
            Vector3 xyz = this.ToXyzMatrix.Apply(rgb);
            return this.HdrTransfer == 18 ? IccHdrTransfer.HlgOotf(xyz, false) : xyz;
        }
        if (this.ToPcs != null)
        {
            Span<float> channels = stackalloc float[4]; channels.Clear(); source[..this.Channels].CopyTo(channels);
            if (invertedCmyk && this.Cmyk) { for (int c = 0; c < 4; c++) { channels[c] = 1 - channels[c]; } }
            this.ToPcs.Apply(channels);
            Vector3 value = new(channels[0], channels[1], channels[2]);
            return this.Lab ? LabToXyz(value) : value;
        }
        Vector3 linear = this.Channels == 1 ? new(this.Curves[0].Evaluate(source[0]))
            : new(this.Curves[0].Evaluate(source[0]), this.Curves[1].Evaluate(source[1]), this.Curves[2].Evaluate(source[2]));
        return this.ToXyzMatrix.Apply(linear);
    }

    internal void FromXyz(Vector3 xyz, Span<float> destination)
    {
        if (this.HdrTransfer != 0)
        {
            if (this.HdrTransfer == 18) { xyz = IccHdrTransfer.HlgOotf(xyz, true); }
            Vector3 rgb = this.inverse.Value.Apply(xyz);
            destination[0] = IccHdrTransfer.Encode(rgb.X, this.HdrTransfer);
            destination[1] = IccHdrTransfer.Encode(rgb.Y, this.HdrTransfer);
            destination[2] = IccHdrTransfer.Encode(rgb.Z, this.HdrTransfer);
            return;
        }
        if (this.FromPcs != null)
        {
            Span<float> channels = stackalloc float[4];
            Vector3 value = this.Lab ? XyzToLab(xyz) : xyz;
            channels[0] = value.X; channels[1] = value.Y; channels[2] = value.Z; channels[3] = 0;
            this.FromPcs.Apply(channels); channels[..this.Channels].CopyTo(destination); return;
        }
        if (this.Curves == null || this.inverse == null) { throw IccData.Invalid("profile has no usable destination transform."); }
        if (this.Channels == 1) { destination[0] = this.Curves[0].Inverse(xyz.Y); return; }
        Vector3 linear = this.inverse.Value.Apply(xyz);
        destination[0] = this.Curves[0].Inverse(linear.X); destination[1] = this.Curves[1].Inverse(linear.Y); destination[2] = this.Curves[2].Inverse(linear.Z);
    }

    internal void ValidateDestination()
    {
        if (this.HdrTransfer != 0) { _ = this.inverse.Value; return; }
        if (this.FromPcs != null) { return; }
        if (this.Curves == null || this.inverse == null) { throw IccData.Invalid("profile has no usable destination transform."); }
        _ = this.inverse.Value;
        foreach (IccCurve curve in this.Curves) { curve.ValidateInverse(); }
    }

    private static Vector3 ReadXyz(ReadOnlyMemory<byte> data)
    {
        if (IccData.U32(data.Span, 0) != 0x58595a20) { throw IccData.Invalid("invalid XYZ tag."); }
        return new(IccData.Fixed(data.Span, 8), IccData.Fixed(data.Span, 12), IccData.Fixed(data.Span, 16));
    }
    private static Vector3 LabToXyz(Vector3 lab)
    {
        float y = ((lab.X * 100) + 16) / 116, x = y + (((lab.Y * 255) - 128) / 500), z = y - (((lab.Z * 255) - 128) / 200);
        static float Convert(float v) => v * v * v > .008856f ? v * v * v : (v - (16f / 116)) / 7.787f;
        return new(Convert(x) * .9642f, Convert(y), Convert(z) * .8249f);
    }
    private static Vector3 XyzToLab(Vector3 xyz)
    {
        static float Convert(float v) => v > .008856f ? MathF.Cbrt(v) : (v * 7.787f) + (16f / 116);
        float x = Convert(xyz.X / .9642f), y = Convert(xyz.Y), z = Convert(xyz.Z / .8249f);
        return new(((y * 116) - 16) / 100, (((x - y) * 500) + 128) / 255, (((y - z) * 200) + 128) / 255);
    }
}
