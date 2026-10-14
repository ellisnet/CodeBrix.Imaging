// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-vardct/src/lf.rs and
// crates/jxl-frame/src/data/lf_global.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).
// Context limits checked against libjxl lib/jxl/entropy_coder.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).

using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Coding;
using CodeBrix.Imaging.Memory;
using static CodeBrix.Imaging.Formats.Jxl.Bitstream.JxlU32;

namespace CodeBrix.Imaging.Formats.Jxl.VarDct;

internal sealed class JxlLfGlobal
{
    internal uint GlobalScale, QuantLf;
    internal JxlHfBlockContext BlockContext;
    internal uint ColorFactor = 84;
    internal float BaseCorrelationX, BaseCorrelationB = 1;
    internal uint XFactorLf = 128, BFactorLf = 128;

    internal static JxlLfGlobal Read(JxlBitReader reader, MemoryAllocator allocator)
    {
        JxlLfGlobal result = new()
        {
            GlobalScale = reader.ReadU32(U(11, 1), U(11, 2049), U(12, 4097), U(16, 8193)),
            QuantLf = reader.ReadU32(16, U(5, 1), U(8, 1), U(16, 1)),
            BlockContext = JxlHfBlockContext.Read(reader, allocator),
        };
        if (!reader.ReadBool())
        {
            result.ColorFactor = reader.ReadU32(84, 256, U(8, 2), U(16, 258));
            result.BaseCorrelationX = reader.ReadF16(); result.BaseCorrelationB = reader.ReadF16();
            result.XFactorLf = reader.ReadBits(8); result.BFactorLf = reader.ReadBits(8);
        }
        return result;
    }
}

internal sealed class JxlHfBlockContext
{
    internal uint[] QuantThresholds = [];
    internal int[][] LfThresholds = [[], [], []];
    internal byte[] Map;
    internal int ClusterCount;

    internal static JxlHfBlockContext Read(JxlBitReader reader, MemoryAllocator allocator)
    {
        JxlHfBlockContext result = new();
        if (reader.ReadBool())
        {
            result.ClusterCount = 15;
            result.Map = [0, 1, 2, 2, 3, 3, 4, 5, 6, 6, 6, 6, 6, 7, 8, 9, 9, 10, 11, 12, 13, 14, 14, 14, 14, 14, 7, 8, 9, 9, 10, 11, 12, 13, 14, 14, 14, 14, 14];
            return result;
        }
        int size = 1;
        for (int c = 0; c < 3; c++)
        {
            int[] thresholds = new int[reader.ReadBits(4)];
            result.LfThresholds[c] = thresholds;
            size *= thresholds.Length + 1;
            for (int i = 0; i < thresholds.Length; i++)
            { thresholds[i] = JxlBitReader.UnpackSigned(reader.ReadU32(U(4), U(8, 16), U(16, 272), U(32, 65808))); }
        }
        result.QuantThresholds = new uint[reader.ReadBits(4)];
        size *= result.QuantThresholds.Length + 1;
        if (size > 64) { JxlThrowHelper.Invalid("VarDCT context thresholds exceed Level 10."); }
        for (int i = 0; i < result.QuantThresholds.Length; i++)
        { result.QuantThresholds[i] = 1 + reader.ReadU32(U(2), U(3, 4), U(5, 12), U(8, 44)); }
        result.Map = JxlEntropyDecoder.ReadClusters(reader, size * 39, allocator, out result.ClusterCount);
        if (result.ClusterCount > 16) { JxlThrowHelper.Invalid("too many VarDCT block clusters."); }
        return result;
    }
}
