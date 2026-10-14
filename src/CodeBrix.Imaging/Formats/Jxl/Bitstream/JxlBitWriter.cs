// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Inverse of field coding in jxl-oxide crates/jxl-bitstream/src/bitstream.rs,
// 0.12.6, f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

using System;
using System.IO;

namespace CodeBrix.Imaging.Formats.Jxl.Bitstream;

internal sealed class JxlBitWriter
{
    private readonly Stream stream;
    private ulong buffer;
    private int available;

    internal JxlBitWriter(Stream stream) => this.stream = stream;
    internal long Position { get; private set; }

    internal void WriteBits(int count, uint value)
    {
        if ((uint)count > 32 || (count < 32 && value >= (1UL << count)))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        this.buffer |= (ulong)value << this.available;
        this.available += count;
        this.Position += count;
        while (this.available >= 8)
        {
            this.stream.WriteByte((byte)this.buffer);
            this.buffer >>= 8;
            this.available -= 8;
        }
    }

    internal void WriteBool(bool value) => this.WriteBits(1, value ? 1U : 0U);

    internal void WriteU32(uint value, JxlU32 d0, JxlU32 d1, JxlU32 d2, JxlU32 d3)
    {
        ReadOnlySpan<JxlU32> choices = [d0, d1, d2, d3];
        for (int i = 0; i < choices.Length; i++)
        {
            JxlU32 choice = choices[i];
            if (value >= choice.Offset && (ulong)(value - choice.Offset) < (1UL << choice.Bits))
            {
                this.WriteBits(2, (uint)i);
                this.WriteBits(choice.Bits, value - choice.Offset);
                return;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(value), "Value does not fit this JPEG XL field.");
    }

    internal void WriteU64(ulong value)
    {
        if (value == 0) { this.WriteBits(2, 0); return; }
        if (value <= 16) { this.WriteBits(2, 1); this.WriteBits(4, (uint)value - 1); return; }
        if (value <= 272) { this.WriteBits(2, 2); this.WriteBits(8, (uint)value - 17); return; }
        this.WriteBits(2, 3);
        this.WriteBits(12, (uint)(value & 4095));
        value >>= 12;
        for (int shift = 12; ; shift += 8)
        {
            this.WriteBool(value != 0);
            if (value == 0) { return; }
            if (shift == 60) { this.WriteBits(4, (uint)value); return; }
            this.WriteBits(8, (uint)(value & 255));
            value >>= 8;
        }
    }

    internal void WriteEnum(uint value) => this.WriteU32(value, 0, 1, JxlU32.U(4, 2), JxlU32.U(6, 18));

    internal void WriteF16(float value)
    {
        Half half = (Half)value;
        if (!Half.IsFinite(half)) { throw new ArgumentOutOfRangeException(nameof(value)); }
        this.WriteBits(16, BitConverter.HalfToUInt16Bits(half));
    }

    internal void ZeroPadToByte() => this.WriteBits((8 - this.available) & 7, 0);

    internal void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        if (this.available != 0) { throw new InvalidOperationException("Writer must be byte aligned."); }
        this.stream.Write(bytes); this.Position += bytes.Length * 8L;
    }

    internal void WriteBitstream(ReadOnlySpan<byte> bytes, long bitCount)
    {
        if (bitCount < 0 || bitCount > bytes.Length * 8L) { throw new ArgumentOutOfRangeException(nameof(bitCount)); }
        int whole = (int)(bitCount / 8);
        if (this.available == 0) { this.WriteBytes(bytes[..whole]); }
        else { for (int i = 0; i < whole; i++) { this.WriteBits(8, bytes[i]); } }
        int tail = (int)(bitCount & 7);
        if (tail != 0) { this.WriteBits(tail, (uint)(bytes[whole] & ((1 << tail) - 1))); }
    }
    internal static uint PackSigned(int value) => unchecked(((uint)value << 1) ^ (uint)(value >> 31));
}
