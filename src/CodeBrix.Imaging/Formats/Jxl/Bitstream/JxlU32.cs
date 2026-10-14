// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-bitstream/src/bitstream.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

namespace CodeBrix.Imaging.Formats.Jxl.Bitstream;

internal readonly record struct JxlU32(uint Offset, int Bits)
{
    public static implicit operator JxlU32(uint value) => new(value, 0);
    internal static JxlU32 U(int bits, uint offset = 0) => new(offset, bits);
}
