// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using CodeBrix.Imaging.Formats.Jxl.VarDct;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlTransformTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    public void inverse_dct_matches_cosine_definition(int size)
    {
        float[] coefficients = new float[size];
        for (int i = 0; i < size; i++) { coefficients[i] = (float)Math.Sin(i * 1.73 + .27) / size; }
        float[] actual = (float[])coefficients.Clone();
        JxlDct.Transform2D(actual, size, 1, true);
        for (int x = 0; x < size; x++)
        {
            double expected = coefficients[0];
            for (int k = 1; k < size; k++) { expected += coefficients[k] * Math.Sqrt(2) * Math.Cos(Math.PI * k * ((2 * x) + 1) / (2 * size)); }
            Assert.InRange(Math.Abs(actual[x] - expected), 0, 0.000003);
        }
        JxlDct.Transform2D(actual, size, 1, false);
        for (int i = 0; i < size; i++) { Assert.InRange(Math.Abs(actual[i] - coefficients[i]), 0, 0.000003); }
    }

    public static IEnumerable<object[]> TransformTypes()
    {
        foreach (JxlTransformType type in Enum.GetValues<JxlTransformType>()) { yield return [(int)type]; }
    }

    [Theory]
    [InlineData(4, 4)]
    [InlineData(8, 8)]
    [InlineData(8, 16)]
    [InlineData(16, 32)]
    [InlineData(64, 16)]
    [InlineData(128, 256)]
    public void vector_path_is_bit_exact_with_separable_scalar_path(int width, int height)
    {
        Random random = new(903214);
        float[] column = new float[height];
        for (int sample = 0; sample < 8; sample++)
        {
            float[] expected = new float[width * height];
            for (int i = 0; i < expected.Length; i++) { expected[i] = (random.NextSingle() - .5f) * 16; }
            float[] actual = (float[])expected.Clone();
            for (int y = 0; y < height; y++) { JxlDct.Transform2D(expected.AsSpan(y * width, width), width, 1, true); }
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++) { column[y] = expected[(y * width) + x]; }
                JxlDct.Transform2D(column, height, 1, true);
                for (int y = 0; y < height; y++) { expected[(y * width) + x] = column[y]; }
            }
            JxlDct.Transform2D(actual, width, height, true);
            for (int i = 0; i < actual.Length; i++) { Assert.Equal(BitConverter.SingleToInt32Bits(expected[i]), BitConverter.SingleToInt32Bits(actual[i])); }
        }
    }

    [Theory]
    [MemberData(nameof(TransformTypes))]
    public void every_transform_preserves_constant_dc(int transform)
    {
        JxlTransformType type = (JxlTransformType)transform;
        (int width, int height) = type.Blocks();
        float[] coefficients = new float[width * height * 64];
        coefficients[0] = .375f;
        JxlInverseTransform.Apply(coefficients, type);
        foreach (float value in coefficients) { Assert.InRange(Math.Abs(value - .375f), 0, 0.000001); }
    }

    [Theory]
    [InlineData(8, 16)]
    [InlineData(32, 8)]
    [InlineData(128, 256)]
    public void rectangular_dct_matches_separable_basis(int width, int height)
    {
        float[] values = new float[width * height];
        values[(3 * width) + 5] = .75f;
        JxlDct.Transform2D(values, width, height, true);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double expected = 1.5 * Math.Cos(Math.PI * 5 * ((2 * x) + 1) / (2 * width)) * Math.Cos(Math.PI * 3 * ((2 * y) + 1) / (2 * height));
                Assert.InRange(Math.Abs(values[(y * width) + x] - expected), 0, .00002);
            }
        }
    }
}
