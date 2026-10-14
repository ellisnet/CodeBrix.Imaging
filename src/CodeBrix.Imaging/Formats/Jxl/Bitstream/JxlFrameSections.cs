// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.

using System.IO;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Headers;
using CodeBrix.Imaging.Memory;

namespace CodeBrix.Imaging.Formats.Jxl.Bitstream;

internal sealed class JxlFrameSections
{
    private readonly JxlBitReader[] readers;
    private readonly int[] lengths;
    internal bool Single => this.readers.Length == 1;

    internal JxlFrameSections(JxlBitReader input, JxlFrameToc toc, MemoryAllocator allocator, CancellationToken cancellationToken)
    {
        if (toc.TotalBytes > (ulong)allocator.MemoryGroupAllocationLimitBytes || toc.TotalBytes > int.MaxValue)
        { JxlThrowHelper.Invalid("compressed frame exceeds allocation limit."); }
        MemoryStream[] sections = new MemoryStream[toc.Sizes.Length];
        for (int i = 0; i < sections.Length; i++)
        {
            sections[i] = input.ReadByteStream((int)toc.Sizes[i]);
        }
        this.readers = new JxlBitReader[sections.Length]; this.lengths = new int[sections.Length];
        for (int i = 0; i < this.readers.Length; i++)
        {
            int physical = toc.LogicalToPhysical?[i] ?? i;
            this.readers[i] = new(sections[physical], cancellationToken);
            this.lengths[i] = (int)sections[physical].Length;
        }
    }

    internal JxlBitReader Get(int logical) => this.readers[this.Single ? 0 : logical];
    internal void Finish(int logical, bool final = false)
    {
        if (this.Single && !final) { return; }
        int index = this.Single ? 0 : logical;
        JxlBitReader reader = this.readers[index];
        reader.ZeroPadToByte();
        if (reader.Position / 8 != this.lengths[index]) { JxlThrowHelper.Invalid("group size does not match TOC."); }
    }
}
