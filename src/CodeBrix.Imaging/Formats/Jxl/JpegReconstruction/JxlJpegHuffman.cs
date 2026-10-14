// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Adapted from jxl-oxide crates/jxl-jbr/src/huffman.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).

namespace CodeBrix.Imaging.Formats.Jxl.JpegReconstruction;

internal sealed class JxlJpegHuffman
{
    private readonly byte[] lengths = new byte[256];
    private readonly ushort[] codes = new ushort[256];

    internal static JxlJpegHuffman Build(JxlJpegData.HuffmanCode definition)
    {
        JxlJpegHuffman table = new();
        uint code = 0;
        int index = 0;
        for (int depth = 1; depth <= 16; depth++)
        {
            int count = definition.Counts[depth];
            if (code + count > 1U << depth) { JxlThrowHelper.Invalid("oversubscribed JPEG Huffman table."); }
            for (int i = 0; i < count; i++, index++, code++)
            {
                if (index >= definition.Values.Length - 1) { continue; }
                int symbol = definition.Values[index];
                if ((uint)symbol >= 256 || (!definition.IsAc && symbol > 11) || table.lengths[symbol] != 0)
                { JxlThrowHelper.Invalid("invalid or duplicate JPEG Huffman symbol."); }
                table.lengths[symbol] = (byte)depth; table.codes[symbol] = (ushort)code;
            }
            code <<= 1;
        }
        if (index != definition.Values.Length) { JxlThrowHelper.Invalid("JPEG Huffman counts do not match symbols."); }
        return table;
    }

    internal void Write(JxlJpegBitWriter writer, int symbol)
    {
        if ((uint)symbol >= 256 || this.lengths[symbol] == 0) { JxlThrowHelper.Invalid("JPEG reconstruction needs an absent Huffman symbol."); }
        writer.Write(this.codes[symbol], this.lengths[symbol]);
    }
}
