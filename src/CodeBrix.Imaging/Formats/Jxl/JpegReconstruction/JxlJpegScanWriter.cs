// Copyright (c) 2026 Jeremy Ellis and contributors
// Licensed under the Apache License, Version 2.0.
// Sequential/progressive scan coding adapted from jxl-oxide crates/jxl-jbr/src/
// reconstruct/scan.rs, 0.12.6,
// f8ae722ef2d6b782941c89517d19cfbf605c4a9d (Apache-2.0 option).
// Global-plane MCU traversal and bounded streaming are CodeBrix adaptations.

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;
using CodeBrix.Imaging.Formats.Jxl.Bitstream;
using CodeBrix.Imaging.Formats.Jxl.Modular;

namespace CodeBrix.Imaging.Formats.Jxl.JpegReconstruction;

internal sealed class JxlJpegScanWriter
{
    private readonly JxlJpegBitWriter writer;
    private readonly int[] dc;
    private readonly List<(ulong Bits, int Count)> refinements = [];
    private JxlJpegHuffman lastAc;
    private uint eob;

    private JxlJpegScanWriter(Stream output, int channels)
    { this.writer = new(output); this.dc = new int[channels]; }

    internal static void Write(Stream output, JxlJpegCoefficients coefficients, JxlJpegData.Scan scan, bool progressive,
        JxlJpegHuffman[] dcTables, JxlJpegHuffman[] acTables, int restartInterval,
        JxlBitReader padding, ref int paddingRemaining, ref long remainingScanBlocks, CancellationToken cancellationToken)
    {
        if (scan.Components.Length == 0 || scan.Ss > scan.Se || scan.Se > 63 || scan.Al > 13 || scan.Ah > 13)
        { JxlThrowHelper.Invalid("invalid JPEG scan parameters."); }
        if (!progressive && (scan.Ss != 0 || scan.Se != 63 || scan.Ah != 0 || scan.Al != 0))
        { JxlThrowHelper.Invalid("progressive fields in a sequential JPEG scan."); }
        int componentCount = coefficients.Image.Color.ColorSpace == 1 ? 1 : 3;
        Span<int> hs = stackalloc int[3], vs = stackalloc int[3];
        int maxH = 1, maxV = 1;
        for (int c = 0; c < componentCount; c++)
        {
            uint sampling = coefficients.Frame.JpegUpsampling[c < 2 ? 1 - c : c];
            hs[c] = sampling is 1 or 2 ? 2 : 1;
            vs[c] = sampling is 1 or 3 ? 2 : 1;
            maxH = Math.Max(maxH, hs[c]); maxV = Math.Max(maxV, vs[c]);
        }
        foreach (JxlJpegData.ScanComponent component in scan.Components)
        { if ((uint)component.Index >= componentCount) { JxlThrowHelper.Invalid("JPEG scan component is out of range."); } }
        bool interleaved = scan.Components.Length > 1;
        int first = scan.Components[0].Index;
        int stepX = interleaved ? maxH : maxH / hs[first], stepY = interleaved ? maxV : maxV / vs[first];
        int columns = ((int)coefficients.Image.Size.Width + (8 * stepX) - 1) / (8 * stepX);
        int rows = ((int)coefficients.Image.Size.Height + (8 * stepY) - 1) / (8 * stepY);
        int blocksPerMcu = 0;
        foreach (JxlJpegData.ScanComponent component in scan.Components)
        { blocksPerMcu += interleaved ? hs[component.Index] * vs[component.Index] : 1; }
        long blocks = (long)columns * rows * blocksPerMcu;
        if (blocks > remainingScanBlocks) { JxlThrowHelper.Invalid("JPEG reconstruction exceeds MaxScanBlocks."); }
        remainingScanBlocks -= blocks;
        JxlJpegScanWriter state = new(output, scan.Components.Length);
        int restart = 0;
        uint blockIndex = 0;
        Span<short> ac = stackalloc short[63];
        ReadOnlySpan<byte> natural = NaturalOrder;
        for (int my = 0; my < rows; my++)
        {
            for (int mx = 0; mx < columns; mx++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int mcu = (my * columns) + mx;
                if (restartInterval != 0 && mcu != 0 && mcu % restartInterval == 0)
                {
                    state.Flush(padding, ref paddingRemaining);
                    output.WriteByte(255); output.WriteByte((byte)(0xd0 + restart)); restart = (restart + 1) & 7;
                    Array.Clear(state.dc);
                }
                for (int c = 0; c < scan.Components.Length; c++)
                {
                    JxlJpegData.ScanComponent component = scan.Components[c];
                    JxlJpegHuffman dcTable = dcTables[component.DcTable], acTable = acTables[component.AcTable];
                    state.lastAc ??= acTable;
                    JxlModularPlane plane = coefficients.Planes[coefficients.ComponentPlane(component.Index)];
                    int horizontal = interleaved ? hs[component.Index] : 1, vertical = interleaved ? vs[component.Index] : 1;
                    for (int dy = 0; dy < vertical; dy++)
                    {
                        for (int dx = 0; dx < horizontal; dx++)
                        {
                            int x = ((mx * horizontal) + dx) * 8, y = ((my * vertical) + dy) * 8;
                            if (x + 8 > plane.Width || y + 8 > plane.Height) { JxlThrowHelper.Invalid("JPEG scan exceeds the coefficient plane."); }
                            int? dcValue = scan.Ss == 0 ? plane.Get(x, y) >> scan.Al : null;
                            int start = Math.Max(scan.Ss, 1), count = Math.Max(0, scan.Se + 1 - start);
                            for (int i = 0; i < count; i++)
                            {
                                int position = natural[start + i];
                                int raw = plane.Get(x + (position % 8), y + (position / 8));
                                if (raw is < -32767 or > 32767) { JxlThrowHelper.Invalid("JPEG AC coefficient exceeds signed 16-bit reconstruction precision."); }
                                ac[i] = (short)(raw < 0 ? -((-raw) >> scan.Al) : raw >> scan.Al);
                            }
                            scan.ExtraZeroRuns.TryGetValue(blockIndex, out uint extraZeroRuns);
                            if (scan.ResetPoints.Contains(blockIndex)) { state.EmitEob(); }
                            if (progressive && scan.Ah != 0) { state.Refine(acTable, dcValue, ac[..count], extraZeroRuns); }
                            else { state.First(c, dcTable, acTable, dcValue, ac[..count], extraZeroRuns, progressive); }
                            blockIndex++;
                        }
                    }
                }
            }
        }
        state.Flush(padding, ref paddingRemaining);
    }

