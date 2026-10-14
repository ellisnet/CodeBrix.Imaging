// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-coding/src/lib.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

using System;
using System.Numerics;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;

namespace CodeBrix.Imaging.Formats.Jxl.Coding;

internal readonly record struct JxlIntegerConfig(int SplitExponent, int Msb, int Lsb)
{
    internal uint Split => 1U << this.SplitExponent;
    internal static int ContextBits(uint value) => 32 - BitOperations.LeadingZeroCount(value);

    internal static JxlIntegerConfig Read(JxlBitReader reader, int alphabetBits)
    {
        int split = (int)reader.ReadBits(ContextBits((uint)alphabetBits));
        if (split > alphabetBits) { JxlThrowHelper.Invalid("invalid hybrid integer split."); }
        if (split == alphabetBits) { return new(split, 0, 0); }
        int msb = (int)reader.ReadBits(ContextBits((uint)split));
        if (msb > split) { JxlThrowHelper.Invalid("invalid hybrid integer MSB count."); }
        int lsb = (int)reader.ReadBits(ContextBits((uint)(split - msb)));
        if (msb + lsb > split) { JxlThrowHelper.Invalid("invalid hybrid integer LSB count."); }
        return new(split, msb, lsb);
    }

    internal uint Decode(JxlBitReader reader, uint token)
    {
        if (token < this.Split) { return token; }
        int bits = (int)((uint)(this.SplitExponent - this.Msb - this.Lsb) + ((token - this.Split) >> (this.Msb + this.Lsb))) & 31;
        uint low = token & ((1U << this.Lsb) - 1);
        uint high = ((token >> this.Lsb) & ((1U << this.Msb) - 1)) | (1U << this.Msb);
        return (uint)(((((ulong)high << bits) | reader.ReadBits(bits)) << this.Lsb) | low);
    }

    internal void Write(JxlBitWriter writer, int alphabetBits)
    {
        this.Validate(alphabetBits);
        writer.WriteBits(ContextBits((uint)alphabetBits), (uint)this.SplitExponent);
        if (this.SplitExponent == alphabetBits) { return; }
        writer.WriteBits(ContextBits((uint)this.SplitExponent), (uint)this.Msb);
        writer.WriteBits(ContextBits((uint)(this.SplitExponent - this.Msb)), (uint)this.Lsb);
    }

    internal void Validate(int alphabetBits)
    {
        if (alphabetBits is < 0 or > 15 || this.SplitExponent < 0 || this.SplitExponent > alphabetBits || this.Msb < 0 || this.Lsb < 0 ||
            this.Msb > this.SplitExponent || this.Lsb > this.SplitExponent - this.Msb ||
            (this.SplitExponent == alphabetBits && (this.Msb != 0 || this.Lsb != 0)))
        { throw new ArgumentException("Invalid JPEG XL hybrid integer configuration."); }
    }

    internal (uint Token, int ExtraBits, uint Extra) Encode(uint value)
    {
        if (value < this.Split) { return (value, 0, 0); }
        int exponent = BitOperations.Log2(value), bits = exponent - this.Msb - this.Lsb;
        uint high = (value >> (exponent - this.Msb)) & ((1U << this.Msb) - 1);
        uint token = this.Split + ((uint)(exponent - this.SplitExponent) << (this.Msb + this.Lsb)) +
            (high << this.Lsb) + (value & ((1U << this.Lsb) - 1));
        return (token, bits, (value >> this.Lsb) & ((1U << bits) - 1));
    }
}
