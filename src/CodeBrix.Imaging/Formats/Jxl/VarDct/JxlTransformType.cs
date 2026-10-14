// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-vardct/src/dct_select.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

namespace CodeBrix.Imaging.Formats.Jxl.VarDct;

internal enum JxlTransformType
{
    Dct8, Hornuss, Dct2, Dct4, Dct16, Dct32, Dct16x8, Dct8x16, Dct32x8, Dct8x32,
    Dct32x16, Dct16x32, Dct4x8, Dct8x4, Afv0, Afv1, Afv2, Afv3, Dct64, Dct64x32,
    Dct32x64, Dct128, Dct128x64, Dct64x128, Dct256, Dct256x128, Dct128x256,
}

internal static class JxlTransformInfo
{
    internal static JxlTransformType Parse(int value)
    {
        if ((uint)value > 26) { JxlThrowHelper.Invalid("invalid VarDCT transform type."); }
        return (JxlTransformType)value;
    }

    internal static (int Width, int Height) Blocks(this JxlTransformType type) => type switch
    {
        JxlTransformType.Dct16 => (2, 2), JxlTransformType.Dct32 => (4, 4),
        JxlTransformType.Dct16x8 => (1, 2), JxlTransformType.Dct8x16 => (2, 1),
        JxlTransformType.Dct32x8 => (1, 4), JxlTransformType.Dct8x32 => (4, 1),
        JxlTransformType.Dct32x16 => (2, 4), JxlTransformType.Dct16x32 => (4, 2),
        JxlTransformType.Dct64 => (8, 8), JxlTransformType.Dct64x32 => (4, 8), JxlTransformType.Dct32x64 => (8, 4),
        JxlTransformType.Dct128 => (16, 16), JxlTransformType.Dct128x64 => (8, 16), JxlTransformType.Dct64x128 => (16, 8),
        JxlTransformType.Dct256 => (32, 32), JxlTransformType.Dct256x128 => (16, 32), JxlTransformType.Dct128x256 => (32, 16),
        _ => (1, 1),
    };

    internal static int MatrixIndex(this JxlTransformType type) => type switch
    {
        <= JxlTransformType.Dct32 => (int)type,
        JxlTransformType.Dct16x8 or JxlTransformType.Dct8x16 => 6,
        JxlTransformType.Dct32x8 or JxlTransformType.Dct8x32 => 7,
        JxlTransformType.Dct32x16 or JxlTransformType.Dct16x32 => 8,
        JxlTransformType.Dct4x8 or JxlTransformType.Dct8x4 => 9,
        <= JxlTransformType.Afv3 => 10,
        JxlTransformType.Dct64 => 11, JxlTransformType.Dct64x32 or JxlTransformType.Dct32x64 => 12,
        JxlTransformType.Dct128 => 13, JxlTransformType.Dct128x64 or JxlTransformType.Dct64x128 => 14,
        JxlTransformType.Dct256 => 15, _ => 16,
    };

    internal static int OrderIndex(this JxlTransformType type)
    {
        int matrix = type.MatrixIndex();
        return matrix switch { 0 => 0, 1 or 2 or 3 or 9 or 10 => 1, >= 11 => matrix - 4, _ => matrix - 2 };
    }

    internal static bool Transpose(this JxlTransformType type)
    {
        if (type is JxlTransformType.Hornuss or JxlTransformType.Dct2 or JxlTransformType.Dct4 or
            JxlTransformType.Dct4x8 or JxlTransformType.Dct8x4 or >= JxlTransformType.Afv0 and <= JxlTransformType.Afv3) { return false; }
        (int width, int height) = type.Blocks();
        return height >= width;
    }
}
