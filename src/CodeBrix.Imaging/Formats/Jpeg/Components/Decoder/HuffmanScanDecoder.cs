// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using CodeBrix.Imaging.IO;

namespace CodeBrix.Imaging.Formats.Jpeg.Components.Decoder; //Was previously: namespace SixLabors.ImageSharp.Formats.Jpeg.Components.Decoder;

/// <summary>
/// Decodes the Huffman encoded spectral scan.
/// Originally ported from <see href="https://github.com/t0rakka/mango"/>
/// with additional fixes for both performance and common encoding errors.
/// </summary>
internal class HuffmanScanDecoder
{
    private readonly BufferedReadStream stream;

    /// <summary>
    /// <see cref="JpegFrame"/> instance containing decoding-related information.
    /// </summary>
    private JpegFrame frame;

    /// <summary>
    /// Shortcut for <see cref="frame"/>.Components.
    /// </summary>
    private JpegComponent[] components;

    /// <summary>
    /// Number of component in the current scan.
    /// </summary>
    private int scanComponentCount;

    /// <summary>
    /// The reset interval determined by RST markers.
    /// </summary>
    private int restartInterval;

    /// <summary>
    /// How many mcu's are left to do.
    /// </summary>
    private int todo;

    /// <summary>
    /// The End-Of-Block countdown for ending the sequence prematurely when the remaining coefficients are zero.
    /// </summary>
    private int eobrun;

    /// <summary>
    /// The DC Huffman tables.
    /// </summary>
    private readonly HuffmanTable[] dcHuffmanTables;

    /// <summary>
    /// The AC Huffman tables
    /// </summary>
    private readonly HuffmanTable[] acHuffmanTables;

    private HuffmanScanBuffer scanBuffer;

    private readonly SpectralConverter spectralConverter;

    private readonly CancellationToken cancellationToken;

    /// <summary>
    /// Initializes a new instance of the <see cref="HuffmanScanDecoder"/> class.
    /// </summary>
    /// <param name="stream">The input stream.</param>
    /// <param name="converter">Spectral to pixel converter.</param>
    /// <param name="cancellationToken">The token to monitor cancellation.</param>
    public HuffmanScanDecoder(
        BufferedReadStream stream,
        SpectralConverter converter,
        CancellationToken cancellationToken)
    {
        this.stream = stream;
        this.spectralConverter = converter;
        this.cancellationToken = cancellationToken;

        // TODO: this is actually a variable value depending on component count
        const int maxTables = 4;
        this.dcHuffmanTables = new HuffmanTable[maxTables];
        this.acHuffmanTables = new HuffmanTable[maxTables];
    }

    /// <summary>
    /// Sets reset interval determined by RST markers.
    /// </summary>
    public int ResetInterval
    {
        set
        {
            this.restartInterval = value;
            this.todo = value;
        }
    }

    // The spectral selection start.
    public int SpectralStart { get; set; }

    // The spectral selection end.
    public int SpectralEnd { get; set; }

    // The successive approximation high bit end.
    public int SuccessiveHigh { get; set; }

    // The successive approximation low bit end.
    public int SuccessiveLow { get; set; }

    /// <summary>
    /// Decodes the entropy coded data.
    /// </summary>
    /// <param name="scanComponentCount">Component count in the current scan.</param>
    public void ParseEntropyCodedData(int scanComponentCount)
    {
        this.cancellationToken.ThrowIfCancellationRequested();

        this.scanComponentCount = scanComponentCount;

        this.scanBuffer = new HuffmanScanBuffer(this.stream);

        var fullScan = this.frame.Progressive || this.frame.MultiScan;
        this.frame.AllocateComponents(fullScan);

        if (!this.frame.Progressive)
        {
            this.ParseBaselineData();
        }
        else
        {
            this.ParseProgressiveData();
        }

        if (this.scanBuffer.HasBadMarker())
        {
            this.stream.Position = this.scanBuffer.MarkerPosition;
        }
    }

    public void InjectFrameData(JpegFrame frame, IRawJpegData jpegData)
    {
        this.frame = frame;
        this.components = frame.Components;

        this.spectralConverter.InjectFrameData(frame, jpegData);
    }

