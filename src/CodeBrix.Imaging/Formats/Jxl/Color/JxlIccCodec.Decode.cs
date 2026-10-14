// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-color/src/icc/decode.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

using System;
using System.Buffers.Binary;

namespace CodeBrix.Imaging.Formats.Jxl.Color;

internal static partial class JxlIccCodec
{
    internal static byte[] Decode(ReadOnlySpan<byte> encoded, long maxBytes)
    {
        ValidateSizes(encoded, (ulong)encoded.Length, maxBytes);
        int position = 0;
        int outputSize = (int)ReadVarint(encoded, ref position);
        int commandSize = (int)ReadVarint(encoded, ref position);
        ReadOnlySpan<byte> commands = encoded.Slice(position, commandSize);
        ReadOnlySpan<byte> data = encoded[(position + commandSize)..];
        int headerSize = Math.Min(outputSize, 128);
        IccOutput output = new(outputSize);
        ReadOnlySpan<byte> header = Take(ref data, headerSize);
        for (int i = 0; i < header.Length; i++) { output.WriteByte(unchecked((byte)(header[i] + PredictHeader(i, (uint)outputSize, header)))); }
        if (outputSize <= 128) { return output.Finish(); }

        position = 0;
        ulong tagMarker = ReadVarint(commands, ref position);
        if (tagMarker != 0)
        {
            ulong tagCount = tagMarker - 1;
            if (tagCount > (ulong)(outputSize - 128) / 12) { JxlThrowHelper.Invalid("ICC tag count exceeds profile size."); }
            output.WriteU32((uint)tagCount);
            uint previousStart = ((uint)tagCount * 12) + 128;
            uint previousSize = 0;
            while (position < commands.Length)
            {
                byte command = commands[position++];
                int code = command & 63;
                if (code == 0) { break; }
                ReadOnlySpan<byte> tag = code == 1 ? Take(ref data, 4) : CommonTag(code);
                ulong start = (command & 64) == 0 ? (ulong)previousStart + previousSize : ReadVarint(commands, ref position);
                ulong size = (command & 128) != 0 ? ReadVarint(commands, ref position) :
                    tag.SequenceEqual("rXYZ"u8) || tag.SequenceEqual("gXYZ"u8) || tag.SequenceEqual("bXYZ"u8) || tag.SequenceEqual("kXYZ"u8) ||
                    tag.SequenceEqual("wtpt"u8) || tag.SequenceEqual("bkpt"u8) || tag.SequenceEqual("lumi"u8) ? 20UL : previousSize;
                if (start > (ulong)outputSize || size > (ulong)outputSize - start) { JxlThrowHelper.Invalid("ICC tag exceeds profile size."); }
                previousStart = (uint)start;
                previousSize = (uint)size;
                output.WriteTag(tag, previousStart, previousSize);
                if (code == 2)
                {
                    output.WriteTag("gTRC"u8, previousStart, previousSize);
                    output.WriteTag("bTRC"u8, previousStart, previousSize);
                }
                else if (code == 3)
                {
                    if (size * 3 > (ulong)outputSize - start) { JxlThrowHelper.Invalid("ICC XYZ tags exceed profile size."); }
                    output.WriteTag("gXYZ"u8, previousStart + previousSize, previousSize);
                    output.WriteTag("bXYZ"u8, previousStart + (previousSize * 2), previousSize);
                }
            }
        }

        while (position < commands.Length)
        {
            int command = commands[position++];
            if (command is >= 1 and <= 3)
            {
                int count = ReadLength(commands, ref position, data.Length);
                ReadOnlySpan<byte> bytes = Take(ref data, count);
                if (command == 1) { output.Write(bytes); }
                else { WriteShuffled(ref output, bytes, command == 2 ? 2 : 4); }
            }
            else if (command == 4)
            {
                if (position >= commands.Length) { JxlThrowHelper.Invalid("missing ICC prediction flags."); }
                int flags = commands[position++];
                int width = (flags & 3) + 1, order = (flags >> 2) & 3;
                if (width == 3 || order == 3) { JxlThrowHelper.Invalid("invalid ICC predictor width or order."); }
                int stride = (flags & 16) == 0 ? width : ReadLength(commands, ref position, output.Written);
                if (stride < width || (long)stride * 4 >= output.Written) { JxlThrowHelper.Invalid("invalid ICC predictor stride."); }
                int count = ReadLength(commands, ref position, data.Length);
                int begin = output.Written;
                WriteShuffled(ref output, Take(ref data, count), width);
                for (int i = 0; i < count; i += width)
                {
                    uint a = output.ReadWord(begin + i - stride, width);
                    uint b = order >= 1 ? output.ReadWord(begin + i - (stride * 2), width) : 0;
                    uint c = order >= 2 ? output.ReadWord(begin + i - (stride * 3), width) : 0;
                    uint prediction = order switch { 0 => a, 1 => unchecked((2 * a) - b), _ => unchecked((3 * (a - b)) + c) };
                    for (int j = 0; j < Math.Min(width, count - i); j++)
                    {
                        int index = begin + i + j;
                        output.Data[index] = unchecked((byte)(output.Data[index] + (prediction >> (8 * (width - 1 - j)))));
                    }
                }
            }
            else if (command == 10)
            {
                output.Write("XYZ "u8);
                output.WriteU32(0);
                output.Write(Take(ref data, 12));
            }
            else if (command is >= 16 and <= 23)
            {
                output.Write(CommonData(command));
                output.WriteU32(0);
            }
            else { JxlThrowHelper.Invalid("invalid ICC reconstruction command."); }
        }
        if (!data.IsEmpty) { JxlThrowHelper.Invalid("unused ICC reconstruction data."); }
        return output.Finish();
    }

