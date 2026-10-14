// Copyright (c) the JPEG XL Project Authors. All rights reserved.
// Copyright (c) 2026 Jeremy Ellis and contributors
// Adapted from libjxl lib/jxl/jpeg/enc_jpeg_data_reader.cc,
// ef67fde2ec16d52e644c5a0969230b9a85e3eb31 (BSD-3-Clause plus PATENTS).
// Managed adaptations are licensed under the Apache License, Version 2.0.
// See THIRD-PARTY-NOTICES.txt for the upstream notices and grant.

using System;
using CodeBrix.Imaging.Formats.Jxl.JpegReconstruction;

namespace CodeBrix.Imaging.Formats.Jxl.Encoder.Jpeg;

internal sealed partial class JxlJpegParser
{
    private void DecodeScan(JxlJpegData.Scan scan)
    {
        bool interleaved = scan.Components.Length > 1;
        Component first = this.components[scan.Components[0].Index];
        int stepX = interleaved ? this.maxH : this.maxH / first.Horizontal, stepY = interleaved ? this.maxV : this.maxV / first.Vertical;
        int columns = (this.width + (8 * stepX) - 1) / (8 * stepX), rows = (this.height + (8 * stepY) - 1) / (8 * stepY);
        JxlJpegInputBits input = new(this.source, this.position); int[] dc = new int[this.components.Length];
        int eob = -1, restart = 0; uint blockIndex = 0; Span<int> block = stackalloc int[64];
        for (int my = 0; my < rows; my++)
        {
            for (int mx = 0; mx < columns; mx++)
            {
                this.cancellationToken.ThrowIfCancellationRequested(); int mcu = (my * columns) + mx;
                if (this.restartInterval != 0 && mcu != 0 && mcu % this.restartInterval == 0)
                {
                    this.allPaddingOnes &= input.Finish(this.paddingWriter);
                    if (eob > 0 || input.Position > this.source.Length - 2 || this.source[input.Position] != 255 || this.source[input.Position + 1] != 0xd0 + restart)
                    { JxlThrowHelper.Invalid("invalid JPEG restart marker or unfinished EOB run."); }
                    input.Position += 2; restart = (restart + 1) & 7; Array.Clear(dc); eob = -1;
                }
                foreach (JxlJpegData.ScanComponent selected in scan.Components)
                {
                    Component component = this.components[selected.Index];
                    int horizontal = interleaved ? component.Horizontal : 1, vertical = interleaved ? component.Vertical : 1;
                    for (int by = 0; by < vertical; by++)
                    {
                        for (int bx = 0; bx < horizontal; bx++)
                        {
                            int x = ((mx * horizontal) + bx) * 8, y = ((my * vertical) + by) * 8;
                            for (int i = 0; i < 8; i++) { component.Plane.Row(y + i).Slice(x, 8).CopyTo(block.Slice(i * 8, 8)); }
                            bool reset; int zeroRuns = 0;
                            if (scan.Ah == 0)
                            { DecodeFirst(input, this.dcTables[selected.DcTable], this.acTables[selected.AcTable], scan, block, ref dc[selected.Index], ref eob, out reset, out zeroRuns); }
                            else { DecodeRefinement(input, this.acTables[selected.AcTable], scan, block, ref eob, out reset); }
                            for (int i = 0; i < 8; i++) { block.Slice(i * 8, 8).CopyTo(component.Plane.Row(y + i).Slice(x, 8)); }
                            if (reset) { scan.ResetPoints.Add(blockIndex); }
                            if (zeroRuns != 0) { scan.ExtraZeroRuns.Add(blockIndex, (uint)zeroRuns); }
                            blockIndex++;
                        }
                    }
                }
            }
        }
        if (eob > 0) { JxlThrowHelper.Invalid("JPEG EOB run exceeds the scan."); }
        this.allPaddingOnes &= input.Finish(this.paddingWriter); this.position = input.Position;
        if (this.paddingWriter.Position >= 1 << 24) { throw new NotSupportedException("JPEG padding exceeds the reconstruction format limit."); }
    }

