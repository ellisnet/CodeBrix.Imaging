// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;

namespace CodeBrix.Imaging.Formats.Tiff.Compression; //Was previously: namespace SixLabors.ImageSharp.Formats.Tiff.Compression;

internal static class BitWriterUtils
{
    public static void WriteBits(Span<byte> buffer, int pos, uint count, byte value)
    {
        if (count == 0)
        {
            return;
        }

        // Guard against writing past the destination buffer. A malformed / hostile CCITT fax
        // (T4 / Modified Huffman) TIFF can declare pixel runs whose accumulated length exceeds the
        // decoded strip buffer; without this check the loop below indexes out of range. Surface it
        // as a catchable ImageFormatException instead of leaking an IndexOutOfRangeException.
        // See GHSA-jj3q-cwqj-842r.
        if (pos < 0 || (long)pos + count > (long)buffer.Length * 8)
        {
            TiffThrowHelper.ThrowImageFormatException("ccitt fax compression parsing error, decoded run exceeds the strip buffer size");
        }

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