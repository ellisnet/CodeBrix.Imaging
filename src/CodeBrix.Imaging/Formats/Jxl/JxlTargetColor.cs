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
}
