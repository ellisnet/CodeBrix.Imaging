// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-image/src/lib.rs and
// crates/jxl-oxide-common/src/lib.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

using System;
using System.IO;
using System.Text;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using static CodeBrix.Imaging.Formats.Jxl.Bitstream.JxlU32;

namespace CodeBrix.Imaging.Formats.Jxl.Headers;

internal readonly record struct JxlSizeHeader(uint Width, uint Height)
{
    internal static JxlSizeHeader Read(JxlBitReader reader)
    {
        bool div8 = reader.ReadBool();
        uint height = div8 ? 8 * (1 + reader.ReadBits(5)) : ReadDimension(reader);
        uint ratio = reader.ReadBits(3);
        uint width = ratio == 0 ? (div8 ? 8 * (1 + reader.ReadBits(5)) : ReadDimension(reader)) : RatioWidth(ratio, height);
        return new(width, height);
    }

    internal void Write(JxlBitWriter writer)
    {
        writer.WriteBool(false);
        WriteDimension(writer, this.Height);
        writer.WriteBits(3, 0);
        WriteDimension(writer, this.Width);
    }

    internal static uint ReadDimension(JxlBitReader reader) => reader.ReadU32(U(9, 1), U(13, 1), U(18, 1), U(30, 1));
    internal static void WriteDimension(JxlBitWriter writer, uint value) => writer.WriteU32(value, U(9, 1), U(13, 1), U(18, 1), U(30, 1));

    internal static uint RatioWidth(uint ratio, uint height) => checked((uint)(ratio switch
    {
        1 => height,
        2 => height * 12UL / 10,
        3 => height * 4UL / 3,
        4 => height * 3UL / 2,
        5 => height * 16UL / 9,
        6 => height * 5UL / 4,
        7 => height * 2UL,
        _ => throw new ArgumentOutOfRangeException(nameof(ratio)),
    }));
}

internal readonly record struct JxlBitDepth(uint Bits, uint ExponentBits)
{
    internal static JxlBitDepth Default => new(8, 0);

    internal static JxlBitDepth Read(JxlBitReader reader)
    {
        bool floatingPoint = reader.ReadBool();
        uint bits = floatingPoint ? reader.ReadU32(32, 16, 24, U(6, 1)) : reader.ReadU32(8, 10, 12, U(6, 1));
        uint exponent = floatingPoint ? reader.ReadBits(4) + 1 : 0;
        if (floatingPoint ? exponent < 2 || exponent > 8 || bits < exponent + 3 || bits > exponent + 24 : bits > 31)
        {
            JxlThrowHelper.Invalid("invalid sample depth.");
        }

        return new(bits, exponent);
    }

    internal void Write(JxlBitWriter writer)
    {
        writer.WriteBool(this.ExponentBits != 0);
        if (this.ExponentBits != 0)
        {
            writer.WriteU32(this.Bits, 32, 16, 24, U(6, 1));
            writer.WriteBits(4, this.ExponentBits - 1);
        }
        else { writer.WriteU32(this.Bits, 8, 10, 12, U(6, 1)); }
    }
}

internal static class JxlName
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static string Read(JxlBitReader reader)
    {
        uint length = reader.ReadU32(0, U(4), U(5, 16), U(10, 48));
        Span<byte> bytes = stackalloc byte[(int)length];
        for (int i = 0; i < bytes.Length; i++) { bytes[i] = (byte)reader.ReadBits(8); }
        try { return Utf8.GetString(bytes); }
        catch (DecoderFallbackException) { JxlThrowHelper.Invalid("non-UTF-8 name."); return null; }
    }

    internal static int GetByteCount(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        int length = Utf8.GetByteCount(name);
        if (length > 1071) { throw new ArgumentOutOfRangeException(nameof(name), "JPEG XL names must occupy at most 1071 UTF-8 bytes."); }
        return length;
    }

    internal static void Write(JxlBitWriter writer, string name)
    {
        int length = GetByteCount(name);
        Span<byte> bytes = stackalloc byte[length];
        Utf8.GetBytes(name, bytes);
        writer.WriteU32((uint)length, 0, U(4), U(5, 16), U(10, 48));
        foreach (byte value in bytes) { writer.WriteBits(8, value); }
    }
}

