// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;

namespace CodeBrix.Imaging.Formats.Tiff.Compression; //Was previously: namespace SixLabors.ImageSharp.Formats.Tiff.Compression;

internal static class BitWriterUtils
{
    public static void WriteBits(Span<byte> buffer, int pos, uint count, byte value)
    {
        var bitPos = pos % 8;
        var bufferPos = pos / 8;
        var startIdx = bufferPos + bitPos;
        var endIdx = (int)(startIdx + count);

        if (value == 1)
        {
            for (var i = startIdx; i < endIdx; i++)
            {
                WriteBit(buffer, bufferPos, bitPos);

                bitPos++;
                if (bitPos >= 8)
                {
                    bitPos = 0;
                    bufferPos++;
                }
            }
        }
        else
        {
            for (var i = startIdx; i < endIdx; i++)
            {
                WriteZeroBit(buffer, bufferPos, bitPos);

                bitPos++;
                if (bitPos >= 8)
                {
                    bitPos = 0;
                    bufferPos++;
                }
            }
        }
    }

    public static void WriteBit(Span<byte> buffer, int bufferPos, int bitPos) => buffer[bufferPos] |= (byte)(1 << (7 - bitPos));

    public static void WriteZeroBit(Span<byte> buffer, int bufferPos, int bitPos) => buffer[bufferPos] = (byte)(buffer[bufferPos] & ~(1 << (7 - bitPos)));
}