    private void ParseBaselineData()
    {
        if (this.scanComponentCount != 1)
        {
            this.ParseBaselineDataInterleaved();
            this.spectralConverter.CommitConversion();
        }
        else if (this.frame.ComponentCount == 1)
        {
            this.ParseBaselineDataSingleComponent();
            this.spectralConverter.CommitConversion();
        }
        else
        {
            this.ParseBaselineDataNonInterleaved();
        }
    }

    private void ParseBaselineDataInterleaved()
    {
        var mcu = 0;
        var mcusPerColumn = this.frame.McusPerColumn;
        var mcusPerLine = this.frame.McusPerLine;
        ref var buffer = ref this.scanBuffer;

        for (var j = 0; j < mcusPerColumn; j++)
        {
            this.cancellationToken.ThrowIfCancellationRequested();

            // decode from binary to spectral
            for (var i = 0; i < mcusPerLine; i++)
            {
                // Scan an interleaved mcu... process components in order
                var mcuCol = mcu % mcusPerLine;
                for (var k = 0; k < this.scanComponentCount; k++)
                {
                    int order = this.frame.ComponentOrder[k];
                    var component = this.components[order];

                    ref var dcHuffmanTable = ref this.dcHuffmanTables[component.DCHuffmanTableId];
                    ref var acHuffmanTable = ref this.acHuffmanTables[component.ACHuffmanTableId];

                    var h = component.HorizontalSamplingFactor;
                    var v = component.VerticalSamplingFactor;

                    // Scan out an mcu's worth of this component; that's just determined
                    // by the basic H and V specified for the component
                    for (var y = 0; y < v; y++)
                    {
                        var blockSpan = component.SpectralBlocks.DangerousGetRowSpan(y);
                        ref var blockRef = ref MemoryMarshal.GetReference(blockSpan);

                        for (var x = 0; x < h; x++)
                        {
                            if (buffer.NoData)
                            {
                                // It is very likely that some spectral data was decoded before we've encountered 'end of scan'
                                // so we need to decode what's left and return (or maybe throw?)
                                this.spectralConverter.ConvertStrideBaseline();
                                return;
                            }

                            var blockCol = (mcuCol * h) + x;

                            this.DecodeBlockBaseline(
                                component,
                                ref Unsafe.Add(ref blockRef, blockCol),
                                ref dcHuffmanTable,
                                ref acHuffmanTable);
                        }
                    }
                }

                // After all interleaved components, that's an interleaved MCU,
                // so now count down the restart interval
                mcu++;
                this.HandleRestart();
            }

            // Convert from spectral to actual pixels via given converter
            this.spectralConverter.ConvertStrideBaseline();
        }
    }

    private void ParseBaselineDataNonInterleaved()
    {
        var component = this.components[this.frame.ComponentOrder[0]];
        ref var buffer = ref this.scanBuffer;

        var w = component.WidthInBlocks;
        var h = component.HeightInBlocks;

        ref var dcHuffmanTable = ref this.dcHuffmanTables[component.DCHuffmanTableId];
        ref var acHuffmanTable = ref this.acHuffmanTables[component.ACHuffmanTableId];

        for (var j = 0; j < h; j++)
        {
            this.cancellationToken.ThrowIfCancellationRequested();
            var blockSpan = component.SpectralBlocks.DangerousGetRowSpan(j);
            ref var blockRef = ref MemoryMarshal.GetReference(blockSpan);

            for (var i = 0; i < w; i++)
            {
                if (buffer.NoData)
                {
                    return;
                }

                this.DecodeBlockBaseline(
                    component,
                    ref Unsafe.Add(ref blockRef, i),
                    ref dcHuffmanTable,
                    ref acHuffmanTable);

                this.HandleRestart();
            }
        }
    }

    private void ParseBaselineDataSingleComponent()
    {
        var component = this.frame.Components[0];
        var mcuLines = this.frame.McusPerColumn;
        var w = component.WidthInBlocks;
        var h = component.SamplingFactors.Height;
        ref var dcHuffmanTable = ref this.dcHuffmanTables[component.DCHuffmanTableId];
        ref var acHuffmanTable = ref this.acHuffmanTables[component.ACHuffmanTableId];

        ref var buffer = ref this.scanBuffer;

        for (var i = 0; i < mcuLines; i++)
        {
            this.cancellationToken.ThrowIfCancellationRequested();

            // decode from binary to spectral
            for (var j = 0; j < h; j++)
            {
                var blockSpan = component.SpectralBlocks.DangerousGetRowSpan(j);
                ref var blockRef = ref MemoryMarshal.GetReference(blockSpan);

                for (var k = 0; k < w; k++)
                {
                    if (buffer.NoData)
                    {
                        // It is very likely that some spectral data was decoded before we've encountered 'end of scan'
                        // so we need to decode what's left and return (or maybe throw?)
                        this.spectralConverter.ConvertStrideBaseline();
                        return;
                    }

                    this.DecodeBlockBaseline(
                        component,
                        ref Unsafe.Add(ref blockRef, k),
                        ref dcHuffmanTable,
                        ref acHuffmanTable);

                    this.HandleRestart();
                }
            }

            // Convert from spectral to actual pixels via given converter
            this.spectralConverter.ConvertStrideBaseline();
        }
    }

