// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Runtime.InteropServices;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Coding;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Entropy;
using Xunit;

namespace CodeBrix.Imaging.Tests.Formats.Jxl;

public class JxlEntropyMemoryTests
{
    [Fact]
    public void token_storage_preserves_full_values_in_eight_bytes()
    {
        Assert.Equal(8, Marshal.SizeOf<JxlToken>());
        foreach (int context in new[] { 0, 1, 1 << 20, int.MaxValue })
        foreach (uint value in new[] { 0U, uint.MaxValue, 0x80000000U })
        foreach (bool length in new[] { false, true })
        {
            JxlToken token = new(context, value, length);
            Assert.Equal(context, token.Context); Assert.Equal(value, token.Value); Assert.Equal(length, token.IsLzLength);
            Assert.NotEqual(token, new(context, value, !length));
        }
    }

    [Theory]
    [InlineData(false, false, 2)] [InlineData(false, false, 7)] [InlineData(false, false, 10)]
    [InlineData(false, true, 2)] [InlineData(false, true, 7)] [InlineData(false, true, 10)]
    [InlineData(true, false, 2)] [InlineData(true, false, 7)] [InlineData(true, false, 10)]
    [InlineData(true, true, 2)] [InlineData(true, true, 7)] [InlineData(true, true, 10)]
    public void grouped_histograms_and_reverse_ans_cost_are_bit_identical(bool prefix, bool lz77, int effort)
    {
        const int contexts = 17;
        JxlToken[] flat = Tokens(12000, contexts, lz77);
        JxlToken[][] groups = [[], flat[..13], [], flat[13..1111], flat[1111..], []];
        JxlTokenSequence sequence = new(groups);
        Assert.Equal(flat.Length, sequence.Length);
        int index = 0; foreach (JxlToken token in sequence) { Assert.Equal(flat[index++], token); } Assert.Equal(flat.Length, index);
        index = flat.Length; foreach (JxlToken token in sequence.Reversed()) { Assert.Equal(flat[--index], token); } Assert.Equal(0, index);
        JxlEntropyEncoder oldModel = JxlEntropyEncoder.CreateOptimized(flat, contexts, prefix, effort, lz77, TestContext.Current.CancellationToken);
        JxlEntropyEncoder grouped = JxlEntropyEncoder.CreateOptimized(sequence, contexts, prefix, effort, lz77, TestContext.Current.CancellationToken);
        Assert.Equal(oldModel.GetBitCount(flat, TestContext.Current.CancellationToken),
            grouped.GetBitCount(sequence, TestContext.Current.CancellationToken));
        Assert.Equal(Write(oldModel, groups), Write(grouped, groups));
    }

    [Theory]
    [InlineData(false, false, 2)] [InlineData(false, true, 7)] [InlineData(false, true, 10)]
    [InlineData(true, false, 2)] [InlineData(true, false, 7)] [InlineData(true, false, 10)]
    [InlineData(true, true, 7)] [InlineData(true, true, 10)]
    public void bounded_config_histograms_preserve_exhaustive_configuration_choices(bool prefix, bool lz77, int effort)
    {
        JxlToken[] tokens = Tokens(12000, 17, lz77);
        byte[] map = new byte[lz77 ? 18 : 17]; for (int i = 0; i < map.Length; i++) { map[i] = (byte)(i % 3); }
        JxlIntegerConfig[] expected = JxlHistogramOptimizer.ChooseConfigs(new JxlTokenSequence(tokens), map, prefix, lz77, effort,
            TestContext.Current.CancellationToken, int.MaxValue);
        foreach (int threshold in new[] { 0, 1, 97, 7000 })
        {
            JxlIntegerConfig[] actual = JxlHistogramOptimizer.ChooseConfigs(new JxlTokenSequence(tokens), map, prefix, lz77, effort,
                TestContext.Current.CancellationToken, threshold);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void million_distinct_values_do_not_create_a_dictionary_entry_per_sample()
    {
        JxlToken[][] groups = new JxlToken[4][];
        for (int g = 0; g < groups.Length; g++)
        {
            groups[g] = new JxlToken[1 << 18];
            for (int i = 0; i < groups[g].Length; i++) { groups[g][i] = new(0, unchecked((uint)(g * groups[g].Length + i) * 2654435761U)); }
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        JxlIntegerConfig[] configs = JxlHistogramOptimizer.ChooseConfigs(new JxlTokenSequence(groups), [0], true, false, 10,
            TestContext.Current.CancellationToken);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 16 << 20, $"Configuration selection allocated {allocated} bytes for 1,048,576 distinct samples.");
        Assert.Single(configs); configs[0].Validate(15);
    }

    [Fact]
    public void grouped_model_does_not_allocate_a_flattened_frame_copy()
    {
        JxlToken[] group = new JxlToken[1 << 18];
        for (int i = 0; i < group.Length; i++) { group[i] = new(i % 3, (uint)(i % 51)); }
        JxlToken[][] groups = [[], group, group, [], group, group, []];
        long before = GC.GetAllocatedBytesForCurrentThread();
        JxlEntropyEncoder model = JxlEntropyEncoder.CreateOptimized(new JxlTokenSequence(groups), 3, true, 7, false, TestContext.Current.CancellationToken);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 4 << 20, $"Grouped analysis allocated {allocated} bytes; a flattened frame alone needs 8 MiB.");
        Assert.True(model.GetBitCount(new JxlTokenSequence(groups), TestContext.Current.CancellationToken) > 0);
    }

    private static JxlToken[] Tokens(int count, int contexts, bool lz77)
    {
        JxlToken[] tokens = new JxlToken[count]; Random random = new(38291);
        for (int i = 0; i < count; i++)
        {
            int context = i % contexts; uint value = (uint)random.NextInt64(0, 1L << (5 + context));
            tokens[i] = lz77 && i % 29 == 27 ? new(contexts, (uint)(i % 83))
                : lz77 && i % 29 == 26 ? new(context, (uint)(i % 101), true) : new(context, value);
        }
        return tokens;
    }

    private static byte[] Write(JxlEntropyEncoder encoder, JxlToken[][] groups)
    {
        using MemoryStream stream = new(); JxlBitWriter writer = new(stream); encoder.WriteHeader(writer);
        foreach (JxlToken[] group in groups) { encoder.WriteTokens(writer, group, TestContext.Current.CancellationToken); }
        writer.ZeroPadToByte(); return stream.ToArray();
    }
}
