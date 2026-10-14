// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-color/src/icc/decode.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

using System;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Coding;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Color;

internal static partial class JxlIccCodec
{
    internal static byte[] ReadEncoded(JxlBitReader reader, long maxBytes, MemoryAllocator allocator)
    {
        ulong size = reader.ReadU64();
        if (size > (ulong)Math.Min(maxBytes, 1 << 28)) { JxlThrowHelper.Invalid("encoded ICC profile exceeds metadata limit."); }
        using JxlEntropyDecoder decoder = JxlEntropyDecoder.Read(reader, 41, allocator);
        decoder.Begin(reader);
        byte[] encoded = new byte[(int)size];
        byte previous = 0, second = 0;
        for (int i = 0; i < encoded.Length; i++)
        {
            uint value = decoder.Read(reader, Context(i, previous, second));
            if (value > 255) { JxlThrowHelper.Invalid("ICC decoded symbol exceeds byte range."); }
            second = previous;
            encoded[i] = previous = (byte)value;
            // Check the declared output size before processing a large entropy stream.
            if (i == Math.Min(17, encoded.Length - 1)) { ValidateSizes(encoded.AsSpan(0, i + 1), size, maxBytes); }
        }
        decoder.Finish();
        ValidateSizes(encoded, size, maxBytes);
        return encoded;
    }

    private static void ValidateSizes(ReadOnlySpan<byte> header, ulong encodedSize, long maxBytes)
    {
        int offset = 0;
        ulong outputSize = ReadVarint(header, ref offset);
        ulong commandsSize = ReadVarint(header, ref offset);
        if (commandsSize > encodedSize || (ulong)offset > encodedSize - commandsSize) { JxlThrowHelper.Invalid("invalid ICC command stream size."); }
        if (outputSize > (ulong)Math.Min(maxBytes, 1 << 28)) { JxlThrowHelper.Invalid("ICC profile exceeds metadata limit."); }
        if (outputSize + 65536 < encodedSize) { JxlThrowHelper.Invalid("encoded ICC profile is inconsistent with output size."); }
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> data, ref int offset)
    {
        ulong value = 0;
        for (int shift = 0; shift < 63; shift += 7)
        {
            if (offset >= data.Length) { JxlThrowHelper.Invalid("truncated ICC varint."); }
            byte part = data[offset++];
            value |= (ulong)(part & 127) << shift;
            if (part < 128) { return value; }
        }
        JxlThrowHelper.Invalid("oversized ICC varint.");
        return 0;
    }

    private static int Context(int index, byte previous, byte second)
    {
        if (index <= 128) { return 0; }
        int firstClass = previous switch
        {
            >= 65 and <= 90 or >= 97 and <= 122 => 0,
            >= 48 and <= 57 or 46 or 44 => 1,
            <= 1 => 2 + previous,
            <= 15 => 4,
            >= 241 and <= 254 => 5,
            255 => 6,
            _ => 7,
        };
        int secondClass = second switch
        {
            >= 65 and <= 90 or >= 97 and <= 122 => 0,
            >= 48 and <= 57 or 46 or 44 => 1,
            <= 15 => 2,
            >= 241 => 3,
            _ => 4,
        };
        return 1 + firstClass + (8 * secondClass);
    }
}
