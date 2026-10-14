// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;
using CodeBrix.Imaging.Formats.Jxl.Headers;

namespace CodeBrix.Imaging.Formats.Jxl;

/// <summary>Advanced image-header controls for raw JPEG XL encoding.</summary>
public sealed class JxlRawImageEncoding
{
    /// <summary>Gets or sets an optional preview with the same channel descriptions as the image.</summary>
    /// <remarks>The caller retains ownership of the preview frame. It must be an ordinary replacement frame without reference dependencies.</remarks>
    public JxlRawFrame Preview { get; set; }
    /// <summary>Gets or sets an enumerated color encoding instead of the default sRGB/gray encoding. An ICC profile cannot be supplied at the same time.</summary>
    public JxlColorEncodingInfo ColorEncoding { get; set; }
    /// <summary>Gets or sets sixteen opsin values: a row-major 3x3 inverse matrix, three biases, three quantization biases and one numerator.</summary>
    /// <remarks>Values are rounded to binary16 before encoding. Null uses the standard matrix and biases.</remarks>
    public float[] OpsinInverse { get; set; }
    /// <summary>Returns a fresh copy of the standard sixteen opsin parameters.</summary>
    public static float[] DefaultOpsinInverse => new JxlImageHeader().Opsin;
    /// <summary>Gets or sets the fifteen custom 2x upsampling weights; null uses the standard weights.</summary>
    public float[] UpsamplingWeights2 { get; set; }
    /// <summary>Gets or sets the 55 custom 4x upsampling weights; null uses the standard weights.</summary>
    public float[] UpsamplingWeights4 { get; set; }
    /// <summary>Gets or sets the 210 custom 8x upsampling weights; null uses the standard weights.</summary>
    public float[] UpsamplingWeights8 { get; set; }
    /// <summary>Gets or sets the intrinsic display dimensions independently of the coded canvas.</summary>
    public Size? IntrinsicSize { get; set; }
    /// <summary>Gets or sets the minimum luminance in nits.</summary>
    public float MinimumNits { get; set; }
    /// <summary>Gets or sets whether the linear segment's threshold is relative to maximum display luminance.</summary>
    public bool RelativeToMaximumDisplay { get; set; }
    /// <summary>Gets or sets the threshold below which tone mapping is linear.</summary>
    public float LinearBelow { get; set; }

    internal void Apply(JxlImageHeader header)
    {
        if (this.ColorEncoding != null)
        {
            if (header.Color.WantIcc) { throw new ArgumentException("Choose an ICC profile or an enumerated color encoding."); }
            header.Color = this.ColorEncoding.ToHeader(header.Color.ColorSpace);
        }
        if (this.IntrinsicSize is Size size)
        {
            if (size.Width is <= 0 or > 1 << 30 || size.Height is <= 0 or > 1 << 30) { throw new ArgumentException("Invalid intrinsic dimensions."); }
            header.IntrinsicSize = new((uint)size.Width, (uint)size.Height);
        }
        header.MinNits = HalfValue(this.MinimumNits); header.LinearBelow = HalfValue(this.LinearBelow);
        header.RelativeToMaxDisplay = this.RelativeToMaximumDisplay;
        if (header.MinNits < 0 || header.MinNits > header.IntensityTarget || header.LinearBelow < 0 || (header.RelativeToMaxDisplay && header.LinearBelow > 1))
        { throw new ArgumentException("Invalid tone-mapping metadata."); }
        if (this.OpsinInverse != null)
        {
            if (!header.XybEncoded) { throw new ArgumentException("An opsin matrix requires XYB encoding."); }
            header.Opsin = Weights(this.OpsinInverse, 16); header.CustomOpsin = true;
        }
        if (this.UpsamplingWeights2 != null) { header.Up2 = Weights(this.UpsamplingWeights2, 15); header.CustomWeightsMask |= 1; }
        if (this.UpsamplingWeights4 != null) { header.Up4 = Weights(this.UpsamplingWeights4, 55); header.CustomWeightsMask |= 2; }
        if (this.UpsamplingWeights8 != null) { header.Up8 = Weights(this.UpsamplingWeights8, 210); header.CustomWeightsMask |= 4; }
    }

