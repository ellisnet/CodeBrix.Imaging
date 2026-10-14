// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-vardct/src/hf_pass.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

using System;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Coding;
using CodeBrix.Imaging.Memory;
using static CodeBrix.Imaging.Formats.Jxl.Bitstream.JxlU32;

namespace CodeBrix.Imaging.Formats.Jxl.VarDct;

internal sealed class JxlHfPass
{
    internal static readonly (int Width, int Height)[] OrderSizes =
        [(8, 8), (8, 8), (16, 16), (32, 32), (16, 8), (32, 8), (32, 16), (64, 64), (64, 32), (128, 128), (128, 64), (256, 256), (256, 128)];
    private static readonly Lazy<(ushort X, ushort Y)[]>[] NaturalOrders = CreateNaturalOrders();
    private readonly (ushort X, ushort Y)[][][] permutations = new (ushort, ushort)[13][][];
    internal JxlEntropyDecoder Distributions;

    internal static JxlHfPass Read(JxlBitReader reader, JxlHfBlockContext contexts, uint presets, MemoryAllocator allocator)
    {
        uint used = reader.ReadU32(0x5f, 0x13, 0, U(13));
        JxlHfPass pass = new();
        using JxlEntropyDecoder decoder = used == 0 ? null : JxlEntropyDecoder.Read(reader, 8, allocator);
        decoder?.Begin(reader);
        for (int i = 0; i < 13; i++)
        {
            if ((used & (1U << i)) == 0) { continue; }
            (int width, int height) = OrderSizes[i];
            (ushort X, ushort Y)[] natural = NaturalOrders[i].Value;
            pass.permutations[i] = new (ushort, ushort)[3][];
            for (int c = 0; c < 3; c++)
            {
                int[] permutation = decoder.ReadPermutation(reader, width * height, width * height / 64);
                (ushort X, ushort Y)[] order = new (ushort, ushort)[permutation.Length];
                for (int n = 0; n < order.Length; n++) { order[n] = natural[permutation[n]]; }
                pass.permutations[i][c] = order;
            }
        }
        decoder?.Finish();
        long count = 495L * presets * contexts.ClusterCount;
        if (count > 1 << 20) { JxlThrowHelper.Invalid("HF entropy context count exceeds the decoder allocation limit."); }
        pass.Distributions = JxlEntropyDecoder.Read(reader, (int)count, allocator);
        return pass;
    }

    internal (ushort X, ushort Y)[] Order(int index, int channel) => this.permutations[index]?[channel] ?? NaturalOrders[index].Value;

    private static Lazy<(ushort X, ushort Y)[]>[] CreateNaturalOrders()
    {
        Lazy<(ushort, ushort)[]>[] orders = new Lazy<(ushort, ushort)[]>[13];
        for (int i = 0; i < orders.Length; i++)
        {
            (int width, int height) = OrderSizes[i];
            orders[i] = new(() => NaturalOrder(width, height));
        }
        return orders;
    }

    private static (ushort X, ushort Y)[] NaturalOrder(int width, int height)
    {
        (ushort, ushort)[] output = new (ushort, ushort)[width * height];
        int index = 0, lfWidth = width / 8, lfHeight = height / 8, scaleY = width / height;
        for (; index < lfWidth * lfHeight; index++) { output[index] = ((ushort)(index % lfWidth), (ushort)(index / lfWidth)); }
        for (int distance = 1; distance < 2 * width; distance++)
        {
            int margin = Math.Max(0, distance - width);
            for (int step = margin; step < distance - margin; step++)
            {
                (int x, int y) = distance % 2 == 1 ? (step, distance - 1 - step) : (distance - 1 - step, step);
                if ((x < lfWidth && y < lfWidth) || y % scaleY != 0) { continue; }
                output[index++] = ((ushort)x, (ushort)(y / scaleY));
            }
        }
        return output;
    }
}
