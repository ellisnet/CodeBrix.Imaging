// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-image/src/color.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using static CodeBrix.Imaging.Formats.Jxl.Bitstream.JxlU32;

namespace CodeBrix.Imaging.Formats.Jxl.Headers;

internal sealed class JxlColorEncoding
{
    internal bool WantIcc;
    internal uint ColorSpace;
    internal uint WhitePoint = 1;
    internal int[] White = new int[2];
    internal uint Primaries = 1;
    internal int[] CustomPrimaries = new int[6];
    internal uint TransferFunction = 13;
    internal bool HasGamma;
    internal uint Gamma;
    internal uint RenderingIntent = 1;

    internal static JxlColorEncoding Read(JxlBitReader reader)
    {
        JxlColorEncoding color = new();
        if (reader.ReadBool()) { return color; }
        color.WantIcc = reader.ReadBool();
        color.ColorSpace = reader.ReadEnum();
        if (color.ColorSpace > 3) { JxlThrowHelper.Invalid("invalid color space."); }
        if (color.WantIcc) { return color; }
        if (color.ColorSpace != 2)
        {
            color.WhitePoint = reader.ReadEnum();
            if (color.WhitePoint is not (1 or 2 or 10 or 11)) { JxlThrowHelper.Invalid("invalid white point."); }
            if (color.WhitePoint == 2) { ReadCoordinates(reader, color.White); }
        }

        if (color.ColorSpace is not (1 or 2))
        {
            color.Primaries = reader.ReadEnum();
            if (color.Primaries is not (1 or 2 or 9 or 11)) { JxlThrowHelper.Invalid("invalid primaries."); }
            if (color.Primaries == 2) { ReadCoordinates(reader, color.CustomPrimaries); }
        }

        color.HasGamma = reader.ReadBool();
        if (color.HasGamma) { color.Gamma = reader.ReadBits(24); }
        else
        {
            color.TransferFunction = reader.ReadEnum();
            if (color.TransferFunction is not (1 or 2 or 8 or 13 or 16 or 17 or 18))
            {
                JxlThrowHelper.Invalid("invalid transfer function.");
            }
        }

        color.RenderingIntent = reader.ReadEnum();
        if (color.RenderingIntent > 3) { JxlThrowHelper.Invalid("invalid rendering intent."); }
        return color;
    }

    internal void Write(JxlBitWriter writer)
    {
        writer.WriteBool(false);
        writer.WriteBool(this.WantIcc);
        writer.WriteEnum(this.ColorSpace);
        if (this.WantIcc) { return; }
        if (this.ColorSpace != 2)
        {
            writer.WriteEnum(this.WhitePoint);
            if (this.WhitePoint == 2) { WriteCoordinates(writer, this.White); }
        }

        if (this.ColorSpace is not (1 or 2))
        {
            writer.WriteEnum(this.Primaries);
            if (this.Primaries == 2) { WriteCoordinates(writer, this.CustomPrimaries); }
        }

        writer.WriteBool(this.HasGamma);
        if (this.HasGamma) { writer.WriteBits(24, this.Gamma); }
        else { writer.WriteEnum(this.TransferFunction); }
        writer.WriteEnum(this.RenderingIntent);
    }

    private static void ReadCoordinates(JxlBitReader reader, int[] coordinates)
    {
        for (int i = 0; i < coordinates.Length; i++)
        {
            coordinates[i] = JxlBitReader.UnpackSigned(reader.ReadU32(U(19), U(19, 524288), U(20, 1048576), U(21, 2097152)));
        }
    }

    private static void WriteCoordinates(JxlBitWriter writer, int[] coordinates)
    {
        foreach (int coordinate in coordinates)
        {
            writer.WriteU32(JxlBitWriter.PackSigned(coordinate), U(19), U(19, 524288), U(20, 1048576), U(21, 2097152));
        }
    }
}
