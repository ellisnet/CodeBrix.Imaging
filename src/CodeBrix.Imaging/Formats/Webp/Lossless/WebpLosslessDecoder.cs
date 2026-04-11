// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CodeBrix.Imaging.Formats.Webp.BitReader;
using CodeBrix.Imaging.Memory;
using CodeBrix.Imaging.PixelFormats;

namespace CodeBrix.Imaging.Formats.Webp.Lossless; //Was previously: namespace SixLabors.ImageSharp.Formats.Webp.Lossless;

/// <summary>
/// Decoder for lossless webp images. This code is a port of libwebp, which can be found here: https://chromium.googlesource.com/webm/libwebp
/// </summary>
/// <remarks>
/// The lossless specification can be found here:
/// https://developers.google.com/speed/webp/docs/webp_lossless_bitstream_specification
/// </remarks>
internal sealed class WebpLosslessDecoder
{
    /// <summary>
    /// A bit reader for reading lossless webp streams.
    /// </summary>
    private readonly Vp8LBitReader bitReader;

    /// <summary>
    /// The global configuration.
    /// </summary>
    private readonly Configuration configuration;

    /// <summary>
    /// Used for allocating memory during processing operations.
    /// </summary>
    private readonly MemoryAllocator memoryAllocator;

    private const int BitsSpecialMarker = 0x100;

    private const uint PackedNonLiteralCode = 0;

    private static readonly int CodeToPlaneCodes = WebpLookupTables.CodeToPlane.Length;

    // Memory needed for lookup tables of one Huffman tree group. Red, blue, alpha and distance alphabets are constant (256 for red, blue and alpha, 40 for
    // distance) and lookup table sizes for them in worst case are 630 and 410 respectively. Size of green alphabet depends on color cache size and is equal
    // to 256 (green component values) + 24 (length prefix values) + color_cache_size (between 0 and 2048).
    // All values computed for 8-bit first level lookup with Mark Adler's tool:
    // http://www.hdfgroup.org/ftp/lib-external/zlib/zlib-1.2.5/examples/enough.c
    private const int FixedTableSize = (630 * 3) + 410;

    private static readonly int[] TableSize =
    {
        FixedTableSize + 654,
        FixedTableSize + 656,
        FixedTableSize + 658,
        FixedTableSize + 662,
        FixedTableSize + 670,
        FixedTableSize + 686,
        FixedTableSize + 718,
        FixedTableSize + 782,
        FixedTableSize + 912,
        FixedTableSize + 1168,
        FixedTableSize + 1680,
        FixedTableSize + 2704
    };

    private static readonly int NumCodeLengthCodes = CodeLengthCodeOrder.Length;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebpLosslessDecoder"/> class.
    /// </summary>
    /// <param name="bitReader">Bitreader to read from the stream.</param>
    /// <param name="memoryAllocator">Used for allocating memory during processing operations.</param>
    /// <param name="configuration">The configuration.</param>
    public WebpLosslessDecoder(Vp8LBitReader bitReader, MemoryAllocator memoryAllocator, Configuration configuration)
    {
        this.bitReader = bitReader;
        this.memoryAllocator = memoryAllocator;
        this.configuration = configuration;
    }

    // This uses C#'s compiler optimization to refer to assembly's static data directly.
    private static ReadOnlySpan<byte> CodeLengthCodeOrder => new byte[] { 17, 18, 0, 1, 2, 3, 4, 5, 16, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };

    // This uses C#'s compiler optimization to refer to assembly's static data directly.
    private static ReadOnlySpan<byte> LiteralMap => new byte[] { 0, 1, 1, 1, 0 };

