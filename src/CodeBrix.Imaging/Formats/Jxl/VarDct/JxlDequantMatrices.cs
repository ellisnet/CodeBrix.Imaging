// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-vardct/src/dequant.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).
// Raw-table Modular dimensions corrected against libjxl quant_weights.h and
// dec_modular.cc, ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause
// plus PATENTS). Copyright (c) the JPEG XL Project Authors.

using System;
using System.Collections.Generic;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Modular;

namespace CodeBrix.Imaging.Formats.Jxl.VarDct;

internal sealed partial class JxlDequantMatrices
{
    private static readonly (int Width, int Height)[] Sizes =
        [(8, 8), (8, 8), (8, 8), (8, 8), (16, 16), (32, 32), (16, 8), (32, 8), (32, 16), (8, 8), (8, 8), (64, 64), (64, 32), (128, 128), (128, 64), (256, 256), (256, 128)];
    private static readonly Lazy<JxlDequantMatrices> Defaults = new(CreateDefault);
    private readonly float[][][] matrices = new float[17][][];
    internal int[][] JpegQuantization;

    internal static JxlDequantMatrices Default => Defaults.Value;

    internal float[] Get(JxlTransformType type, int channel) => this.matrices[type.MatrixIndex()][channel];

    internal static JxlDequantMatrices Read(JxlBitReader reader, int bitDepth, int lfGroups, JxlMaTree tree, JxlModularWorkspace workspace)
    {
        if (reader.ReadBool()) { return Defaults.Value; }
        JxlDequantMatrices result = new();
        for (int index = 0; index < 17; index++)
        {
            int mode = (int)reader.ReadBits(3);
            (int width, int height) = Sizes[index];
            if (mode >= 1 && mode <= 5 && index is not (0 or 1 or 2 or 3 or 9 or 10))
            { JxlThrowHelper.Invalid("invalid dequantization mode for transform size."); }
            if (mode == 0) { result.matrices[index] = Defaults.Value.matrices[index]; continue; }
            if (mode == 7)
            {
                // Raw tables serialize a narrow/tall Modular image, while
                // its flattened samples form the wide/short coefficient table.
                (width, height) = (height, width);
                float denominator = reader.ReadF16();
                using JxlModularWorkspace temporary = workspace.CreateTemporary();
                List<JxlModularPlane> planes = [temporary.Allocate(width, height), temporary.Allocate(width, height), temporary.Allocate(width, height)];
                JxlModularImage modular = JxlModularImage.Read(reader, planes, tree, temporary);
                modular.DecodeAll(reader, 1 + (lfGroups * 3) + index, temporary);
                modular.Finish(bitDepth, temporary);
                float[][] matrix = new float[3][];
                bool jpeg = index == 0 && MathF.Round(1 / denominator) == 2040;
                if (jpeg) { result.JpegQuantization = new int[3][]; }
                for (int c = 0; c < 3; c++)
                {
                    matrix[c] = new float[width * height];
                    if (jpeg) { result.JpegQuantization[c] = new int[width * height]; }
                    for (int y = 0; y < height; y++)
                    {
                        Span<int> row = planes[c].Row(y);
                        for (int x = 0; x < width; x++)
                        {
                            matrix[c][(y * width) + x] = row[x] * denominator;
                            if (jpeg) { result.JpegQuantization[c][(y * width) + x] = row[x]; }
                        }
                    }
                }
                Validate(matrix);
                result.matrices[index] = matrix;
                continue;
            }
            float[][] parameters = mode switch
            {
                1 => ReadFixed(reader, 3), 2 => ReadFixed(reader, 6), 3 => ReadFixed(reader, 2),
                4 => ReadFixed(reader, 1), 5 => ReadFixed(reader, 9), _ => null,
            };
            if (mode == 5)
            {
                for (int c = 0; c < 3; c++) { for (int p = 0; p < 6; p++) { parameters[c][p] *= 64; } }
            }
            float[][] dct = mode >= 3 ? ReadDct(reader) : null;
            float[][] dct4 = mode == 5 ? ReadDct(reader) : null;
            result.matrices[index] = Make(index, mode, parameters, dct, dct4);
        }
        return result;
    }

    private static JxlDequantMatrices CreateDefault()
    {
        JxlDequantMatrices result = new();
        for (int i = 0; i < 17; i++)
        {
            int mode = i switch { 1 => 1, 2 => 2, 3 => 3, 9 => 4, 10 => 5, _ => 6 };
            float[][] parameters = i switch
            {
                1 => DefaultHornuss, 2 => DefaultDct2, 3 => [[1, 1], [1, 1], [1, 1]], 9 => [[1], [1], [1]], 10 => DefaultAfv, _ => null,
            };
            float[][] dct = mode <= 2 ? null : i == 3 ? DefaultDct4 : i is 9 or 10 ? DefaultDct4x8 : DefaultDct(i);
            result.matrices[i] = Make(i, mode, parameters, dct, i == 10 ? DefaultDct4 : null);
        }
        return result;
    }

    private static float[][] ReadFixed(JxlBitReader reader, int count)
    {
        float[][] result = new float[3][];
        for (int c = 0; c < 3; c++)
        {
            result[c] = new float[count];
            for (int i = 0; i < count; i++) { result[c][i] = reader.ReadF16(); }
        }
        return result;
    }