    private static void DecodeFirst(JxlJpegInputBits input, JxlJpegInputHuffman dcTable, JxlJpegInputHuffman acTable,
        JxlJpegData.Scan scan, Span<int> block, ref int dc, ref int eob, out bool reset, out int zeroRuns)
    {
        reset = false; zeroRuns = 0; int start = scan.Ss, end = scan.Se, multiplier = 1 << scan.Al;
        bool runsAllowed = start > 0;
        if (start == 0)
        {
            int bits = dcTable.Read(input);
            if (bits > 11) { JxlThrowHelper.Invalid("invalid JPEG DC magnitude."); }
            dc += Extend(input.Read(bits), bits); block[0] = CheckedCoefficient(dc * multiplier); start++;
        }
        if (start > end) { return; }
        if (eob > 0) { eob--; return; }
        for (int k = start; k <= end; k++)
        {
            int symbol = acTable.Read(input), zeros = symbol >> 4, bits = symbol & 15;
            if (bits != 0)
            {
                k += zeros;
                if (k > end || bits + scan.Al >= 12) { JxlThrowHelper.Invalid("JPEG AC coefficient exceeds its band or precision."); }
                block[JxlJpegScanWriter.NaturalOrder[k]] = CheckedCoefficient(Extend(input.Read(bits), bits) * multiplier); zeroRuns = 0;
            }
            else if (zeros == 15)
            {
                k += 15; zeroRuns++;
                if (k > end) { JxlThrowHelper.Invalid("JPEG zero run exceeds its band."); }
            }
            else
            {
                reset = runsAllowed && k == start && eob == 0;
                if (zeros != 0 && !runsAllowed) { JxlThrowHelper.Invalid("JPEG EOB run crosses a DC coefficient."); }
                eob = (1 << zeros) + input.Read(zeros); break;
            }
        }
        eob--;
    }

    private static void DecodeRefinement(JxlJpegInputBits input, JxlJpegInputHuffman acTable, JxlJpegData.Scan scan,
        Span<int> block, ref int eob, out bool reset)
    {
        reset = false; int start = scan.Ss, end = scan.Se, bit = 1 << scan.Al; bool runsAllowed = start > 0;
        if (start == 0) { block[0] = CheckedCoefficient(block[0] | (input.Read(1) * bit)); start++; }
        if (start > end) { return; }
        int k = start; bool inZeroRun = false;
        if (eob <= 0)
        {
            for (; k <= end; k++)
            {
                int symbol = acTable.Read(input), zeros = symbol >> 4, size = symbol & 15, value = 0;
                if (size != 0)
                {
                    if (size != 1) { JxlThrowHelper.Invalid("invalid JPEG refinement symbol."); }
                    value = input.Read(1) != 0 ? bit : -bit; inZeroRun = false;
                }
                else if (zeros != 15)
                {
                    reset = runsAllowed && k == start && eob == 0;
                    if (zeros != 0 && !runsAllowed) { JxlThrowHelper.Invalid("JPEG refinement EOB run crosses DC."); }
                    eob = (1 << zeros) + input.Read(zeros); break;
                }
                else { inZeroRun = true; }
                do
                {
                    ref int coefficient = ref block[JxlJpegScanWriter.NaturalOrder[k]];
                    if (coefficient != 0) { Refine(input, ref coefficient, bit); }
                    else if (--zeros < 0) { break; }
                    k++;
                } while (k <= end);
                if (value != 0)
                {
                    if (k > end) { JxlThrowHelper.Invalid("JPEG refinement coefficient exceeds its band."); }
                    block[JxlJpegScanWriter.NaturalOrder[k]] = value;
                }
            }
        }
        if (inZeroRun) { JxlThrowHelper.Invalid("extra JPEG refinement zero run before EOB."); }
        if (eob > 0)
        {
            for (; k <= end; k++)
            {
                ref int coefficient = ref block[JxlJpegScanWriter.NaturalOrder[k]];
                if (coefficient != 0) { Refine(input, ref coefficient, bit); }
            }
        }
        eob--;
    }

    private static void Refine(JxlJpegInputBits input, ref int coefficient, int bit)
    {
        if (input.Read(1) != 0 && (coefficient & bit) == 0)
        { coefficient = CheckedCoefficient(coefficient + (coefficient >= 0 ? bit : -bit)); }
    }
    private static int Extend(int value, int bits) => bits == 0 || value >= 1 << (bits - 1) ? value : value + 1 - (1 << bits);
    private static int CheckedCoefficient(int value)
    {
        if (value is < short.MinValue or > short.MaxValue) { JxlThrowHelper.Invalid("JPEG coefficient exceeds signed 16-bit precision."); }
        return value;
    }

    private static int CheckedDcCoefficient(int value)
    {
        // JPEG XL reconstruction clamps the original DC after removing the
        // RGB bias. Accepting a value outside this range would lose JPEG bytes.
        if (value is < -2047 or > 2047)
        { throw new NotSupportedException("JPEG DC coefficients outside -2047 through 2047 cannot be reconstructed byte-exactly from JPEG XL."); }
        return value;
    }
}
