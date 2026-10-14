// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// JPEG bit packing and byte stuffing adapted from jxl-oxide crates/jxl-jbr/src/
// bit_writer.rs, 0.12.6, f8ae722ef2d6b782941c89517d19cfbf605c4a9d
// (Apache-2.0 option). The bounded streaming buffer is specific to CodeBrix.
// Padding-bit order follows libjxl lib/jxl/jpeg/dec_jpeg_data_writer.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause + PATENTS).
// Copyright (c) the JPEG XL Project Authors. All rights reserved.

using System;
using System.IO;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;

namespace CodeBrix.Imaging.Formats.Jxl.JpegReconstruction;

internal sealed class JxlJpegBitWriter(Stream output)
{
    private readonly byte[] bytes = new byte[4096];
    private int used, buffered;
    private ulong pending;
    internal int PaddingBits => (8 - this.buffered) & 7;

    internal void Write(ulong bits, int count)
    {
        if ((uint)count > 64) { throw new ArgumentOutOfRangeException(nameof(count)); }
        while (count != 0)
        {
            int take = Math.Min(count, 56);
            this.pending = (this.pending << take) | ((bits >> (count - take)) & ((1UL << take) - 1));
            this.buffered += take; count -= take;
            while (this.buffered >= 8)
            {
                this.buffered -= 8;
                byte value = (byte)(this.pending >> this.buffered);
                this.Put(value);
                if (value == 0xff) { this.Put(0); }
            }
            this.pending &= (1UL << this.buffered) - 1;
        }
    }

    internal void Flush(JxlBitReader padding, ref int paddingRemaining)
    {
        int count = this.PaddingBits;
        if (count != 0)
        {
            if (padding != null && count > paddingRemaining) { JxlThrowHelper.Invalid("JPEG reconstruction padding is exhausted."); }
            // jbrd stores padding in chronological LSB order; JPEG writes that order MSB first.
            if (padding == null) { this.Write(255, count); }
            else { for (int i = 0; i < count; i++) { this.Write(padding.ReadBits(1), 1); } }
            if (padding != null) { paddingRemaining -= count; }
        }
        this.Drain();
    }
    private void Put(byte value)
    {
        if (this.used == this.bytes.Length) { this.Drain(); }
        this.bytes[this.used++] = value;
    }
    private void Drain()
    {
        if (this.used == 0) { return; }
        output.Write(this.bytes.AsSpan(0, this.used)); this.used = 0;
    }
}
