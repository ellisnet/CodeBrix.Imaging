// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using CodeBrix.Imaging.ColorSpaces.Icc;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlIccLutTests
{
    private const float XyzScale = 65535f / 32768;

    public static IEnumerable<object[]> LutCases()
    {
        foreach (bool reverse in new[] { false, true })
        foreach (bool xyz in new[] { false, true })
        foreach (int precision in new[] { 1, 2 })
        for (int stages = 0; stages < 4; stages++)
        { yield return [reverse, xyz, precision, stages]; }
    }

    [Theory]
    [MemberData(nameof(LutCases))]
    public void pcs_scaling_surrounds_all_lut_stages(bool reverse, bool xyz, int precision, int stages)
    {
        // Independent numerical examples of ICC.1:2022 sections 6.3.4.2 and
        // 10.12-10.13. B squares each channel; CLUT permutes G,B,R; M and A
        // are identity; matrix halves each channel and adds 1/8, 1/16, 1/32.
        // Nonlinear B curves and nonzero offsets expose moving the PCS factor
        // into the matrix, including when the matrix is absent entirely.
        IccLut lut = IccLut.Read(MultiStageTag(reverse, precision, stages), reverse, xyz);
        float scale = xyz ? XyzScale : 1;
        float[] values = [.25f, .5f, .75f];
        if (reverse) { for (int c = 0; c < 3; c++) { values[c] *= scale; } }
        float[] expected = (reverse, stages) switch
        {
            (false, 0) => [.0625f, .25f, .5625f],
            (false, 1) => [.0625f, .09765625f, .1650390625f],
            (false, 2) => [.25f, .5625f, .0625f],
            (false, 3) => [.140625f, .19140625f, .0244140625f],
            (true, 0) => [.0625f, .25f, .5625f],
            (true, 1) => [.15625f, .1875f, .3125f],
            (true, 2) => [.25f, .5625f, .0625f],
            _ => [.1875f, .3125f, .15625f],
        };
        if (!reverse) { for (int c = 0; c < 3; c++) { expected[c] *= scale; } }
        lut.Apply(values);
        AssertChannels(expected, values);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void matrix_outputs_are_clipped_before_the_following_curve(bool reverse)
    {
        byte[] tag = MultiStageTag(reverse, 2, 1);
        int matrix = (int)BinaryPrimitives.ReadUInt32BigEndian(tag.AsSpan(16));
        for (int c = 0; c < 3; c++)
        { Fixed(tag, matrix + c * 16, 4); Fixed(tag, matrix + 36 + c * 4, -.5f); }
        float[] values = reverse ? [.25f, .75f, 1] : [.0625f, .5625f, 1];
        IccLut.Read(tag, reverse, false).Apply(values);
        AssertChannels([0, 1, 1], values);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void legacy_16_bit_xyz_uses_unsigned_1_15_pcs_coordinates(bool reverse)
    {
        float[] values = [.25f, .5f, .75f];
        if (reverse) { for (int c = 0; c < 3; c++) { values[c] *= XyzScale; } }
        IccLut.Read(LegacyTag(2), reverse, true).Apply(values);
        float scale = reverse ? 1 : XyzScale;
        AssertChannels([.25f * scale, .5f * scale, .75f * scale], values);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public void legacy_xyz_input_matrix_is_applied_before_input_tables(int precision)
    {
        byte[] tag = LegacyTag(precision);
        for (int c = 0; c < 3; c++) { Fixed(tag, 12 + c * 16, .5f); }
        // Constant input tables turn a misplaced matrix into .5 instead of 1.
        int tables = precision == 1 ? 48 : 52;
        tag.AsSpan(tables, 3 * (precision == 1 ? 256 : 2) * precision).Fill(255);
        float[] values = [.25f, .5f, .75f];
        IccLut.Read(tag, true, true).Apply(values);
        AssertChannels([1, 1, 1], values);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void unspecified_legacy_8_bit_xyz_retains_unit_scaling(bool reverse)
    {
        // ICC.1:2022 10.11 leaves lut8 XYZ interpretation implementation-specific.
        float[] values = [.25f, .5f, .75f];
        IccLut.Read(LegacyTag(1), reverse, true).Apply(values);
        AssertChannels([.25f, .5f, .75f], values);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void legacy_16_bit_lab_preserves_neutral_and_white_encoding(bool reverse)
    {
        const float legacyScale = 65280f / 65535;
        float[] expected = [.5f, 128f / 255, 1];
        float[] values = (float[])expected.Clone();
        if (!reverse) { for (int c = 0; c < 3; c++) { values[c] *= legacyScale; } }
        else { for (int c = 0; c < 3; c++) { expected[c] *= legacyScale; } }
        IccLut.Read(LegacyTag(2), reverse, false).Apply(values);
        AssertChannels(expected, values);
    }

    [Fact]
    public void legacy_lab_values_above_the_normalized_range_are_clipped()
    {
        float[] values = [1, 1, 1];
        IccLut.Read(LegacyTag(2), false, false).Apply(values);
        AssertChannels([1, 1, 1], values);
    }

    private static byte[] MultiStageTag(bool reverse, int precision, int stages)
    {
        const int b = 32, matrix = 80, m = 128, a = 176, grid = 224;
        byte[] tag = new byte[grid + 20 + 2 * 4 * 4 * 3 * precision];
        Put(tag, 0, reverse ? 0x6d424120U : 0x6d414220U); tag[8] = tag[9] = 3;
        Put(tag, 12, b);
        if ((stages & 1) != 0) { Put(tag, 16, matrix); Put(tag, 20, m); }
        if ((stages & 2) != 0) { Put(tag, 24, grid); Put(tag, 28, a); }
        for (int c = 0; c < 3; c++)
        {
            foreach (int offset in new[] { b, m, a })
            { Put(tag, offset + c * 16, 0x70617261); Fixed(tag, offset + c * 16 + 12, offset == b ? 2 : 1); }
            Fixed(tag, matrix + c * 16, .5f);
            Fixed(tag, matrix + 36 + c * 4, .125f / (1 << c));
        }
        tag[grid] = 2; tag[grid + 1] = 4; tag[grid + 2] = 4; tag[grid + 16] = (byte)precision;
        int at = grid + 20;
        for (int r = 0; r < 2; r++)
        for (int g = 0; g < 4; g++)
        for (int blue = 0; blue < 4; blue++)
        foreach (float value in new[] { g / 3f, blue / 3f, (float)r })
        { Sample(tag, ref at, value, precision); }
        return tag;
    }

    private static byte[] LegacyTag(int precision)
    {
        int entries = precision == 1 ? 256 : 2, tables = precision == 1 ? 48 : 52;
        byte[] tag = new byte[tables + precision * (6 * entries + 8 * 3)];
        Put(tag, 0, precision == 1 ? 0x6d667431U : 0x6d667432U); tag[8] = tag[9] = 3; tag[10] = 2;
        for (int c = 0; c < 3; c++) { Fixed(tag, 12 + c * 16, 1); }
        if (precision == 2) { BinaryPrimitives.WriteUInt16BigEndian(tag.AsSpan(48), 2); BinaryPrimitives.WriteUInt16BigEndian(tag.AsSpan(50), 2); }
        int at = tables;
        for (int c = 0; c < 3; c++)
        for (int i = 0; i < entries; i++) { Sample(tag, ref at, i / (float)(entries - 1), precision); }
        for (int r = 0; r < 2; r++)
        for (int g = 0; g < 2; g++)
        for (int b = 0; b < 2; b++)
        foreach (int value in new[] { r, g, b }) { Sample(tag, ref at, value, precision); }
        for (int c = 0; c < 3; c++)
        for (int i = 0; i < entries; i++) { Sample(tag, ref at, i / (float)(entries - 1), precision); }
        return tag;
    }

    private static void Sample(byte[] bytes, ref int offset, float value, int precision)
    {
        if (precision == 1) { bytes[offset++] = (byte)MathF.Round(value * 255); }
        else { BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset), (ushort)MathF.Round(value * 65535)); offset += 2; }
    }

    private static void AssertChannels(float[] expected, float[] actual)
    {
        for (int c = 0; c < 3; c++) { Assert.InRange(Math.Abs(expected[c] - actual[c]), 0, 2e-6f); }
    }

    private static void Put(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value);
    private static void Fixed(byte[] bytes, int offset, float value) => BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(offset), (int)MathF.Round(value * 65536));
}