    private static float[] Weights(float[] input, int count)
    {
        if (input.Length != count) { throw new ArgumentException($"Expected {count} weights."); }
        float[] result = new float[count]; for (int i = 0; i < count; i++) { result[i] = HalfValue(input[i]); } return result;
    }
    private static float HalfValue(float value)
    { float result = (float)(Half)value; if (!float.IsFinite(result)) { throw new ArgumentException("Header values must be finite at binary16 precision."); } return result; }
}

/// <summary>An enumerated JPEG XL color encoding using the numeric identifiers defined by the format.</summary>
public sealed class JxlColorEncodingInfo
{
    /// <summary>Gets or sets the white point: 1=D65, 2=custom, 10=equal energy, 11=DCI.</summary>
    public int WhitePoint { get; set; } = 1;
    /// <summary>Gets or sets custom CIE xy white-point coordinates.</summary>
    public Vector2 WhitePointCoordinates { get; set; } = new(.3127f, .329f);
    /// <summary>Gets or sets the primaries: 1=sRGB, 2=custom, 9=BT.2020, 11=P3.</summary>
    public int Primaries { get; set; } = 1;
    /// <summary>Gets or sets custom red CIE xy coordinates.</summary>
    public Vector2 Red { get; set; } = new(.64f, .33f);
    /// <summary>Gets or sets custom green CIE xy coordinates.</summary>
    public Vector2 Green { get; set; } = new(.3f, .6f);
    /// <summary>Gets or sets custom blue CIE xy coordinates.</summary>
    public Vector2 Blue { get; set; } = new(.15f, .06f);
    /// <summary>Gets or sets the transfer function: 1=BT.709, 2=unknown, 8=linear, 13=sRGB, 16=PQ, 17=DCI, 18=HLG.</summary>
    public int TransferFunction { get; set; } = 13;
    /// <summary>Gets or sets the exponent mapping linear light to encoded samples, overriding the transfer function when present.</summary>
    public float? Gamma { get; set; }
    /// <summary>Gets or sets rendering intent: 0=perceptual, 1=relative, 2=saturation, 3=absolute.</summary>
    public int RenderingIntent { get; set; } = 1;

    internal JxlColorEncoding ToHeader(uint colorSpace)
    {
        if (this.WhitePoint is not (1 or 2 or 10 or 11) || this.Primaries is not (1 or 2 or 9 or 11) ||
            this.TransferFunction is not (1 or 2 or 8 or 13 or 16 or 17 or 18) || this.RenderingIntent is < 0 or > 3)
        { throw new ArgumentException("Invalid enumerated JPEG XL color encoding."); }
        uint gamma = 0;
        if (this.Gamma is float value)
        {
            double scaled = Math.Round((double)value * 1e7);
            if (!double.IsFinite(scaled) || scaled < 1 || scaled > 0xFFFFFF) { throw new ArgumentOutOfRangeException(nameof(this.Gamma)); }
            gamma = (uint)scaled;
        }
        return new() { ColorSpace = colorSpace, WhitePoint = (uint)this.WhitePoint, Primaries = (uint)this.Primaries,
            TransferFunction = (uint)this.TransferFunction, RenderingIntent = (uint)this.RenderingIntent, HasGamma = this.Gamma.HasValue, Gamma = gamma,
            White = [Coordinate(this.WhitePointCoordinates.X), Coordinate(this.WhitePointCoordinates.Y)],
            CustomPrimaries = [Coordinate(this.Red.X), Coordinate(this.Red.Y), Coordinate(this.Green.X), Coordinate(this.Green.Y), Coordinate(this.Blue.X), Coordinate(this.Blue.Y)] };
    }

    private static int Coordinate(float value)
    {
        double scaled = Math.Round((double)value * 1e6);
        if (!double.IsFinite(scaled) || scaled < -2097152 || scaled > 2097151) { throw new ArgumentException("Chromaticity coordinates exceed JPEG XL's range."); }
        return (int)scaled;
    }
}
