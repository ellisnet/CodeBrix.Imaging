// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-frame/src/data/toc.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

using System;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Coding;
using CodeBrix.Imaging.Formats.Jxl.Encoder.Entropy;
using CodeBrix.Imaging.Memory;
using static CodeBrix.Imaging.Formats.Jxl.Bitstream.JxlU32;

namespace CodeBrix.Imaging.Formats.Jxl.Headers;

internal sealed class JxlFrameToc
{
    internal uint[] Sizes;
    internal ulong[] Offsets;
    internal int[] LogicalToPhysical;
    internal ulong TotalBytes;
    internal long DataPosition;

    internal static JxlFrameToc Read(JxlBitReader reader, JxlFrameHeader frame, MemoryAllocator allocator)
    {
        ulong count = frame.GroupCount == 1 && frame.PassCount == 1 ? 1 : 2 + frame.LfGroupCount + (frame.GroupCount * frame.PassCount);
        if (count > 65536) { JxlThrowHelper.Invalid("too many frame TOC entries."); }
        JxlFrameToc toc = new() { Sizes = new uint[(int)count], Offsets = new ulong[(int)count] };
        if (reader.ReadBool())
        {
            using JxlEntropyDecoder decoder = JxlEntropyDecoder.Read(reader, 8, allocator);
            decoder.Begin(reader);
            toc.LogicalToPhysical = decoder.ReadPermutation(reader, (int)count);
            decoder.Finish();
        }
        reader.ZeroPadToByte();
        for (int i = 0; i < (int)count; i++)
        {
            toc.Offsets[i] = toc.TotalBytes;
            toc.Sizes[i] = reader.ReadU32(U(10), U(14, 1024), U(22, 17408), U(30, 4211712));
            toc.TotalBytes += toc.Sizes[i];
        }
        reader.ZeroPadToByte();
        toc.DataPosition = reader.Position / 8;
        return toc;
    }

    internal static void WriteOrdered(JxlBitWriter writer, JxlFrameHeader frame, ReadOnlySpan<uint> sizes)
        => Write(writer, frame, sizes, default);

    // Sizes and following payloads are in physical order; the permutation maps
    // each logical section to its physical position, as in the reader.
    internal static void Write(JxlBitWriter writer, JxlFrameHeader frame, ReadOnlySpan<uint> sizes,
        ReadOnlySpan<int> logicalToPhysical, int effort = 7, CancellationToken cancellationToken = default)
    {
        ulong count = frame.GroupCount == 1 && frame.PassCount == 1 ? 1 : 2 + frame.LfGroupCount + (frame.GroupCount * frame.PassCount);
        if (count > 65536 || (ulong)sizes.Length != count) { throw new ArgumentException("JPEG XL frame TOC count mismatch.", nameof(sizes)); }
        if (!logicalToPhysical.IsEmpty && logicalToPhysical.Length != sizes.Length)
        { throw new ArgumentException("JPEG XL TOC permutation count mismatch.", nameof(logicalToPhysical)); }
        // Validate all inputs before emitting the TOC flag.
        foreach (uint size in sizes)
        { if (size >= 4211712UL + (1UL << 30)) { throw new ArgumentOutOfRangeException(nameof(sizes)); } }
        JxlToken[] tokens = logicalToPhysical.IsEmpty ? null : JxlPermutationEncoder.Tokenize(logicalToPhysical);
        cancellationToken.ThrowIfCancellationRequested(); writer.WriteBool(tokens != null);
        if (tokens != null)
        {
            JxlEntropyEncoder model = JxlEntropyEncoder.CreateOptimized(tokens, 8, false, effort, false, cancellationToken);
            model.WriteHeader(writer); model.WriteTokens(writer, tokens, cancellationToken);
        }
        writer.ZeroPadToByte();
        foreach (uint size in sizes) { writer.WriteU32(size, U(10), U(14, 1024), U(22, 17408), U(30, 4211712)); }
        writer.ZeroPadToByte();
    }
}
