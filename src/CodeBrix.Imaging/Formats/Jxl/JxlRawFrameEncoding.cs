// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

namespace CodeBrix.Imaging.Formats.Jxl;

/// <summary>Explicit coding features applied to a raw frame's supplied residual samples.</summary>
/// <remarks>These features are added during decoding. They are not inferred from decoded frame metadata.</remarks>
public sealed class JxlRawFrameEncoding
{
    /// <summary>Gets or sets a transform to use wherever it fits; null selects transforms automatically.</summary>
    /// <remarks>Blocks at image or group boundaries that cannot fit the requested transform use an 8 by 8 DCT.</remarks>
    public JxlDctTransform? Transform { get; set; }
    /// <summary>Gets or sets custom VarDCT quantization matrices. Omitted families use the format defaults.</summary>
    public JxlQuantizationMatrix[] QuantizationMatrices { get; set; } = [];
    /// <summary>Gets or sets whether VarDCT takes its low-frequency samples from the previously encoded next-level LF frame.</summary>
    public bool UseLowFrequencyFrame { get; set; }
    /// <summary>Gets or sets patches copied from previously saved reference frames.</summary>
    public JxlPatch[] Patches { get; set; } = [];
    /// <summary>Gets or sets quantized splines painted over the supplied samples.</summary>
    public JxlSpline[] Splines { get; set; } = [];
    /// <summary>Gets or sets the shared spline quantization adjustment.</summary>
    public int SplineQuantizationAdjustment { get; set; }
    /// <summary>Gets or sets eight noise strengths in the interval zero through 1023/1024; null disables explicit noise.</summary>
    /// <remarks>Values are rounded to the nearest multiple of 1/1024. Explicit noise replaces the encoder's photon-noise setting for this frame.</remarks>
    public float[] NoiseLut { get; set; }
}

/// <summary>A spline with integral control points and quantized frequency coefficients.</summary>
/// <remarks>Each coefficient array contains 32 values. The channel weights are 0.0042, 0.075, 0.07 and 0.3333 for X, Y, B and width, respectively, before the shared quantization adjustment.</remarks>
public sealed class JxlSpline
{
    /// <summary>Gets or sets the spline control points in coded sample coordinates.</summary>
    public Point[] ControlPoints { get; set; } = [];
    /// <summary>Gets or sets 32 quantized X coefficients.</summary>
    public int[] X { get; set; } = new int[32];
    /// <summary>Gets or sets 32 quantized Y coefficients.</summary>
    public int[] Y { get; set; } = new int[32];
    /// <summary>Gets or sets 32 quantized B-minus-Y coefficients.</summary>
    public int[] B { get; set; } = new int[32];
    /// <summary>Gets or sets 32 quantized width coefficients.</summary>
    public int[] Width { get; set; } = new int[32];
}

/// <summary>A rectangle copied from a saved reference into one or more locations.</summary>
public sealed class JxlPatch
{
    /// <summary>Gets or sets the reference slot, from zero through three.</summary>
    public int Reference { get; set; }
    /// <summary>Gets or sets the rectangle in the reference frame.</summary>
    public Rectangle Source { get; set; }
    /// <summary>Gets or sets the destination placements.</summary>
    public JxlPatchTarget[] Targets { get; set; } = [];
}

/// <summary>Identifies a JPEG XL patch compositing operation.</summary>
public enum JxlPatchBlendMode
{
    /// <summary>Keep the current samples.</summary>
    None,
    /// <summary>Replace the current samples with the patch.</summary>
    Replace,
    /// <summary>Add the patch to the current samples.</summary>
    Add,
    /// <summary>Multiply the patch and current samples.</summary>
    Multiply,
    /// <summary>Blend the patch over the current samples using alpha.</summary>
    BlendAbove,
    /// <summary>Blend the current samples over the patch using alpha.</summary>
    BlendBelow,
    /// <summary>Add the patch multiplied by its alpha.</summary>
    MultiplyAddAbove,
    /// <summary>Add the current samples multiplied by their alpha to the patch.</summary>
    MultiplyAddBelow,
}

/// <summary>Describes patch blending for the color planes or one extra channel.</summary>
/// <param name="Mode">The compositing operation.</param>
/// <param name="AlphaChannel">The alpha extra-channel index.</param>
/// <param name="Clamp">Whether to clamp the blending multiplier.</param>
public readonly record struct JxlPatchBlendInfo(JxlPatchBlendMode Mode, int AlphaChannel = 0, bool Clamp = false);

/// <summary>A destination for a raw-frame patch.</summary>
public sealed class JxlPatchTarget
{
    /// <summary>Gets or sets the patch's upper-left destination in coded sample coordinates.</summary>
    public Point Position { get; set; }
    /// <summary>Gets or sets color blending.</summary>
    public JxlPatchBlendInfo Blending { get; set; } = new(JxlPatchBlendMode.Replace);
    /// <summary>Gets or sets blending for each extra channel; an empty array leaves extra channels untouched.</summary>
    public JxlPatchBlendInfo[] ExtraChannelBlending { get; set; } = [];
}
