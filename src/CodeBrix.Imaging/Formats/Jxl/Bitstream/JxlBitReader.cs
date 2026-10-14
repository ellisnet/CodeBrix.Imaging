// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Field coding adapted from jxl-oxide crates/jxl-bitstream/src/bitstream.rs,
// 0.12.6, f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).
// Stream buffering is independently implemented for the CodeBrix stream API.

using System;
using System.IO;
using System.Threading;

namespace CodeBrix.Imaging.Formats.Jxl.Bitstream;

internal sealed class JxlBitReader
{
    private readonly Stream stream;
    private readonly CancellationToken cancellationToken;
    private ulong buffer;
    private int available;

    internal JxlBitReader(Stream stream, CancellationToken cancellationToken = default)
    {
        this.stream = stream;
        this.cancellationToken = cancellationToken;
    }

    internal long Position { get; private set; }
    internal void CheckCancellation() => this.cancellationToken.ThrowIfCancellationRequested();

    // Zero-padded lookahead is needed by prefix decoding near the stream end.
    // ConsumeBits always verifies the actual number of available bits.
    internal uint PeekBits(int count)
    {
        if ((uint)count > 32) { throw new ArgumentOutOfRangeException(nameof(count)); }
        this.cancellationToken.ThrowIfCancellationRequested();
        while (this.available < count)
        {
            int value = this.stream.ReadByte();
            if (value < 0) { break; }
            this.buffer |= (ulong)(byte)value << this.available;
            this.available += 8;
        }

        return (uint)(this.buffer & ((1UL << count) - 1));
    }

    internal void ConsumeBits(int count)
    {
        if ((uint)count > (uint)this.available) { JxlThrowHelper.Invalid("truncated bitstream."); }
        this.buffer >>= count;
        this.available -= count;
        this.Position += count;
    }

    internal uint ReadBits(int count)
    {
        uint value = this.PeekBits(count);
        this.ConsumeBits(count);
        return value;
    }

    internal bool ReadBool() => this.ReadBits(1) != 0;

    internal void ReadBytes(Span<byte> destination)
    {
        if ((this.Position & 7) != 0) { throw new InvalidOperationException("Reader must be byte aligned."); }
        int offset = 0;
        while (this.available >= 8 && offset < destination.Length) { destination[offset++] = (byte)this.ReadBits(8); }
        while (offset < destination.Length)
        {
            this.cancellationToken.ThrowIfCancellationRequested();
            int read = this.stream.Read(destination[offset..]);
            if (read == 0) { JxlThrowHelper.Invalid("truncated byte payload."); }
            offset += read;
            this.Position += read * 8L;
        }
    }

    // Do not reserve a declared payload length before receiving its bytes.
    // A tiny truncated file may claim a section close to a gigabyte long.
    internal MemoryStream ReadByteStream(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        MemoryStream result = new();
        try
        {
            Span<byte> scratch = stackalloc byte[8192];
            while (length != 0)
            {
                int count = Math.Min(length, scratch.Length);
                this.ReadBytes(scratch[..count]);
                result.Write(scratch[..count]);
                length -= count;
            }
            result.Position = 0;
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    internal uint ReadU32(JxlU32 d0, JxlU32 d1, JxlU32 d2, JxlU32 d3)
    {
        JxlU32 spec = this.ReadBits(2) switch { 0 => d0, 1 => d1, 2 => d2, _ => d3 };
        return unchecked(spec.Offset + this.ReadBits(spec.Bits));
    }

    internal ulong ReadU64()
    {
        uint selector = this.ReadBits(2);
        if (selector == 0) { return 0; }
        if (selector == 1) { return this.ReadBits(4) + 1UL; }
        if (selector == 2) { return this.ReadBits(8) + 17UL; }
        ulong value = this.ReadBits(12);
        for (int shift = 12; this.ReadBool(); shift += 8)
        {
            if (shift == 60) { return value | ((ulong)this.ReadBits(4) << 60); }
            value |= (ulong)this.ReadBits(8) << shift;
        }

        return value;
    }

    internal uint ReadEnum() => this.ReadU32(0, 1, JxlU32.U(4, 2), JxlU32.U(6, 18));

    internal float ReadF16()
    {
        float value = (float)BitConverter.UInt16BitsToHalf((ushort)this.ReadBits(16));
        if (!float.IsFinite(value)) { JxlThrowHelper.Invalid("non-finite F16 field."); }
        return value;
    }

    internal void ZeroPadToByte()
    {
        if (this.ReadBits((int)((8 - (this.Position & 7)) & 7)) != 0)
        {
            JxlThrowHelper.Invalid("nonzero alignment padding.");
        }
    }

    internal void SkipBits(ulong count)
    {
        if ((this.Position & 7) == 0 && count >= 8)
        {
            while (this.available >= 8 && count >= 8) { this.ConsumeBits(8); count -= 8; }
            Span<byte> scratch = stackalloc byte[8192];
            while (count >= 8)
            {
                this.cancellationToken.ThrowIfCancellationRequested();
                int length = (int)Math.Min(count / 8, (ulong)scratch.Length);
                int read = this.stream.Read(scratch[..length]);
                if (read == 0) { JxlThrowHelper.Invalid("truncated skipped data."); }
                count -= (uint)read * 8UL;
                this.Position += read * 8L;
            }
        }
        while (count != 0)
        {
            int take = (int)Math.Min(count, 32UL);
            this.ReadBits(take);
            count -= (uint)take;
        }
    }

    internal static int UnpackSigned(uint value) => unchecked((int)(value >> 1) ^ -((int)value & 1));
}