    private void First(int component, JxlJpegHuffman dcTable, JxlJpegHuffman acTable, int? dcValue, ReadOnlySpan<short> ac, uint extraZeros, bool progressive)
    {
        if (dcValue.HasValue)
        {
            int difference = dcValue.Value - this.dc[component]; this.dc[component] = dcValue.Value;
            int magnitude = Math.Abs(difference), length = BitOperations.Log2((uint)magnitude) + 1;
            if (magnitude == 0) { length = 0; }
            if (progressive) { this.EmitEob(); }
            this.Symbol(dcTable, length); this.writer.Write(unchecked((ulong)(difference < 0 ? difference - 1 : difference)), length);
        }
        int offset = 0;
        while (offset < ac.Length)
        {
            int zeros = 0;
            while (offset + zeros < ac.Length && ac[offset + zeros] == 0) { zeros++; }
            if (offset + zeros == ac.Length) { break; }
            if (progressive) { this.EmitEob(); }
            int value = ac[offset + zeros]; offset += zeros + 1;
            while (zeros >= 16) { this.Symbol(acTable, 0xf0); zeros -= 16; }
            int length = BitOperations.Log2((uint)Math.Abs(value)) + 1;
            if (length > 15) { JxlThrowHelper.Invalid("JPEG AC coefficient magnitude is too large."); }
            this.Symbol(acTable, (zeros << 4) | length);
            this.writer.Write(unchecked((ulong)(value < 0 ? value - 1 : value)), length);
        }
        if (extraZeros != 0 && progressive) { this.EmitEob(); }
        for (uint i = 0; i < extraZeros; i++) { this.Symbol(acTable, 0xf0); }
        int trailing = ac.Length - offset - ((int)extraZeros * 16);
        if (progressive)
        {
            if (this.eob == 0) { this.lastAc = acTable; }
            if (trailing > 0) { this.eob++; if (this.eob >= 32767) { this.EmitEob(); } }
        }
        else if (trailing > 0) { this.Symbol(acTable, 0); }
    }

