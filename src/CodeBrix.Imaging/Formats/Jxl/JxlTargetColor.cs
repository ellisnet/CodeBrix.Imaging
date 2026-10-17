// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

namespace CodeBrix.Imaging.Formats.Jxl;

/// <summary>Specifies the color encoding of decoded JPEG XL pixels.</summary>
public enum JxlTargetColor
{
    /// <summary>Use sRGB for XYB images and the original encoding for other images. CMYK display pixels use sRGB.</summary>
    Auto,
    /// <summary>Use the original encoding. CMYK samples require <see cref="JxlDecoder.DecodeRaw"/>.</summary>
    Original,
    /// <summary>Convert color samples to sRGB. Floating-point output retains values outside the unit interval.</summary>
    Srgb,
    /// <summary>
    /// Convert color samples to linear sRGB (sRGB primaries and D65 white with a linear transfer function), libjxl's convention for
    /// floating-point output. Values outside the unit interval are retained, as for <see cref="Srgb"/>. Decoded images carry a
    /// linear-sRGB ICC profile unless metadata is ignored. Use a floating-point or 16-bit pixel type: 8-bit linear samples lose shadow detail.
    /// </summary>
    LinearSrgb,
}
