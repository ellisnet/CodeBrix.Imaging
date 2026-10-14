// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

namespace CodeBrix.Imaging.Formats.Jxl;

/// <summary>A JPEG XL transform for explicit raw-frame VarDCT encoding.</summary>
/// <remarks>Rectangular names specify height followed by width, matching the JPEG XL transform identifiers.</remarks>
public enum JxlDctTransform
{
    /// <summary>An 8 by 8 DCT.</summary>
    Dct8,
    /// <summary>The Hornuss transform.</summary>
    Hornuss,
    /// <summary>Hierarchical 2 by 2 DCTs.</summary>
    Dct2,
    /// <summary>Four 4 by 4 DCTs.</summary>
    Dct4,
    /// <summary>A 16 by 16 DCT.</summary>
    Dct16,
    /// <summary>A 32 by 32 DCT.</summary>
    Dct32,
    /// <summary>A DCT with height 16 and width 8.</summary>
    Dct16x8,
    /// <summary>A DCT with height 8 and width 16.</summary>
    Dct8x16,
    /// <summary>A DCT with height 32 and width 8.</summary>
    Dct32x8,
    /// <summary>A DCT with height 8 and width 32.</summary>
    Dct8x32,
    /// <summary>A DCT with height 32 and width 16.</summary>
    Dct32x16,
    /// <summary>A DCT with height 16 and width 32.</summary>
    Dct16x32,
    /// <summary>Two DCTs with height 4 and width 8.</summary>
    Dct4x8,
    /// <summary>Two DCTs with height 8 and width 4.</summary>
    Dct8x4,
    /// <summary>The AFV transform in orientation zero.</summary>
    Afv0,
    /// <summary>The AFV transform in orientation one.</summary>
    Afv1,
    /// <summary>The AFV transform in orientation two.</summary>
    Afv2,
    /// <summary>The AFV transform in orientation three.</summary>
    Afv3,
    /// <summary>A 64 by 64 DCT.</summary>
    Dct64,
    /// <summary>A DCT with height 64 and width 32.</summary>
    Dct64x32,
    /// <summary>A DCT with height 32 and width 64.</summary>
    Dct32x64,
    /// <summary>A 128 by 128 DCT.</summary>
    Dct128,
    /// <summary>A DCT with height 128 and width 64.</summary>
    Dct128x64,
    /// <summary>A DCT with height 64 and width 128.</summary>
    Dct64x128,
    /// <summary>A 256 by 256 DCT.</summary>
    Dct256,
    /// <summary>A DCT with height 256 and width 128.</summary>
    Dct256x128,
    /// <summary>A DCT with height 128 and width 256.</summary>
    Dct128x256,
}