    private void Refine(JxlJpegHuffman acTable, int? dcValue, ReadOnlySpan<short> ac, uint extraZeros)
    {
        if (dcValue.HasValue) { this.EmitEob(); this.writer.Write(unchecked((ulong)dcValue.Value), 1); }
        int offset = 0;
        while (offset < ac.Length)
        {
            int next = offset;
            while (next < ac.Length && ac[next] is not (1 or -1)) { next++; }
            if (next == ac.Length) { break; }
            this.EmitEob();
            int zeros = 0, count = 0;
            ulong bits = 0;
            for (int i = offset; i < next; i++)
            {
                if (ac[i] == 0)
                {
                    if (++zeros == 16)
                    { this.Symbol(acTable, 0xf0); this.writer.Write(bits, count); zeros = count = 0; }
                }
                else { bits = (bits << 1) | (uint)(ac[i] & 1); count++; }
            }
            this.Symbol(acTable, (zeros << 4) | 1);
            this.writer.Write(ac[next] == 1 ? 1UL : 0UL, 1); this.writer.Write(bits, count);
            offset = next + 1;
        }
        if (extraZeros != 0) { this.EmitEob(); }
        int zeroRun = 0, refinementCount = 0;
        ulong refinementBits = 0;
        for (int i = offset; i < ac.Length; i++)
        {
            if (ac[i] == 0)
            {
                if (++zeroRun == 16 && extraZeros > 0)
                {
                    this.Symbol(acTable, 0xf0); this.writer.Write(refinementBits, refinementCount);
                    zeroRun = refinementCount = 0; extraZeros--;
                }
            }
            else { refinementBits = (refinementBits << 1) | (uint)(ac[i] & 1); refinementCount++; }
        }
        while (extraZeros != 0)
        {
            extraZeros--;
            this.Symbol(acTable, 0xf0); this.writer.Write(refinementBits, refinementCount);
            zeroRun = refinementCount = 0;
        }
        if (this.eob == 0) { this.lastAc = acTable; }
        if (zeroRun > 0 || refinementCount > 0)
        {
            this.eob++; this.refinements.Add((refinementBits, refinementCount));
            if (this.eob >= 32767) { this.EmitEob(); }
        }
    }

    private void EmitEob()
    {
        if (this.eob == 0) { return; }
        int count = BitOperations.Log2(this.eob);
        this.Symbol(this.lastAc, count << 4); this.writer.Write(this.eob & ((1U << count) - 1), count);
        this.eob = 0;
        foreach ((ulong bits, int length) in this.refinements) { this.writer.Write(bits, length); }
        this.refinements.Clear();
    }
    private void Flush(JxlBitReader padding, ref int paddingRemaining)
    { this.EmitEob(); this.writer.Flush(padding, ref paddingRemaining); }
    private void Symbol(JxlJpegHuffman table, int symbol)
    {
        if (table == null) { JxlThrowHelper.Invalid("JPEG scan references an undefined Huffman table."); }
        table.Write(this.writer, symbol);
    }

    internal static ReadOnlySpan<byte> NaturalOrder =>
    [0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6,
     7, 14, 21, 28, 35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39,
     46, 53, 60, 61, 54, 47, 55, 62, 63];
}