    private void CheckProgressiveData()
    {
        // Validate successive scan parameters.
        // Logic has been adapted from libjpeg.
        // See Table B.3 – Scan header parameter size and values. itu-t81.pdf
        var invalid = false;
        if (this.SpectralStart == 0)
        {
            if (this.SpectralEnd != 0)
            {
                invalid = true;
            }
        }
        else
        {
            // Need not check Ss/Se < 0 since they came from unsigned bytes.
            if (this.SpectralEnd < this.SpectralStart || this.SpectralEnd > 63)
            {
                invalid = true;
            }

            // AC scans may have only one component.
            if (this.scanComponentCount != 1)
            {
                invalid = true;
            }
        }

        if (this.SuccessiveHigh != 0)
        {
            // Successive approximation refinement scan: must have Al = Ah-1.
            if (this.SuccessiveHigh - 1 != this.SuccessiveLow)
            {
                invalid = true;
            }
        }

        // TODO: How does this affect 12bit jpegs.
        // According to libjpeg the range covers 8bit only?
        if (this.SuccessiveLow > 13)
        {
            invalid = true;
        }

        if (invalid)
        {
            JpegThrowHelper.ThrowBadProgressiveScan(this.SpectralStart, this.SpectralEnd, this.SuccessiveHigh, this.SuccessiveLow);
        }
    }

    private void ParseProgressiveData()
    {
        this.CheckProgressiveData();

        if (this.scanComponentCount == 1)
        {
            this.ParseProgressiveDataNonInterleaved();
        }
        else
        {
            this.ParseProgressiveDataInterleaved();
        }
    }

    private void ParseProgressiveDataInterleaved()
    {
        // Interleaved
        var mcu = 0;
        var mcusPerColumn = this.frame.McusPerColumn;
        var mcusPerLine = this.frame.McusPerLine;
        ref var buffer = ref this.scanBuffer;

        for (var j = 0; j < mcusPerColumn; j++)
        {
            for (var i = 0; i < mcusPerLine; i++)
            {
                // Scan an interleaved mcu... process components in order
                var mcuRow = mcu / mcusPerLine;
                var mcuCol = mcu % mcusPerLine;
                for (var k = 0; k < this.scanComponentCount; k++)
                {
                    int order = this.frame.ComponentOrder[k];
                    var component = this.components[order];
                    ref var dcHuffmanTable = ref this.dcHuffmanTables[component.DCHuffmanTableId];

                    var h = component.HorizontalSamplingFactor;
                    var v = component.VerticalSamplingFactor;

                    // Scan out an mcu's worth of this component; that's just determined
                    // by the basic H and V specified for the component
                    for (var y = 0; y < v; y++)
                    {
                        var blockRow = (mcuRow * v) + y;
                        var blockSpan = component.SpectralBlocks.DangerousGetRowSpan(blockRow);
                        ref var blockRef = ref MemoryMarshal.GetReference(blockSpan);

                        for (var x = 0; x < h; x++)
                        {
                            if (buffer.NoData)
                            {
                                return;
                            }

                            var blockCol = (mcuCol * h) + x;

                            this.DecodeBlockProgressiveDC(
                                component,
                                ref Unsafe.Add(ref blockRef, blockCol),
                                ref dcHuffmanTable);
                        }
                    }
                }

                // After all interleaved components, that's an interleaved MCU,
                // so now count down the restart interval
                mcu++;
                this.HandleRestart();
            }
        }
    }

