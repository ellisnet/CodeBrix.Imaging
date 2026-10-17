// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;
using CodeBrix.Imaging.ColorSpaces.Icc;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Formats.Jxl.Rendering;

namespace CodeBrix.Imaging.Formats.Jxl.Color;

internal sealed class JxlOutputColorTransform
{
    private static readonly IccMatrix XyzToSrgb = IccColorProfile.SrgbToXyzD50.Inverse();
    private readonly JxlImageHeader header;
    private readonly IccColorProfile profile;
    private readonly JxlEnumeratedColorSpace enumerated;
    private readonly JxlToneMapper toneMapper;
    internal readonly bool OutputSrgb;
    internal readonly bool OutputLinear;
    internal readonly int BlackIndex;
    internal readonly bool PreserveCoded;

    internal JxlOutputColorTransform(JxlImageHeader header, byte[] icc, JxlDecoder options, bool raw)
    {
        this.header = header;
        this.BlackIndex = Array.FindIndex(header.ExtraChannels, e => e.Type == 4);
        this.PreserveCoded = raw && !options.Coalesce && options.TargetColor == JxlTargetColor.Auto;
        this.OutputLinear = options.TargetColor == JxlTargetColor.LinearSrgb;
        this.OutputSrgb = options.TargetColor is JxlTargetColor.Srgb or JxlTargetColor.LinearSrgb ||
            (options.TargetColor == JxlTargetColor.Auto && !this.PreserveCoded && (header.XybEncoded || (!raw && this.BlackIndex >= 0)));
        if (!raw && this.BlackIndex >= 0 && !this.OutputSrgb)
        { throw new ArgumentException("CMYK original-color output requires DecodeRaw.", nameof(options.TargetColor)); }
        if (icc != null && (this.OutputSrgb || (header.XybEncoded && !this.PreserveCoded)))
        {
            this.profile = IccColorProfile.Read(icc, header.Color.RenderingIntent);
            int expected = this.BlackIndex >= 0 ? 4 : (int)(header.Color.ColorSpace == 1 ? 1 : 3);
            if (this.profile.Channels != expected) { JxlThrowHelper.Invalid("ICC color space does not match the image channels."); }
            if (!this.OutputSrgb) { this.profile.ValidateDestination(); }
        }
        if (icc == null) { this.enumerated = new(header.Color, header.IntensityTarget); }
        if (this.OutputSrgb && options.TargetDisplayLuminance > 0 && options.TargetDisplayLuminance != header.IntensityTarget)
        {
            uint transfer = this.profile?.HdrTransfer ?? header.Color.TransferFunction;
            if (transfer is 16 or 18)
            { this.toneMapper = new(header.IntensityTarget, 0, options.TargetDisplayLuminance, transfer == 18); }
        }
    }

    private static readonly byte[] LinearSrgbIcc = JxlIccSynthesis.Create(new JxlColorEncoding { TransferFunction = 8 });

    // The ICC profile decoded images carry: the file's own for original-color output, none for sRGB
    // (Imaging's default), and a synthesized linear-sRGB profile for linear-sRGB output.
    internal byte[] MetadataIcc(byte[] icc) => this.OutputLinear ? (byte[])LinearSrgbIcc.Clone() : this.OutputSrgb ? null : icc;

    internal bool IsIdentity(JxlFloatFrame frame) => this.PreserveCoded ||
        (!this.OutputLinear && frame.ColorTransformed && !frame.LinearSrgb && (!this.OutputSrgb || this.enumerated?.IsSrgb == true));

    internal Vector3 Convert(Vector3 value, float black, JxlFloatFrame frame, JxlColorTransform frameTransform, bool forceLinear = false)
    {
        if (this.PreserveCoded) { return value; }
        bool linear = frame.LinearSrgb || forceLinear;
        if (frameTransform != null)
        {
            value = frameTransform.Convert(value);
            linear = this.header.XybEncoded && this.header.Color.WantIcc;
        }
        if (!linear && (!this.OutputSrgb || (!this.OutputLinear && this.enumerated?.IsSrgb == true))) { return value; }
        if (linear)
        {
            if (this.OutputSrgb) { return this.EncodeSrgb(value); }
            Span<float> result = stackalloc float[4];
            if (this.profile.HdrTransfer != 0) { value *= this.header.IntensityTarget / 203; }
            this.profile.FromXyz(IccColorProfile.SrgbToXyzD50.Apply(value), result);
            return this.profile.Channels == 1 ? new(result[0]) : new(result[0], result[1], result[2]);
        }
        if (this.profile != null)
        {
            Span<float> channels = stackalloc float[4] { value.X, value.Y, value.Z, black };
            Vector3 xyz = this.profile.ToXyz(channels, this.BlackIndex >= 0);
            if (this.toneMapper != null)
            {
                value = XyzToSrgb.Apply(xyz);
                if (this.profile.HdrTransfer != 0) { value *= 203 / this.header.IntensityTarget; }
                return this.EncodeSrgb(value);
            }
            if (this.OutputLinear) { return XyzToSrgb.Apply(xyz); }
            IccColorProfile.Srgb.FromXyz(xyz, channels);
            return new(channels[0], channels[1], channels[2]);
        }
        value = this.enumerated.ToLinearSrgb(value);
        return this.EncodeSrgb(value);
    }

    private Vector3 EncodeSrgb(Vector3 value)
    {
        if (this.toneMapper != null) { value = this.toneMapper.Apply(value); }
        if (this.OutputLinear) { return value; }
        return new(JxlEnumeratedColorSpace.Srgb(value.X), JxlEnumeratedColorSpace.Srgb(value.Y), JxlEnumeratedColorSpace.Srgb(value.Z));
    }
}