    /// <summary>
    /// Decodes the image from the stream using the bitreader.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="pixels">The pixel buffer to store the decoded data.</param>
    /// <param name="width">The width of the image.</param>
    /// <param name="height">The height of the image.</param>
    public void Decode<TPixel>(Buffer2D<TPixel> pixels, int width, int height)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (var decoder = new Vp8LDecoder(width, height, this.memoryAllocator))
        {
            this.DecodeImageStream(decoder, width, height, true);
            this.DecodeImageData(decoder, decoder.Pixels.Memory.Span);
            this.DecodePixelValues(decoder, pixels, width, height);
        }
    }

    public IMemoryOwner<uint> DecodeImageStream(Vp8LDecoder decoder, int xSize, int ySize, bool isLevel0)
    {
        var transformXSize = xSize;
        var transformYSize = ySize;
        var numberOfTransformsPresent = 0;
        if (isLevel0)
        {
            decoder.Transforms = new List<Vp8LTransform>(WebpConstants.MaxNumberOfTransforms);

            // Next bit indicates, if a transformation is present.
            while (this.bitReader.ReadBit())
            {
                if (numberOfTransformsPresent > WebpConstants.MaxNumberOfTransforms)
                {
                    WebpThrowHelper.ThrowImageFormatException($"The maximum number of transforms of {WebpConstants.MaxNumberOfTransforms} was exceeded");
                }

                this.ReadTransformation(transformXSize, transformYSize, decoder);
                if (decoder.Transforms[numberOfTransformsPresent].TransformType == Vp8LTransformType.ColorIndexingTransform)
                {
                    transformXSize = LosslessUtils.SubSampleSize(transformXSize, decoder.Transforms[numberOfTransformsPresent].Bits);
                }

                numberOfTransformsPresent++;
            }
        }
        else
        {
            decoder.Metadata = new Vp8LMetadata();
        }

        // Color cache.
        var isColorCachePresent = this.bitReader.ReadBit();
        var colorCacheBits = 0;
        var colorCacheSize = 0;
        if (isColorCachePresent)
        {
            colorCacheBits = (int)this.bitReader.ReadValue(4);

            // Note: According to webpinfo color cache bits of 11 are valid, even though 10 is defined in the source code as maximum.
            // That is why 11 bits is also considered valid here.
            var colorCacheBitsIsValid = colorCacheBits is >= 1 and <= WebpConstants.MaxColorCacheBits + 1;
            if (!colorCacheBitsIsValid)
            {
                WebpThrowHelper.ThrowImageFormatException("Invalid color cache bits found");
            }
        }

        // Read the Huffman codes (may recurse).
        this.ReadHuffmanCodes(decoder, transformXSize, transformYSize, colorCacheBits, isLevel0);
        decoder.Metadata.ColorCacheSize = colorCacheSize;

        // Finish setting up the color-cache.
        if (isColorCachePresent)
        {
            decoder.Metadata.ColorCache = new ColorCache();
            colorCacheSize = 1 << colorCacheBits;
            decoder.Metadata.ColorCacheSize = colorCacheSize;
            decoder.Metadata.ColorCache.Init(colorCacheBits);
        }
        else
        {
            decoder.Metadata.ColorCacheSize = 0;
        }

        this.UpdateDecoder(decoder, transformXSize, transformYSize);
        if (isLevel0)
        {
            // level 0 complete.
            return null;
        }

        // Use the Huffman trees to decode the LZ77 encoded data.
        var pixelData = this.memoryAllocator.Allocate<uint>(decoder.Width * decoder.Height, AllocationOptions.Clean);
        this.DecodeImageData(decoder, pixelData.GetSpan());

        return pixelData;
    }

    private void DecodePixelValues<TPixel>(Vp8LDecoder decoder, Buffer2D<TPixel> pixels, int width, int height)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var pixelData = decoder.Pixels.GetSpan();

        // Apply reverse transformations, if any are present.
        ApplyInverseTransforms(decoder, pixelData, this.memoryAllocator);

        var pixelDataAsBytes = MemoryMarshal.Cast<uint, byte>(pixelData);
        var bytesPerRow = width * 4;
        for (var y = 0; y < height; y++)
        {
            var rowAsBytes = pixelDataAsBytes.Slice(y * bytesPerRow, bytesPerRow);
            var pixelRow = pixels.DangerousGetRowSpan(y);
            PixelOperations<TPixel>.Instance.FromBgra32Bytes(
                this.configuration,
                rowAsBytes.Slice(0, bytesPerRow),
                pixelRow.Slice(0, width),
                width);
        }
    }

    public void DecodeImageData(Vp8LDecoder decoder, Span<uint> pixelData)
    {
        var lastPixel = 0;
        var width = decoder.Width;
        var height = decoder.Height;
        var row = lastPixel / width;
        var col = lastPixel % width;
        const int lenCodeLimit = WebpConstants.NumLiteralCodes + WebpConstants.NumLengthCodes;
        var colorCacheSize = decoder.Metadata.ColorCacheSize;
        var colorCache = decoder.Metadata.ColorCache;
        var colorCacheLimit = lenCodeLimit + colorCacheSize;
        var mask = decoder.Metadata.HuffmanMask;
        var hTreeGroup = GetHTreeGroupForPos(decoder.Metadata, col, row);

        var totalPixels = width * height;
        var decodedPixels = 0;
        var lastCached = decodedPixels;
        while (decodedPixels < totalPixels)
        {
            int code;
            if ((col & mask) == 0)
            {
                hTreeGroup = GetHTreeGroupForPos(decoder.Metadata, col, row);
            }

            if (hTreeGroup[0].IsTrivialCode)
            {
                pixelData[decodedPixels] = hTreeGroup[0].LiteralArb;
                this.AdvanceByOne(ref col, ref row, width, colorCache, ref decodedPixels, pixelData, ref lastCached);
                continue;
            }

            this.bitReader.FillBitWindow();
            if (hTreeGroup[0].UsePackedTable)
            {
                code = (int)this.ReadPackedSymbols(hTreeGroup, pixelData, decodedPixels);
                if (this.bitReader.IsEndOfStream())
                {
                    break;
                }

                if (code == PackedNonLiteralCode)
                {
                    this.AdvanceByOne(ref col, ref row, width, colorCache, ref decodedPixels, pixelData, ref lastCached);
                    continue;
                }
            }
            else
            {
                code = (int)this.ReadSymbol(hTreeGroup[0].HTrees[HuffIndex.Green]);
            }

            if (this.bitReader.IsEndOfStream())
            {
                break;
            }

            // Literal
            if (code < WebpConstants.NumLiteralCodes)
            {
                if (hTreeGroup[0].IsTrivialLiteral)
                {
                    pixelData[decodedPixels] = hTreeGroup[0].LiteralArb | ((uint)code << 8);
                }
                else
                {
                    var red = this.ReadSymbol(hTreeGroup[0].HTrees[HuffIndex.Red]);
                    this.bitReader.FillBitWindow();
                    var blue = this.ReadSymbol(hTreeGroup[0].HTrees[HuffIndex.Blue]);
                    var alpha = this.ReadSymbol(hTreeGroup[0].HTrees[HuffIndex.Alpha]);
                    if (this.bitReader.IsEndOfStream())
                    {
                        break;
                    }

                    pixelData[decodedPixels] = (uint)(((byte)alpha << 24) | ((byte)red << 16) | ((byte)code << 8) | (byte)blue);
                }

                this.AdvanceByOne(ref col, ref row, width, colorCache, ref decodedPixels, pixelData, ref lastCached);
            }
            else if (code < lenCodeLimit)
            {
                // Backward reference is used.
                var lengthSym = code - WebpConstants.NumLiteralCodes;
                var length = this.GetCopyLength(lengthSym);
                var distSymbol = this.ReadSymbol(hTreeGroup[0].HTrees[HuffIndex.Dist]);
                this.bitReader.FillBitWindow();
                var distCode = this.GetCopyDistance((int)distSymbol);
                var dist = PlaneCodeToDistance(width, distCode);
                if (this.bitReader.IsEndOfStream())
                {
                    break;
                }

                CopyBlock(pixelData, decodedPixels, dist, length);
                decodedPixels += length;
                col += length;
                while (col >= width)
                {
                    col -= width;
                    row++;
                }

                if ((col & mask) != 0)
                {
                    hTreeGroup = GetHTreeGroupForPos(decoder.Metadata, col, row);
                }

                if (colorCache != null)
                {
                    while (lastCached < decodedPixels)
                    {
                        colorCache.Insert(pixelData[lastCached]);
                        lastCached++;
                    }
                }
            }
            else if (code < colorCacheLimit)
            {
                // Color cache should be used.
                var key = code - lenCodeLimit;
                while (lastCached < decodedPixels)
                {
                    colorCache.Insert(pixelData[lastCached]);
                    lastCached++;
                }

                pixelData[decodedPixels] = colorCache.Lookup(key);
                this.AdvanceByOne(ref col, ref row, width, colorCache, ref decodedPixels, pixelData, ref lastCached);
            }
            else
            {
                WebpThrowHelper.ThrowImageFormatException("Webp parsing error");
            }
        }
    }

    private void AdvanceByOne(ref int col, ref int row, int width, ColorCache colorCache, ref int decodedPixels, Span<uint> pixelData, ref int lastCached)
    {
        col++;
        decodedPixels++;
        if (col >= width)
        {
            col = 0;
            row++;

            if (colorCache != null)
            {
                while (lastCached < decodedPixels)
                {
                    colorCache.Insert(pixelData[lastCached]);
                    lastCached++;
                }
            }
        }
    }

    private void ReadHuffmanCodes(Vp8LDecoder decoder, int xSize, int ySize, int colorCacheBits, bool allowRecursion)
    {
        var maxAlphabetSize = 0;
        var numHTreeGroups = 1;
        var numHTreeGroupsMax = 1;

        // If the next bit is zero, there is only one meta Huffman code used everywhere in the image. No more data is stored.
        // If this bit is one, the image uses multiple meta Huffman codes. These meta Huffman codes are stored as an entropy image.
        if (allowRecursion && this.bitReader.ReadBit())
        {
            // Use meta Huffman codes.
            var huffmanPrecision = (int)(this.bitReader.ReadValue(3) + 2);
            var huffmanXSize = LosslessUtils.SubSampleSize(xSize, huffmanPrecision);
            var huffmanYSize = LosslessUtils.SubSampleSize(ySize, huffmanPrecision);
            var huffmanPixels = huffmanXSize * huffmanYSize;

            var huffmanImage = this.DecodeImageStream(decoder, huffmanXSize, huffmanYSize, false);
            var huffmanImageSpan = huffmanImage.GetSpan();
            decoder.Metadata.HuffmanSubSampleBits = huffmanPrecision;

            // TODO: Isn't huffmanPixels the length of the span?
            for (var i = 0; i < huffmanPixels; i++)
            {
                // The huffman data is stored in red and green bytes.
                var group = (huffmanImageSpan[i] >> 8) & 0xffff;
                huffmanImageSpan[i] = group;
                if (group >= numHTreeGroupsMax)
                {
                    numHTreeGroupsMax = (int)group + 1;
                }
            }

            numHTreeGroups = numHTreeGroupsMax;
            decoder.Metadata.HuffmanImage = huffmanImage;
        }

        // Find maximum alphabet size for the hTree group.
        for (var j = 0; j < WebpConstants.HuffmanCodesPerMetaCode; j++)
        {
            var alphabetSize = WebpConstants.AlphabetSize[j];
            if (j == 0 && colorCacheBits > 0)
            {
                alphabetSize += 1 << colorCacheBits;
            }

            if (maxAlphabetSize < alphabetSize)
            {
                maxAlphabetSize = alphabetSize;
            }
        }

        var tableSize = TableSize[colorCacheBits];
        var huffmanTables = new HuffmanCode[numHTreeGroups * tableSize];
        var hTreeGroups = new HTreeGroup[numHTreeGroups];
        var huffmanTable = huffmanTables.AsSpan();
        var codeLengths = new int[maxAlphabetSize];
        for (var i = 0; i < numHTreeGroupsMax; i++)
        {
            hTreeGroups[i] = new HTreeGroup(HuffmanUtils.HuffmanPackedTableSize);
            var hTreeGroup = hTreeGroups[i];
            var totalSize = 0;
            var isTrivialLiteral = true;
            var maxBits = 0;
            codeLengths.AsSpan().Clear();
            for (var j = 0; j < WebpConstants.HuffmanCodesPerMetaCode; j++)
            {
                var alphabetSize = WebpConstants.AlphabetSize[j];
                if (j == 0 && colorCacheBits > 0)
                {
                    alphabetSize += 1 << colorCacheBits;
                }

                var size = this.ReadHuffmanCode(alphabetSize, codeLengths, huffmanTable);
                if (size == 0)
                {
                    WebpThrowHelper.ThrowImageFormatException("Huffman table size is zero");
                }

                // TODO: Avoid allocation.
                hTreeGroup.HTrees.Add(huffmanTable.Slice(0, size).ToArray());

                var huffTableZero = huffmanTable[0];
                if (isTrivialLiteral && LiteralMap[j] == 1)
                {
                    isTrivialLiteral = huffTableZero.BitsUsed == 0;
                }

                totalSize += huffTableZero.BitsUsed;
                huffmanTable = huffmanTable.Slice(size);

                if (j <= HuffIndex.Alpha)
                {
                    var localMaxBits = codeLengths[0];
                    int k;
                    for (k = 1; k < alphabetSize; ++k)
                    {
                        var codeLengthK = codeLengths[k];
                        if (codeLengthK > localMaxBits)
                        {
                            localMaxBits = codeLengthK;
                        }
                    }

                    maxBits += localMaxBits;
                }
            }

            hTreeGroup.IsTrivialLiteral = isTrivialLiteral;
            hTreeGroup.IsTrivialCode = false;
            if (isTrivialLiteral)
            {
                var red = hTreeGroup.HTrees[HuffIndex.Red][0].Value;
                var blue = hTreeGroup.HTrees[HuffIndex.Blue][0].Value;
                var green = hTreeGroup.HTrees[HuffIndex.Green][0].Value;
                var alpha = hTreeGroup.HTrees[HuffIndex.Alpha][0].Value;
                hTreeGroup.LiteralArb = (alpha << 24) | (red << 16) | blue;
                if (totalSize == 0 && green < WebpConstants.NumLiteralCodes)
                {
                    hTreeGroup.IsTrivialCode = true;
                    hTreeGroup.LiteralArb |= green << 8;
                }
            }

            hTreeGroup.UsePackedTable = !hTreeGroup.IsTrivialCode && maxBits < HuffmanUtils.HuffmanPackedBits;
            if (hTreeGroup.UsePackedTable)
            {
                this.BuildPackedTable(hTreeGroup);
            }
        }

        decoder.Metadata.NumHTreeGroups = numHTreeGroups;
        decoder.Metadata.HTreeGroups = hTreeGroups;
        decoder.Metadata.HuffmanTables = huffmanTables;
    }

    private int ReadHuffmanCode(int alphabetSize, int[] codeLengths, Span<HuffmanCode> table)
    {
        var simpleCode = this.bitReader.ReadBit();
        for (var i = 0; i < alphabetSize; i++)
        {
            codeLengths[i] = 0;
        }

        if (simpleCode)
        {
            // (i) Simple Code Length Code.
            // This variant is used in the special case when only 1 or 2 Huffman code lengths are non-zero,
            // and are in the range of[0, 255]. All other Huffman code lengths are implicitly zeros.

            // Read symbols, codes & code lengths directly.
            var numSymbols = this.bitReader.ReadValue(1) + 1;
            var firstSymbolLenCode = this.bitReader.ReadValue(1);

            // The first code is either 1 bit or 8 bit code.
            var symbol = this.bitReader.ReadValue(firstSymbolLenCode == 0 ? 1 : 8);
            codeLengths[symbol] = 1;

            // The second code (if present), is always 8 bit long.
            if (numSymbols == 2)
            {
                symbol = this.bitReader.ReadValue(8);
                codeLengths[symbol] = 1;
            }
        }
        else
        {
            // (ii) Normal Code Length Code:
            // The code lengths of a Huffman code are read as follows: num_code_lengths specifies the number of code lengths;
            // the rest of the code lengths (according to the order in kCodeLengthCodeOrder) are zeros.
            var codeLengthCodeLengths = new int[NumCodeLengthCodes];
            var numCodes = this.bitReader.ReadValue(4) + 4;
            if (numCodes > NumCodeLengthCodes)
            {
                WebpThrowHelper.ThrowImageFormatException("Bitstream error, numCodes has an invalid value");
            }

            for (var i = 0; i < numCodes; i++)
            {
                codeLengthCodeLengths[CodeLengthCodeOrder[i]] = (int)this.bitReader.ReadValue(3);
            }

            this.ReadHuffmanCodeLengths(table, codeLengthCodeLengths, alphabetSize, codeLengths);
        }

        var size = HuffmanUtils.BuildHuffmanTable(table, HuffmanUtils.HuffmanTableBits, codeLengths, alphabetSize);

        return size;
    }

    private void ReadHuffmanCodeLengths(Span<HuffmanCode> table, int[] codeLengthCodeLengths, int numSymbols, int[] codeLengths)
    {
        int maxSymbol;
        var symbol = 0;
        var prevCodeLen = WebpConstants.DefaultCodeLength;
        var size = HuffmanUtils.BuildHuffmanTable(table, WebpConstants.LengthTableBits, codeLengthCodeLengths, NumCodeLengthCodes);
        if (size == 0)
        {
            WebpThrowHelper.ThrowImageFormatException("Error building huffman table");
        }

        if (this.bitReader.ReadBit())
        {
            var lengthNBits = 2 + (2 * (int)this.bitReader.ReadValue(3));
            maxSymbol = 2 + (int)this.bitReader.ReadValue(lengthNBits);
        }
        else
        {
            maxSymbol = numSymbols;
        }

        while (symbol < numSymbols)
        {
            if (maxSymbol-- == 0)
            {
                break;
            }

            this.bitReader.FillBitWindow();
            var prefetchBits = this.bitReader.PrefetchBits();
            var idx = (int)(prefetchBits & 127);
            var huffmanCode = table[idx];
            this.bitReader.AdvanceBitPosition(huffmanCode.BitsUsed);
            var codeLen = huffmanCode.Value;
            if (codeLen < WebpConstants.CodeLengthLiterals)
            {
                codeLengths[symbol++] = (int)codeLen;
                if (codeLen != 0)
                {
                    prevCodeLen = (int)codeLen;
                }
            }
            else
            {
                var usePrev = codeLen == WebpConstants.CodeLengthRepeatCode;
                var slot = codeLen - WebpConstants.CodeLengthLiterals;
                var extraBits = WebpConstants.CodeLengthExtraBits[slot];
                var repeatOffset = WebpConstants.CodeLengthRepeatOffsets[slot];
                var repeat = (int)(this.bitReader.ReadValue(extraBits) + repeatOffset);
                if (symbol + repeat > numSymbols)
                {
                    return;
                }

                var length = usePrev ? prevCodeLen : 0;
                while (repeat-- > 0)
                {
                    codeLengths[symbol++] = length;
                }
            }
        }
    }

    /// <summary>
    /// Reads the transformations, if any are present.
    /// </summary>
    /// <param name="xSize">The width of the image.</param>
    /// <param name="ySize">The height of the image.</param>
    /// <param name="decoder">Vp8LDecoder where the transformations will be stored.</param>
    private void ReadTransformation(int xSize, int ySize, Vp8LDecoder decoder)
    {
        var transformType = (Vp8LTransformType)this.bitReader.ReadValue(2);
        var transform = new Vp8LTransform(transformType, xSize, ySize);

        // Each transform is allowed to be used only once.
        foreach (var decoderTransform in decoder.Transforms)
        {
            if (decoderTransform.TransformType == transform.TransformType)
            {
                WebpThrowHelper.ThrowImageFormatException("Each transform can only be present once");
            }
        }

        switch (transformType)
        {
            case Vp8LTransformType.SubtractGreen:
                // There is no data associated with this transform.
                break;
            case Vp8LTransformType.ColorIndexingTransform:
                // The transform data contains color table size and the entries in the color table.
                // 8 bit value for color table size.
                var numColors = this.bitReader.ReadValue(8) + 1;
                var bits = numColors > 16 ? 0
                    : numColors > 4 ? 1
                    : numColors > 2 ? 2
                    : 3;
                transform.Bits = bits;
                using (var colorMap = this.DecodeImageStream(decoder, (int)numColors, 1, false))
                {
                    var finalNumColors = 1 << (8 >> transform.Bits);
                    var newColorMap = this.memoryAllocator.Allocate<uint>(finalNumColors, AllocationOptions.Clean);
                    LosslessUtils.ExpandColorMap((int)numColors, colorMap.GetSpan(), newColorMap.GetSpan());
                    transform.Data = newColorMap;
                }

                break;

            case Vp8LTransformType.PredictorTransform:
            case Vp8LTransformType.CrossColorTransform:
            {
                // The first 3 bits of prediction data define the block width and height in number of bits.
                transform.Bits = (int)this.bitReader.ReadValue(3) + 2;
                var blockWidth = LosslessUtils.SubSampleSize(transform.XSize, transform.Bits);
                var blockHeight = LosslessUtils.SubSampleSize(transform.YSize, transform.Bits);
                var transformData = this.DecodeImageStream(decoder, blockWidth, blockHeight, false);
                transform.Data = transformData;
                break;
            }
        }

        decoder.Transforms.Add(transform);
    }

    /// <summary>
    /// A Webp lossless image can go through four different types of transformation before being entropy encoded.
    /// This will reverse the transformations, if any are present.
    /// </summary>
    /// <param name="decoder">The decoder holding the transformation infos.</param>
    /// <param name="pixelData">The pixel data to apply the transformation.</param>
    /// <param name="memoryAllocator">The memory allocator is needed to allocate memory during the predictor transform.</param>
    public static void ApplyInverseTransforms(Vp8LDecoder decoder, Span<uint> pixelData, MemoryAllocator memoryAllocator)
    {
        var transforms = decoder.Transforms;
        for (var i = transforms.Count - 1; i >= 0; i--)
        {
            var transform = transforms[i];
            var transformType = transform.TransformType;
            switch (transformType)
            {
                case Vp8LTransformType.PredictorTransform:
                    using (var output = memoryAllocator.Allocate<uint>(pixelData.Length, AllocationOptions.Clean))
                    {
                        LosslessUtils.PredictorInverseTransform(transform, pixelData, output.GetSpan());
                    }

                    break;
                case Vp8LTransformType.SubtractGreen:
                    LosslessUtils.AddGreenToBlueAndRed(pixelData);
                    break;
                case Vp8LTransformType.CrossColorTransform:
                    LosslessUtils.ColorSpaceInverseTransform(transform, pixelData);
                    break;
                case Vp8LTransformType.ColorIndexingTransform:
                    LosslessUtils.ColorIndexInverseTransform(transform, pixelData);
                    break;
            }
        }
    }

    /// <summary>
    /// The alpha channel of a lossy webp image can be compressed using the lossless webp compression.
    /// This method will undo the compression.
    /// </summary>
    /// <param name="dec">The alpha decoder.</param>
    public void DecodeAlphaData(AlphaDecoder dec)
    {
        var pixelData = dec.Vp8LDec.Pixels.Memory.Span;
        var data = MemoryMarshal.Cast<uint, byte>(pixelData);
        var row = 0;
        var col = 0;
        var vp8LDec = dec.Vp8LDec;
        var width = vp8LDec.Width;
        var height = vp8LDec.Height;
        var hdr = vp8LDec.Metadata;
        var pos = 0; // Current position.
        var end = width * height; // End of data.
        var last = end; // Last pixel to decode.
        var lastRow = height;
        const int lenCodeLimit = WebpConstants.NumLiteralCodes + WebpConstants.NumLengthCodes;
        var mask = hdr.HuffmanMask;
        var htreeGroup = pos < last ? GetHTreeGroupForPos(hdr, col, row) : null;
        while (!this.bitReader.Eos && pos < last)
        {
            // Only update when changing tile.
            if ((col & mask) == 0)
            {
                htreeGroup = GetHTreeGroupForPos(hdr, col, row);
            }

            this.bitReader.FillBitWindow();
            var code = (int)this.ReadSymbol(htreeGroup[0].HTrees[HuffIndex.Green]);
            if (code < WebpConstants.NumLiteralCodes)
            {
                // Literal
                data[pos] = (byte)code;
                ++pos;
                ++col;

                if (col >= width)
                {
                    col = 0;
                    ++row;
                    if (row <= lastRow && row % WebpConstants.NumArgbCacheRows == 0)
                    {
                        dec.ExtractPalettedAlphaRows(row);
                    }
                }
            }
            else if (code < lenCodeLimit)
            {
                // Backward reference
                var lengthSym = code - WebpConstants.NumLiteralCodes;
                var length = this.GetCopyLength(lengthSym);
                var distSymbol = (int)this.ReadSymbol(htreeGroup[0].HTrees[HuffIndex.Dist]);
                this.bitReader.FillBitWindow();
                var distCode = this.GetCopyDistance(distSymbol);
                var dist = PlaneCodeToDistance(width, distCode);
                if (pos >= dist && end - pos >= length)
                {
                    CopyBlock8B(data, pos, dist, length);
                }
                else
                {
                    WebpThrowHelper.ThrowImageFormatException("error while decoding alpha data");
                }

                pos += length;
                col += length;
                while (col >= width)
                {
                    col -= width;
                    ++row;
                    if (row <= lastRow && row % WebpConstants.NumArgbCacheRows == 0)
                    {
                        dec.ExtractPalettedAlphaRows(row);
                    }
                }

                if (pos < last && (col & mask) > 0)
                {
                    htreeGroup = GetHTreeGroupForPos(hdr, col, row);
                }
            }
            else
            {
                WebpThrowHelper.ThrowImageFormatException("bitstream error while parsing alpha data");
            }

            this.bitReader.Eos = this.bitReader.IsEndOfStream();
        }

        // Process the remaining rows corresponding to last row-block.
        dec.ExtractPalettedAlphaRows(row > lastRow ? lastRow : row);
    }

    private void UpdateDecoder(Vp8LDecoder decoder, int width, int height)
    {
        var numBits = decoder.Metadata.HuffmanSubSampleBits;
        decoder.Width = width;
        decoder.Height = height;
        decoder.Metadata.HuffmanXSize = LosslessUtils.SubSampleSize(width, numBits);
        decoder.Metadata.HuffmanMask = numBits == 0 ? ~0 : (1 << numBits) - 1;
    }

    private uint ReadPackedSymbols(Span<HTreeGroup> group, Span<uint> pixelData, int decodedPixels)
    {
        var val = (uint)(this.bitReader.PrefetchBits() & (HuffmanUtils.HuffmanPackedTableSize - 1));
        var code = group[0].PackedTable[val];
        if (code.BitsUsed < BitsSpecialMarker)
        {
            this.bitReader.AdvanceBitPosition(code.BitsUsed);
            pixelData[decodedPixels] = code.Value;
            return PackedNonLiteralCode;
        }

        this.bitReader.AdvanceBitPosition(code.BitsUsed - BitsSpecialMarker);

        return code.Value;
    }

    private void BuildPackedTable(HTreeGroup hTreeGroup)
    {
        for (uint code = 0; code < HuffmanUtils.HuffmanPackedTableSize; code++)
        {
            var bits = code;
            ref var huff = ref hTreeGroup.PackedTable[bits];
            var hCode = hTreeGroup.HTrees[HuffIndex.Green][bits];
            if (hCode.Value >= WebpConstants.NumLiteralCodes)
            {
                huff.BitsUsed = hCode.BitsUsed + BitsSpecialMarker;
                huff.Value = hCode.Value;
            }
            else
            {
                huff.BitsUsed = 0;
                huff.Value = 0;
                bits >>= AccumulateHCode(hCode, 8, ref huff);
                bits >>= AccumulateHCode(hTreeGroup.HTrees[HuffIndex.Red][bits], 16, ref huff);
                bits >>= AccumulateHCode(hTreeGroup.HTrees[HuffIndex.Blue][bits], 0, ref huff);
                bits >>= AccumulateHCode(hTreeGroup.HTrees[HuffIndex.Alpha][bits], 24, ref huff);
            }
        }
    }

    /// <summary>
    /// Decodes the next Huffman code from the bit-stream.
    /// FillBitWindow() needs to be called at minimum every second call to ReadSymbol, in order to pre-fetch enough bits.
    /// </summary>
    private uint ReadSymbol(Span<HuffmanCode> table)
    {
        var val = (uint)this.bitReader.PrefetchBits();
        var tableSpan = table.Slice((int)(val & HuffmanUtils.HuffmanTableMask));
        var nBits = tableSpan[0].BitsUsed - HuffmanUtils.HuffmanTableBits;
        if (nBits > 0)
        {
            this.bitReader.AdvanceBitPosition(HuffmanUtils.HuffmanTableBits);
            val = (uint)this.bitReader.PrefetchBits();
            tableSpan = tableSpan.Slice((int)tableSpan[0].Value);
            tableSpan = tableSpan.Slice((int)val & ((1 << nBits) - 1));
        }

        this.bitReader.AdvanceBitPosition(tableSpan[0].BitsUsed);

        return tableSpan[0].Value;
    }

    [MethodImpl(InliningOptions.ShortMethod)]
    private int GetCopyLength(int lengthSymbol) =>
        this.GetCopyDistance(lengthSymbol); // Length and distance prefixes are encoded the same way.

    private int GetCopyDistance(int distanceSymbol)
    {
        if (distanceSymbol < 4)
        {
            return distanceSymbol + 1;
        }

        var extraBits = (distanceSymbol - 2) >> 1;
        var offset = (2 + (distanceSymbol & 1)) << extraBits;

        return (int)(offset + this.bitReader.ReadValue(extraBits) + 1);
    }

    [MethodImpl(InliningOptions.ShortMethod)]
    private static Span<HTreeGroup> GetHTreeGroupForPos(Vp8LMetadata metadata, int x, int y)
    {
        var metaIndex = GetMetaIndex(metadata.HuffmanImage, metadata.HuffmanXSize, metadata.HuffmanSubSampleBits, x, y);
        return metadata.HTreeGroups.AsSpan((int)metaIndex);
    }

    [MethodImpl(InliningOptions.ShortMethod)]
    private static uint GetMetaIndex(IMemoryOwner<uint> huffmanImage, int xSize, int bits, int x, int y)
    {
        if (bits is 0)
        {
            return 0;
        }

        var huffmanImageSpan = huffmanImage.GetSpan();
        return huffmanImageSpan[(xSize * (y >> bits)) + (x >> bits)];
    }

    private static int PlaneCodeToDistance(int xSize, int planeCode)
    {
        if (planeCode > CodeToPlaneCodes)
        {
            return planeCode - CodeToPlaneCodes;
        }

        var distCode = WebpLookupTables.CodeToPlane[planeCode - 1];
        var yOffset = distCode >> 4;
        var xOffset = 8 - (distCode & 0xf);
        var dist = (yOffset * xSize) + xOffset;

        // dist < 1 can happen if xSize is very small.
        return dist >= 1 ? dist : 1;
    }

    /// <summary>
    /// Copies pixels when a backward reference is used.
    /// Copy 'length' number of pixels (in scan-line order) from the sequence of pixels prior to them by 'dist' pixels.
    /// </summary>
    /// <param name="pixelData">The pixel data.</param>
    /// <param name="decodedPixels">The number of so far decoded pixels.</param>
    /// <param name="dist">The backward reference distance prior to the current decoded pixel.</param>
    /// <param name="length">The number of pixels to copy.</param>
    private static void CopyBlock(Span<uint> pixelData, int decodedPixels, int dist, int length)
    {
        var start = decodedPixels - dist;
        if (start < 0)
        {
            WebpThrowHelper.ThrowImageFormatException("webp image data seems to be invalid");
        }

        if (dist >= length)
        {
            // no overlap.
            var src = pixelData.Slice(start, length);
            var dest = pixelData.Slice(decodedPixels);
            src.CopyTo(dest);
        }
        else
        {
            // There is overlap between the backward reference distance and the pixels to copy.
            var src = pixelData.Slice(start);
            var dest = pixelData.Slice(decodedPixels);
            for (var i = 0; i < length; i++)
            {
                dest[i] = src[i];
            }
        }
    }

    /// <summary>
    /// Copies alpha values when a backward reference is used.
    /// Copy 'length' number of alpha values from the sequence of alpha values prior to them by 'dist'.
    /// </summary>
    /// <param name="data">The alpha values.</param>
    /// <param name="pos">The position of the so far decoded pixels.</param>
    /// <param name="dist">The backward reference distance prior to the current decoded pixel.</param>
    /// <param name="length">The number of pixels to copy.</param>
    private static void CopyBlock8B(Span<byte> data, int pos, int dist, int length)
    {
        if (dist >= length)
        {
            // no overlap.
            data.Slice(pos - dist, length).CopyTo(data.Slice(pos));
        }
        else
        {
            var dst = data.Slice(pos);
            var src = data.Slice(pos - dist);
            for (var i = 0; i < length; i++)
            {
                dst[i] = src[i];
            }
        }
    }

    [MethodImpl(InliningOptions.ShortMethod)]
    private static int AccumulateHCode(HuffmanCode hCode, int shift, ref HuffmanCode huff)
    {
        huff.BitsUsed += hCode.BitsUsed;
        huff.Value |= hCode.Value << shift;
        return hCode.BitsUsed;
    }
}