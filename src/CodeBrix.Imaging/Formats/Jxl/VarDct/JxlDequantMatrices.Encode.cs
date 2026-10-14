// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Inverts the jxl-oxide-derived dequant.rs mode-7 grammar in the paired reader,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).
// Power-of-two weight quantization and raw API integration are original work.
// Wire dimensions follow libjxl quant_weights.h and enc_modular.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Copyright (c) the JPEG XL Project Authors.

using System;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Modular;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.VarDct;

internal sealed partial class JxlDequantMatrices
{
    private int[][][] encodedWeights;
    private float[] encodedScales;

    internal static JxlDequantMatrices Create(JxlQuantizationMatrix[] custom)
    {
        ArgumentNullException.ThrowIfNull(custom);
        if (custom.Length == 0) { return Default; }
        if (custom.Length > Sizes.Length) { throw new ArgumentException("There are only seventeen quantization matrix families.", nameof(custom)); }
        JxlDequantMatrices result = new() { encodedWeights = new int[17][][], encodedScales = new float[17] };
        Array.Copy(Default.matrices, result.matrices, Sizes.Length);
        foreach (JxlQuantizationMatrix matrix in custom)
        {
            if (matrix == null || !Enum.IsDefined(matrix.Transform)) { throw new ArgumentException("Invalid quantization matrix transform.", nameof(custom)); }
            int index = ((JxlTransformType)matrix.Transform).MatrixIndex();
            if (result.encodedWeights[index] != null) { throw new ArgumentException("A transform family has more than one quantization matrix.", nameof(custom)); }
            (int width, int height) = Sizes[index]; int count = width * height;
            float[][] input = [matrix.X, matrix.Y, matrix.B]; float maximum = 0;
            foreach (float[] channel in input)
            {
                if (channel == null || channel.Length != count) { throw new ArgumentException("Quantization matrix dimensions do not match the transform.", nameof(custom)); }
                foreach (float value in channel)
                {
                    if (!float.IsFinite(value) || value <= 0 || value >= 1E8f) { throw new ArgumentException("Invalid quantization matrix weight.", nameof(custom)); }
                    maximum = Math.Max(maximum, value);
                }
            }
            float scale = MathF.ScaleB(1, -24);
            while (maximum / scale > 1 << 28) { scale *= 2; }
            result.encodedScales[index] = scale;
            int[][] encoded = result.encodedWeights[index] = new int[3][];
            float[][] decoded = result.matrices[index] = new float[3][];
            for (int c = 0; c < 3; c++)
            {
                encoded[c] = new int[count]; decoded[c] = new float[count];
                for (int i = 0; i < count; i++)
                {
                    encoded[c][i] = (int)MathF.Round(input[c][i] / scale);
                    decoded[c][i] = encoded[c][i] * scale;
                    if (decoded[c][i] <= 0 || decoded[c][i] >= 1E8f)
                    { throw new ArgumentException("Quantization weights exceed the precision supported by their shared scale.", nameof(custom)); }
                }
            }
        }
        return result;
    }

    internal void Write(JxlBitWriter writer, MemoryAllocator allocator, Action<JxlBitWriter, JxlModularPlane[]> writeModular)
    {
        writer.WriteBool(this.encodedWeights == null);
        if (this.encodedWeights == null) { return; }
        for (int index = 0; index < Sizes.Length; index++)
        {
            int[][] weights = this.encodedWeights[index];
            writer.WriteBits(3, weights == null ? 0U : 7U);
            if (weights == null) { continue; }
            writer.WriteF16(this.encodedScales[index]);
            (int height, int width) = Sizes[index];
            using JxlModularWorkspace workspace = JxlModularWorkspace.ForEncoding(allocator, 262144);
            JxlModularPlane[] planes = new JxlModularPlane[3];
            for (int c = 0; c < 3; c++)
            {
                planes[c] = workspace.Allocate(width, height);
                for (int y = 0; y < height; y++) { weights[c].AsSpan(y * width, width).CopyTo(planes[c].Row(y)); }
            }
            writeModular(writer, planes);
        }
    }
}