    private void ParseProgressiveDataNonInterleaved()
    {
        var component = this.components[this.frame.ComponentOrder[0]];
        ref var buffer = ref this.scanBuffer;

        var w = component.WidthInBlocks;
        var h = component.HeightInBlocks;

        if (this.SpectralStart == 0)
        {
            ref var dcHuffmanTable = ref this.dcHuffmanTables[component.DCHuffmanTableId];

            for (var j = 0; j < h; j++)
            {
                this.cancellationToken.ThrowIfCancellationRequested();

                var blockSpan = component.SpectralBlocks.DangerousGetRowSpan(j);
                ref var blockRef = ref MemoryMarshal.GetReference(blockSpan);

                for (var i = 0; i < w; i++)
                {
                    if (buffer.NoData)
                    {
                        return;
                    }

                    this.DecodeBlockProgressiveDC(
                        component,
                        ref Unsafe.Add(ref blockRef, i),
                        ref dcHuffmanTable);

                    this.HandleRestart();
                }
            }
        }
        else
        {
            ref var acHuffmanTable = ref this.acHuffmanTables[component.ACHuffmanTableId];

            for (var j = 0; j < h; j++)
            {
                this.cancellationToken.ThrowIfCancellationRequested();

                var blockSpan = component.SpectralBlocks.DangerousGetRowSpan(j);
                ref var blockRef = ref MemoryMarshal.GetReference(blockSpan);

                for (var i = 0; i < w; i++)
                {
                    if (buffer.NoData)
                    {
                        return;
                    }

                    this.DecodeBlockProgressiveAC(
                        ref Unsafe.Add(ref blockRef, i),
                        ref acHuffmanTable);

                    this.HandleRestart();
                }
            }
        }
    }

    private void DecodeBlockBaseline(
        JpegComponent component,
        ref Block8x8 block,
        ref HuffmanTable dcTable,
        ref HuffmanTable acTable)
    {
        ref var blockDataRef = ref Unsafe.As<Block8x8, short>(ref block);
        ref var buffer = ref this.scanBuffer;

        // DC
        var t = buffer.DecodeHuffman(ref dcTable);
        if (t != 0)
        {
            t = buffer.Receive(t);
        }

        t += component.DcPredictor;
        component.DcPredictor = t;
        blockDataRef = (short)t;

        // AC
        for (var i = 1; i < 64;)
        {
            var s = buffer.DecodeHuffman(ref acTable);

            var r = s >> 4;
            s &= 15;

            if (s != 0)
            {
                i += r;
                s = buffer.Receive(s);
                Unsafe.Add(ref blockDataRef, ZigZag.TransposingOrder[i++]) = (short)s;
            }
            else
            {
                if (r == 0)
                {
                    break;
                }

                i += 16;
            }
        }
    }

    private void DecodeBlockProgressiveDC(JpegComponent component, ref Block8x8 block, ref HuffmanTable dcTable)
    {
        ref var blockDataRef = ref Unsafe.As<Block8x8, short>(ref block);
        ref var buffer = ref this.scanBuffer;

        if (this.SuccessiveHigh == 0)
        {
            // First scan for DC coefficient, must be first
            var s = buffer.DecodeHuffman(ref dcTable);
            if (s != 0)
            {
                s = buffer.Receive(s);
            }

            s += component.DcPredictor;
            component.DcPredictor = s;
            blockDataRef = (short)(s << this.SuccessiveLow);
        }
        else
        {
            // Refinement scan for DC coefficient
            buffer.CheckBits();
            blockDataRef |= (short)(buffer.GetBits(1) << this.SuccessiveLow);
        }
    }

    private void DecodeBlockProgressiveAC(ref Block8x8 block, ref HuffmanTable acTable)
    {
        ref var blockDataRef = ref Unsafe.As<Block8x8, short>(ref block);
        if (this.SuccessiveHigh == 0)
        {
            // MCU decoding for AC initial scan (either spectral selection,
            // or first pass of successive approximation).
            if (this.eobrun != 0)
            {
                --this.eobrun;
                return;
            }

            ref var buffer = ref this.scanBuffer;
            var start = this.SpectralStart;
            var end = this.SpectralEnd;
            var low = this.SuccessiveLow;

            for (var i = start; i <= end; ++i)
            {
                var s = buffer.DecodeHuffman(ref acTable);
                var r = s >> 4;
                s &= 15;

                i += r;

                if (s != 0)
                {
                    s = buffer.Receive(s);
                    Unsafe.Add(ref blockDataRef, ZigZag.TransposingOrder[i]) = (short)(s << low);
                }
                else
                {
                    if (r != 15)
                    {
                        this.eobrun = 1 << r;
                        if (r != 0)
                        {
                            buffer.CheckBits();
                            this.eobrun += buffer.GetBits(r);
                        }

                        --this.eobrun;
                        break;
                    }
                }
            }
        }
        else
        {
            // Refinement scan for these AC coefficients
            this.DecodeBlockProgressiveACRefined(ref blockDataRef, ref acTable);
        }
    }