    private static byte PredictHeader(int index, uint outputSize, ReadOnlySpan<byte> header) => index switch
    {
        <= 3 => (byte)(outputSize >> (8 * (3 - index))),
        8 => 4,
        >= 12 and <= 23 => "mntrRGB XYZ "u8[index - 12],
        >= 36 and <= 39 => "acsp"u8[index - 36],
        41 or 42 when header[40] == 65 => 80,
        43 when header[40] == 65 => 76,
        41 when header[40] == 77 => 83,
        42 when header[40] == 77 => 70,
        43 when header[40] == 77 => 84,
        42 when header[40] == 83 && header[41] == 71 => 73,
        43 when header[40] == 83 && header[41] == 71 => 32,
        42 when header[40] == 83 && header[41] == 85 => 78,
        43 when header[40] == 83 && header[41] == 85 => 87,
        70 => 246, 71 => 214, 73 => 1, 78 => 211, 79 => 45,
        >= 80 and <= 83 => header[index - 76],
        _ => 0,
    };

    private static ReadOnlySpan<byte> CommonTag(int code) => code switch
    {
        2 or 11 => "rTRC"u8, 3 or 7 => "rXYZ"u8, 4 => "cprt"u8, 5 => "wtpt"u8, 6 => "bkpt"u8,
        8 => "gXYZ"u8, 9 => "bXYZ"u8, 10 => "kXYZ"u8, 12 => "gTRC"u8, 13 => "bTRC"u8, 14 => "kTRC"u8,
        15 => "chad"u8, 16 => "desc"u8, 17 => "chrm"u8, 18 => "dmnd"u8, 19 => "dmdd"u8, 20 => "lumi"u8,
        _ => throw new InvalidImageContentException("JPEG XL: invalid ICC tag command."),
    };

    private static ReadOnlySpan<byte> CommonData(int command) => command switch
    {
        16 => "XYZ "u8, 17 => "desc"u8, 18 => "text"u8, 19 => "mluc"u8,
        20 => "para"u8, 21 => "curv"u8, 22 => "sf32"u8, _ => "gbd "u8,
    };

    private static int ReadLength(ReadOnlySpan<byte> commands, ref int position, int limit)
    {
        ulong value = ReadVarint(commands, ref position);
        if (value > (ulong)limit) { JxlThrowHelper.Invalid("ICC command length exceeds available data."); }
        return (int)value;
    }

    private static ReadOnlySpan<byte> Take(ref ReadOnlySpan<byte> data, int count)
    {
        if (count > data.Length) { JxlThrowHelper.Invalid("truncated ICC reconstruction data."); }
        ReadOnlySpan<byte> result = data[..count];
        data = data[count..];
        return result;
    }

    private static void WriteShuffled(ref IccOutput output, ReadOnlySpan<byte> data, int width)
    {
        int height = data.Length / width, extra = data.Length % width;
        for (int row = 0; row < height; row++)
        {
            int offset = row;
            for (int column = 0; column < width; column++)
            {
                output.WriteByte(data[offset]);
                offset += height + (column < extra ? 1 : 0);
            }
        }
        for (int i = 1; i <= extra; i++) { output.WriteByte(data[((height + 1) * i) - 1]); }
    }

    private struct IccOutput(int size)
    {
        internal byte[] Data = new byte[size];
        internal int Written;

        internal void WriteByte(byte value)
        {
            if (this.Written == this.Data.Length) { JxlThrowHelper.Invalid("ICC reconstruction exceeds declared size."); }
            this.Data[this.Written++] = value;
        }
        internal void Write(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length > this.Data.Length - this.Written) { JxlThrowHelper.Invalid("ICC reconstruction exceeds declared size."); }
            bytes.CopyTo(this.Data.AsSpan(this.Written));
            this.Written += bytes.Length;
        }
        internal void WriteU32(uint value)
        {
            if (this.Data.Length - this.Written < 4) { JxlThrowHelper.Invalid("ICC reconstruction exceeds declared size."); }
            BinaryPrimitives.WriteUInt32BigEndian(this.Data.AsSpan(this.Written), value);
            this.Written += 4;
        }
        internal void WriteTag(ReadOnlySpan<byte> tag, uint start, uint length)
        {
            this.Write(tag);
            this.WriteU32(start);
            this.WriteU32(length);
        }
        internal uint ReadWord(int offset, int width)
        {
            uint value = 0;
            for (int i = 0; i < width; i++) { value = (value << 8) | this.Data[offset + i]; }
            return value;
        }
        internal byte[] Finish()
        {
            if (this.Written != this.Data.Length) { JxlThrowHelper.Invalid("ICC reconstruction does not match declared size."); }
            return this.Data;
        }
    }
}
