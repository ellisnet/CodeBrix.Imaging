// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from libjxl lib/jxl/jpeg/enc_jpeg_data_reader.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.JpegReconstruction;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Jpeg;

internal sealed class JxlJpegInputBits(byte[] input, int position)
{
    internal int Position = position;
    private int value, remaining;

    internal int Read(int count)
    {
        int result = 0;
        for (int i = 0; i < count; i++)
        {
            if (this.remaining == 0)
            {
                if (this.Position >= input.Length) { JxlThrowHelper.Invalid("truncated JPEG scan."); }
                this.value = input[this.Position++]; this.remaining = 8;
                if (this.value == 255)
                {
                    if (this.Position >= input.Length || input[this.Position++] != 0)
                    { JxlThrowHelper.Invalid("JPEG scan reached a marker before all coefficients were decoded."); }
                }
            }
            result = (result << 1) | ((this.value >> --this.remaining) & 1);
        }
        return result;
    }

    internal bool Finish(JxlBitWriter padding)
    {
        bool allOnes = true;
        while (this.remaining != 0)
        {
            uint bit = (uint)((this.value >> --this.remaining) & 1); padding.WriteBits(1, bit); allOnes &= bit != 0;
        }
        return allOnes;
    }
}

internal sealed class JxlJpegInputHuffman
{
    private readonly int[] first = new int[17], counts = new int[17], offsets = new int[17];
    private readonly ushort[] values;

    internal JxlJpegInputHuffman(JxlJpegData.HuffmanCode definition)
    {
        this.values = definition.Values;
        int code = 0, index = 0;
        for (int depth = 1; depth <= 16; depth++)
        {
            this.first[depth] = code; this.offsets[depth] = index; this.counts[depth] = definition.Counts[depth];
            code += definition.Counts[depth]; index += definition.Counts[depth];
            if (code > 1 << depth) { JxlThrowHelper.Invalid("oversubscribed JPEG Huffman table."); }
            code <<= 1;
        }
    }

    internal int Read(JxlJpegInputBits input)
    {
        int code = 0;
        for (int depth = 1; depth <= 16; depth++)
        {
            code = (code << 1) | input.Read(1); int offset = code - this.first[depth];
            if ((uint)offset >= this.counts[depth]) { continue; }
            int index = this.offsets[depth] + offset;
            if (index >= this.values.Length - 1) { JxlThrowHelper.Invalid("invalid JPEG Huffman sentinel."); }
            return this.values[index];
        }
        JxlThrowHelper.Invalid("invalid JPEG Huffman symbol."); return 0;
    }
}