    private void DecodeBlockProgressiveACRefined(ref short blockDataRef, ref HuffmanTable acTable)
    {
        // Refinement scan for these AC coefficients
        ref var buffer = ref this.scanBuffer;
        var start = this.SpectralStart;
        var end = this.SpectralEnd;

        var p1 = 1 << this.SuccessiveLow;
        var m1 = (-1) << this.SuccessiveLow;

        var k = start;

        if (this.eobrun == 0)
        {
            for (; k <= end; k++)
            {
                var s = buffer.DecodeHuffman(ref acTable);
                var r = s >> 4;
                s &= 15;

                if (s != 0)
                {
                    buffer.CheckBits();
                    if (buffer.GetBits(1) != 0)
                    {
                        s = p1;
                    }
                    else
                    {
                        s = m1;
                    }
                }
                else
                {
                    if (r != 15)
                    {
                        this.eobrun = 1 << r;

                        if (r != 0)
                        {
                            buffer.CheckBits();
                            this.eobrun += buffer.GetBits(r);
                        }

                        break;
                    }
                }

                do
                {
                    ref var coef = ref Unsafe.Add(ref blockDataRef, ZigZag.TransposingOrder[k]);
                    if (coef != 0)
                    {
                        buffer.CheckBits();
                        if (buffer.GetBits(1) != 0)
                        {
                            if ((coef & p1) == 0)
                            {
                                coef += (short)(coef >= 0 ? p1 : m1);
                            }
                        }
                    }
                    else
                    {
                        if (--r < 0)
                        {
                            break;
                        }
                    }

                    k++;
                }
                while (k <= end);

                if ((s != 0) && (k < 64))
                {
                    Unsafe.Add(ref blockDataRef, ZigZag.TransposingOrder[k]) = (short)s;
                }
            }
        }

        if (this.eobrun > 0)
        {
            for (; k <= end; k++)
            {
                ref var coef = ref Unsafe.Add(ref blockDataRef, ZigZag.TransposingOrder[k]);

                if (coef != 0)
                {
                    buffer.CheckBits();
                    if (buffer.GetBits(1) != 0)
                    {
                        if ((coef & p1) == 0)
                        {
                            coef += (short)(coef >= 0 ? p1 : m1);
                        }
                    }
                }
            }

            --this.eobrun;
        }
    }

    [MethodImpl(InliningOptions.ShortMethod)]
    private void Reset()
    {
        for (var i = 0; i < this.components.Length; i++)
        {
            this.components[i].DcPredictor = 0;
        }

        this.eobrun = 0;
        this.scanBuffer.Reset();
    }

    [MethodImpl(InliningOptions.ShortMethod)]
    private bool HandleRestart()
    {
        if (this.restartInterval > 0 && (--this.todo) == 0)
        {
            if (this.scanBuffer.Marker == JpegConstants.Markers.XFF)
            {
                if (!this.scanBuffer.FindNextMarker())
                {
                    return false;
                }
            }

            this.todo = this.restartInterval;

            if (this.scanBuffer.HasRestartMarker())
            {
                this.Reset();
                return true;
            }

            if (this.scanBuffer.HasBadMarker())
            {
                this.stream.Position = this.scanBuffer.MarkerPosition;
                this.Reset();
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Build the huffman table using code lengths and code values.
    /// </summary>
    /// <param name="type">Table type.</param>
    /// <param name="index">Table index.</param>
    /// <param name="codeLengths">Code lengths.</param>
    /// <param name="values">Code values.</param>
    /// <param name="workspace">The provided spare workspace memory, can be dirty.</param>
    [MethodImpl(InliningOptions.ShortMethod)]
    public void BuildHuffmanTable(int type, int index, ReadOnlySpan<byte> codeLengths, ReadOnlySpan<byte> values, Span<uint> workspace)
    {
        var tables = type == 0 ? this.dcHuffmanTables : this.acHuffmanTables;
        tables[index] = new HuffmanTable(codeLengths, values, workspace);
    }
}