internal sealed class JxlExtraChannel
{
    internal uint Type;
    internal JxlBitDepth BitDepth = JxlBitDepth.Default;
    internal uint DimShift;
    internal string Name = string.Empty;
    internal bool AlphaAssociated;
    internal float[] SpotColor = new float[4];
    internal uint CfaChannel = 1;

    internal static JxlExtraChannel Read(JxlBitReader reader)
    {
        JxlExtraChannel channel = new();
        if (reader.ReadBool()) { return channel; }
        channel.Type = reader.ReadEnum();
        if (channel.Type > 6 && channel.Type != 15 && channel.Type != 16)
        {
            JxlThrowHelper.Invalid("invalid extra channel type.");
        }

        channel.BitDepth = JxlBitDepth.Read(reader);
        channel.DimShift = reader.ReadU32(0, 3, 4, U(3, 1));
        channel.Name = JxlName.Read(reader);
        if (channel.Type == 0) { channel.AlphaAssociated = reader.ReadBool(); }
        if (channel.Type == 2)
        {
            for (int i = 0; i < 4; i++) { channel.SpotColor[i] = reader.ReadF16(); }
        }

        if (channel.Type == 5) { channel.CfaChannel = reader.ReadU32(1, U(2), U(4, 3), U(8, 19)); }
        return channel;
    }

    internal void Write(JxlBitWriter writer)
    {
        writer.WriteBool(false);
        writer.WriteEnum(this.Type);
        this.BitDepth.Write(writer);
        writer.WriteU32(this.DimShift, 0, 3, 4, U(3, 1));
        JxlName.Write(writer, this.Name);
        if (this.Type == 0) { writer.WriteBool(this.AlphaAssociated); }
        if (this.Type == 2) { foreach (float value in this.SpotColor) { writer.WriteF16(value); } }
        if (this.Type == 5) { writer.WriteU32(this.CfaChannel, 1, U(2), U(4, 3), U(8, 19)); }
    }
}

internal sealed class JxlExtensions
{
    internal ulong Mask;
    internal ulong[] BitLengths = new ulong[64];
    internal byte[][] Data = new byte[64][];

    internal static JxlExtensions Read(JxlBitReader reader, long maxBytes)
    {
        JxlExtensions extensions = new() { Mask = reader.ReadU64() };
        ulong totalBytes = 0;
        for (int i = 0; i < 64; i++)
        {
            if ((extensions.Mask & (1UL << i)) == 0) { continue; }
            ulong bits = reader.ReadU64();
            // Subtract before adding to avoid overflow on hostile lengths.
            ulong bytes = bits / 8 + (bits % 8 == 0 ? 0UL : 1UL);
            if (bytes > (ulong)maxBytes - totalBytes || bytes > int.MaxValue)
            {
                JxlThrowHelper.Invalid("extension data exceeds the metadata limit.");
            }

            totalBytes += bytes;
            extensions.BitLengths[i] = bits;
        }

        Span<byte> scratch = stackalloc byte[8192];
        for (int i = 0; i < 64; i++)
        {
            if ((extensions.Mask & (1UL << i)) == 0) { continue; }
            ulong bits = extensions.BitLengths[i];
            // Extension payloads need not be byte aligned. Grow from actual
            // input, so a truncated length declaration cannot reserve megabytes.
            using MemoryStream data = new();
            while (bits >= 8)
            {
                int count = (int)Math.Min((ulong)scratch.Length, bits / 8);
                if ((reader.Position & 7) == 0) { reader.ReadBytes(scratch[..count]); }
                else { for (int j = 0; j < count; j++) { scratch[j] = (byte)reader.ReadBits(8); } }
                data.Write(scratch[..count]);
                bits -= (uint)count * 8UL;
            }
            if (bits != 0) { data.WriteByte((byte)reader.ReadBits((int)bits)); }
            extensions.Data[i] = data.ToArray();
        }

        return extensions;
    }

    internal void Write(JxlBitWriter writer)
    {
        writer.WriteU64(this.Mask);
        for (int i = 0; i < 64; i++)
        {
            if ((this.Mask & (1UL << i)) != 0) { writer.WriteU64(this.BitLengths[i]); }
        }

        for (int i = 0; i < 64; i++)
        {
            if ((this.Mask & (1UL << i)) == 0) { continue; }
            ulong bits = this.BitLengths[i];
            foreach (byte value in this.Data[i])
            {
                int count = (int)Math.Min(8UL, bits);
                writer.WriteBits(count, value);
                bits -= (uint)count;
            }
        }
    }
}