    private static float[][] ReadDct(JxlBitReader reader)
    {
        float[][] result = ReadFixed(reader, 1 + (int)reader.ReadBits(4));
        for (int c = 0; c < 3; c++) { result[c][0] *= 64; }
        return result;
    }

    private static float[][] Make(int index, int mode, float[][] parameters, float[][] dct, float[][] dct4)
    {
        float[][] result = new float[3][];
        for (int c = 0; c < 3; c++)
        {
            float[] p = parameters?[c];
            float[] matrix = new float[64];
            switch (mode)
            {
                case 1:
                    Array.Fill(matrix, p[0]); matrix[0] = 1; matrix[1] = matrix[8] = p[1]; matrix[9] = p[2];
                    break;
                case 2:
                    matrix[0] = 1;
                    for (int k = 0; k < 6; k++)
                    {
                        int size = 1 << (k / 2);
                        for (int y = k % 2 == 0 ? 0 : size; y < (k % 2 == 0 ? size : size * 2); y++)
                        {
                            for (int x = size; x < 2 * size; x++)
                            { matrix[(y * 8) + x] = p[k]; if (k % 2 == 0) { matrix[(x * 8) + y] = p[k]; } }
                        }
                    }
                    break;
                case 3:
                    float[] small = DctWeights(dct[c], 4, 4);
                    for (int y = 0; y < 8; y++)
                    { for (int x = 0; x < 8; x++) { matrix[(y * 8) + x] = small[((y / 2) * 4) + (x / 2)]; } }
                    matrix[1] /= p[0]; matrix[8] /= p[0]; matrix[9] /= p[1];
                    break;
                case 4:
                    float[] rectangular = DctWeights(dct[c], 8, 4);
                    for (int y = 0; y < 8; y++) { rectangular.AsSpan(y / 2 * 8, 8).CopyTo(matrix.AsSpan(y * 8)); }
                    matrix[8] /= p[0];
                    break;
                case 5:
                    float[] weights8 = DctWeights(dct[c], 8, 4), weights4 = DctWeights(dct4[c], 4, 4);
                    float[] bands = [p[5], 0, 0, 0];
                    for (int i = 1; i < 4; i++) { bands[i] = bands[i - 1] * Multiplier(p[i + 5]); }
                    ReadOnlySpan<float> frequencies = [0, 0, 0.8517779f, 5.3777843f, 0, 0, 4.734748f, 5.4492455f, 1.659827f, 4, 7.275749f, 10.423227f, 2.6629324f, 7.6306577f, 8.962389f, 12.971662f];
                    for (int y = 0; y < 4; y++)
                    {
                        for (int x = 0; x < 4; x++)
                        {
                            matrix[(16 * y) + (2 * x)] = (x, y) switch
                            {
                                (0, 0) => 1, (0, 1) => p[2], (1, 0) => p[3], (1, 1) => p[4],
                                _ => Interpolate(frequencies[(y * 4) + x] - frequencies[2], frequencies[15] - frequencies[2] + 1E-6f, bands),
                            };
                            matrix[(16 * y) + (2 * x) + 1] = x == 0 && y == 0 ? p[1] : weights4[(y * 4) + x];
                        }
                        for (int x = 0; x < 8; x++) { matrix[(16 * y) + 8 + x] = x == 0 && y == 0 ? p[0] : weights8[(y * 8) + x]; }
                    }
                    break;
                case 6:
                    (int width, int height) = Sizes[index];
                    matrix = DctWeights(dct[c], width, height);
                    break;
            }
            for (int i = 0; i < matrix.Length; i++) { matrix[i] = 1 / matrix[i]; }
            result[c] = matrix;
        }
        Validate(result);
        return result;
    }

    private static float[] DctWeights(float[] parameters, int width, int height)
    {
        float[] bands = new float[parameters.Length];
        bands[0] = parameters[0];
        for (int i = 1; i < bands.Length; i++)
        {
            bands[i] = bands[i - 1] * Multiplier(parameters[i]);
            if (!(bands[i] > 0)) { JxlThrowHelper.Invalid("nonpositive DCT dequantization band."); }
        }
        float[] result = new float[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float dx = (float)x / (width - 1), dy = (float)y / (height - 1);
                result[(y * width) + x] = Interpolate(MathF.Sqrt((dx * dx) + (dy * dy)), MathF.Sqrt(2) + 1E-6f, bands);
            }
        }
        return result;
    }

    private static float Multiplier(float value) => value > 0 ? 1 + value : 1 / (1 - value);
    private static float Interpolate(float position, float maximum, float[] bands)
    {
        if (bands.Length == 1) { return bands[0]; }
        float scaled = position * (bands.Length - 1) / maximum;
        int index = (int)scaled;
        if ((uint)index >= bands.Length - 1) { JxlThrowHelper.Invalid("invalid dequantization band position."); }
        return bands[index] * MathF.Pow(bands[index + 1] / bands[index], scaled - index);
    }
    private static void Validate(float[][] matrix)
    {
        foreach (float[] channel in matrix)
        {
            foreach (float value in channel)
            { if (!float.IsFinite(value) || value >= 1E8f || value <= 0) { JxlThrowHelper.Invalid("invalid dequantization matrix element."); } }
        }
    }
}
